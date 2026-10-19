using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Pins the most critical use case: creating an order (recipe check, price snapshot,
/// kitchen ticket, stock deduction, transaction) plus status/promotion updates.
/// </summary>
public sealed class OrdersFlowTests : TestBase
{
    private static CreateOrderRequest Request(int tableId, int createdBy, int sizeId, int quantity = 2, int? toppingId = null, int toppingQuanlity = 1)
    {
        var item = new CreateOrderItemRequest
        {
            SizeId = sizeId,
            Quanlity = quantity,
            Note = "no onion"
        };
        if (toppingId.HasValue)
        {
            item.Toppings.Add(new CreateOrderToppingRequest { ToppingId = toppingId.Value, Quanlity = toppingQuanlity });
        }
        var request = new CreateOrderRequest { TableId = tableId, CreatedBy = createdBy };
        request.Items.Add(item);
        return request;
    }

    private static UpdateOrderItemsRequest UpdateRequest(
        int changedBy,
        int sizeId,
        int quantity = 2,
        int? toppingId = null,
        int toppingQuanlity = 1)
    {
        var item = new CreateOrderItemRequest
        {
            SizeId = sizeId,
            Quanlity = quantity,
            Note = "updated"
        };
        if (toppingId.HasValue)
        {
            item.Toppings.Add(new CreateOrderToppingRequest { ToppingId = toppingId.Value, Quanlity = toppingQuanlity });
        }

        var request = new UpdateOrderItemsRequest { ChangedBy = changedBy };
        request.Items.Add(item);
        return request;
    }

    [Fact]
    public async Task Create_SnapshotsPrice_CreatesKitchenTicket_AndDeductsStock()
    {
        var s = SeedOrderScenario(sizePrice: 50m, toppingPrice: 5m, stock: 100m);

        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2, toppingId: s.Topping.ToppingId, toppingQuanlity: 1),
            CancellationToken.None);

        // Subtotal = (size 50 * 2) + (topping 5 * 1 * 2) = 110
        Assert.Equal(110m, order.Subtotal);
        Assert.Equal(110m, order.TotalAmount);
        Assert.Equal("SentToKitchen", order.Status);
        Assert.Equal(s.Table.TableId, order.TableId);
        Assert.Equal(s.Staff.Id, order.CreatedBy);

        var item = Db.OrderItems.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(55m, item.UnitPrice); // 50 base + 5 topping, snapshot at creation time
        Assert.Equal(2, item.Quanlity);
        // Defect #21: UnitPrice là đơn giá cuối cùng (đã gồm topping) và Subtotal phải
        // khớp chính xác với UnitPrice * quantity, không cộng topping thêm lần nữa.
        Assert.Equal(order.Subtotal, item.UnitPrice * item.Quanlity);
        Assert.Equal("Pending", item.Status);
        Assert.Equal("no onion", item.Note);

        var itemTopping = Db.OrderItemToppings.Single(x => x.OrderItemId == item.Id);
        Assert.Equal(5m, itemTopping.UnitPrice);
        Assert.Equal(1, itemTopping.Quanlity);

        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Pending", kitchenOrder.Status);
        Assert.Equal(0, kitchenOrder.Priority);
        Assert.True(kitchenOrder.SentAt > DateTime.UtcNow.AddMinutes(-5));

        var kitchenItem = Db.KitchenOrderItems.Single(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId);
        Assert.Equal(item.Id, kitchenItem.OrderItemId);
        Assert.Equal(2, kitchenItem.Quanlity);
        Assert.Equal("Pending", kitchenItem.Status);

        // Recipe uses 2 units of the ingredient per food unit x 2 ordered = 4
        var stock = Db.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId);
        Assert.Equal(96m, stock.Quanlity);

        var transaction = Db.InventoryTransactions.Single();
        Assert.Equal("Sale", transaction.Type);
        Assert.Equal(4m, transaction.Quanlity);
        Assert.Equal(100m, transaction.QuanlityBefore);
        Assert.Equal(96m, transaction.QuanlityAfter);
        Assert.Equal("Order", transaction.ReferenceType);
        Assert.Equal(order.OrderId, transaction.ReferenceId);
        Assert.Equal("Automatically deducted when order was sent to kitchen", transaction.Note);
        Assert.Equal(s.Staff.Id, transaction.CreatedBy);

        // Table becomes occupied
        var table = Db.RestaurantTables.Single(x => x.TableId == s.Table.TableId);
        Assert.Equal("Occupied", table.Status);
    }

    [Fact]
    public async Task Create_AggregatesIngredientRequirements_ForMultipleItems()
    {
        var s = SeedOrderScenario(sizePrice: 10m, toppingPrice: 1m, stock: 100m);

        var request = new CreateOrderRequest { TableId = s.Table.TableId, CreatedBy = s.Staff.Id };
        request.Items.Add(new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 });
        request.Items.Add(new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 3 });

        var order = await Orders.CreateAsync(request, CancellationToken.None);

        Assert.Equal(50m, order.Subtotal);
        Assert.Equal(90m, Db.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity); // 2*5 = 10 consumed
        Assert.Equal(2, Db.KitchenOrderItems.Count(x => x.KitchenOrderId == Db.KitchenOrders.Single(k => k.OrderId == order.OrderId).KitchenOrderId));
    }

    [Fact]
    public async Task Create_Throws_WhenTableIsMissingOrInactive()
    {
        var s = SeedOrderScenario();
        s.Table.IsActive = false;
        Db.SaveChanges();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId), CancellationToken.None));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Orders.CreateAsync(
            Request(999999, s.Staff.Id, s.Size.SizeId), CancellationToken.None));
    }

    [Fact]
    public async Task Create_Throws_WhenSizeHasNoActiveRecipe()
    {
        var s = SeedOrderScenario();
        var food = SeedFood(s.Category.CategoryId);
        var orphanSize = SeedSize(food.FoodId, 20m, name: "S");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, orphanSize.SizeId), CancellationToken.None));

        Assert.Equal($"Food size {orphanSize.SizeId} does not have an active recipe", ex.Message);
    }

    [Fact]
    public async Task Create_Throws_WhenStockIsInsufficient_AndPersistsNothing()
    {
        var s = SeedOrderScenario(stock: 1m); // needs 4

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId), CancellationToken.None));

        Assert.Contains("Insufficient ingredient", ex.Message);
        using var fresh = NewContext();
        Assert.Empty(fresh.Orders);
        Assert.Empty(fresh.KitchenOrders);
        Assert.Equal(1m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task Create_Throws_WhenIngredientHasNoInventoryRecord()
    {
        var s = SeedOrderScenario();
        Db.Inventories.RemoveRange(Db.Inventories);
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId), CancellationToken.None));

        Assert.Contains("No inventory record exists for ingredient", ex.Message);
    }

    [Fact]
    public async Task Create_Throws_WhenSizeIsInactive()
    {
        var s = SeedOrderScenario();
        s.Size.IsActive = false;
        Db.SaveChanges();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId), CancellationToken.None));
    }

    [Fact]
    public async Task Create_Throws_WhenToppingIsUnavailable()
    {
        var s = SeedOrderScenario();
        s.Topping.IsAvailable = false;
        Db.SaveChanges();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, toppingId: s.Topping.ToppingId), CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsOrder_OrNull()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        Assert.NotNull(await Orders.GetAsync(order.OrderId, CancellationToken.None));
        Assert.Null(await Orders.GetAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task Get_IncludesItemDetails_WithFoodAndSizeNames()
    {
        var s = SeedOrderScenario(sizePrice: 50m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 3),
            CancellationToken.None);

        var detail = await Orders.GetAsync(order.OrderId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(order.OrderId, detail!.OrderId);
        Assert.Equal(order.Subtotal, detail.Subtotal);
        Assert.Equal(order.TotalAmount, detail.TotalAmount);

        var item = Assert.Single(detail.Items);
        Assert.Equal(s.Food.FoodName, item.FoodName);
        Assert.Equal(s.Size.SizeName, item.SizeName);
        Assert.Equal(3, item.Quanlity);
        Assert.Equal(50m, item.UnitPrice);
        Assert.Equal("no onion", item.Note);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task Get_IncludesToppings_WithNameAndQuantity()
    {
        var s = SeedOrderScenario(sizePrice: 50m, toppingPrice: 5m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2, toppingId: s.Topping.ToppingId, toppingQuanlity: 3),
            CancellationToken.None);

        var detail = await Orders.GetAsync(order.OrderId, CancellationToken.None);

        Assert.NotNull(detail);
        var item = Assert.Single(detail!.Items);
        var topping = Assert.Single(item.Toppings);
        Assert.Equal(s.Topping.ToppingId, topping.ToppingId);
        Assert.Equal(s.Topping.ToppingName, topping.ToppingName);
        Assert.Equal(3, topping.Quanlity);
        Assert.Equal(5m, topping.UnitPrice);
    }

    [Fact]
    public async Task UpdateStatus_TrimsStatus_AndWritesHistory()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Pending");

        var result = await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest
        {
            Status = "  Preparing  ",
            ChangedBy = s.Staff.Id,
            Note = "started"
        }, CancellationToken.None);

        Assert.True(result);
        var updated = Db.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Preparing", updated.Status);

        var history = Db.OrderStatusHistories.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Pending", history.OldStatus);
        Assert.Equal("Preparing", history.NewStatus);
        Assert.Equal(s.Staff.Id, history.ChangedBy);
        Assert.Equal("started", history.Note);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsFalse_WhenOrderMissing()
    {
        Assert.False(await Orders.UpdateStatusAsync(999999, new UpdateOrderStatusRequest { Status = "Ready" }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateStatus_RejectsStatusOutsideAllowList()
    {
        // Nhóm B2: nay có allow-list (trước đây nhận mọi chuỗi).
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Orders.UpdateStatusAsync(order.OrderId,
            new UpdateOrderStatusRequest { Status = "TotallyInvalid", ChangedBy = s.Staff.Id }, CancellationToken.None));

        Assert.Equal("Invalid order status", ex.Message);
        Assert.Equal("Pending", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateStatus_RejectsBlankStatus()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => Orders.UpdateStatusAsync(order.OrderId,
            new UpdateOrderStatusRequest { Status = "   ", ChangedBy = s.Staff.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateStatus_RejectsChangeFromTerminalState()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateStatusAsync(order.OrderId,
            new UpdateOrderStatusRequest { Status = "Ready", ChangedBy = s.Staff.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Create_Throws_WhenItemsIsNull()
    {
        // Nhóm B8: JSON `null` cho Items lọt qua [MinLength] → phải báo lỗi rõ thay vì NRE.
        var s = SeedOrderScenario();
        var request = new CreateOrderRequest { TableId = s.Table.TableId, CreatedBy = s.Staff.Id, Items = null! };

        await Assert.ThrowsAsync<ArgumentException>(() => Orders.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyPromotion_PercentDiscount_IsCappedByMaxDiscountAmount()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 1000m);
        Db.Promotions.Add(new Promotion
        {
            Name = "10%",
            Description = "d",
            DiscountType = "Percent",
            DiscountValue = 10m,
            MinimumOrderAmount = 100m,
            MaxDiscountAmount = 50m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        Db.SaveChanges();
        var promotionId = Db.Promotions.Single().Id;

        var result = await Orders.ApplyPromotionAsync(order.OrderId, promotionId, CancellationToken.None);

        Assert.True(result);
        var updated = Db.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(50m, updated.DiscountAmount); // 10% of 1000 = 100, capped to 50
        Assert.Equal(950m, updated.TotalAmount);
    }

    [Fact]
    public async Task ApplyPromotion_FixedDiscount_IsApplied()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 500m);
        Db.Promotions.Add(new Promotion
        {
            Name = "fixed",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 30m,
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        Db.SaveChanges();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, Db.Promotions.Single().Id, CancellationToken.None));
        Assert.Equal(30m, Db.Orders.Single(x => x.OrderId == order.OrderId).DiscountAmount);
    }

    [Fact]
    public async Task ApplyPromotion_ReturnsFalse_WhenBelowMinimumOrExpiredOrInactive()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 10m);

        Db.Promotions.Add(new Promotion
        {
            Name = "min-not-met",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 5m,
            MinimumOrderAmount = 100m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        Db.Promotions.Add(new Promotion
        {
            Name = "expired",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 5m,
            StartAt = DateTime.UtcNow.AddDays(-5),
            EndAt = DateTime.UtcNow.AddDays(-1),
            IsActive = true
        });
        Db.Promotions.Add(new Promotion
        {
            Name = "inactive",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 5m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = false
        });
        Db.SaveChanges();

        foreach (var promotion in Db.Promotions.ToList())
        {
            Assert.False(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));
        }

        Assert.Equal(0m, Db.Orders.Single(x => x.OrderId == order.OrderId).DiscountAmount);
    }

    [Fact]
    public async Task ApplyPromotion_ReturnsFalse_WhenOrderOrPromotionMissing()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        Assert.False(await Orders.ApplyPromotionAsync(999999, 1, CancellationToken.None));
        Assert.False(await Orders.ApplyPromotionAsync(order.OrderId, 999999, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyPromotion_PercentDiscount_RoundsToTwoDecimalsBeforeSave()
    {
        // Defect #18: 12.5% của 100.05 = 12.50625; DB decimal(18,2) phải nhận 12.51
        // và TotalAmount phải tính từ chính giá trị đã làm tròn.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 100.05m);
        Db.Promotions.Add(new Promotion
        {
            Name = "12.5%",
            Description = "rounding",
            DiscountType = "Percent",
            DiscountValue = 12.5m,
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        Db.SaveChanges();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, Db.Promotions.Single().Id, CancellationToken.None));

        var updated = Db.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(12.51m, updated.DiscountAmount);
        Assert.Equal(87.54m, updated.TotalAmount);
    }

    [Fact]
    public async Task Create_Throws_WhenToppingIsNotAssignedToFood()
    {
        // Defect #19: topping active/available nhưng không có dòng FoodToppings cho món này.
        var s = SeedOrderScenario();
        var unrelatedTopping = SeedTopping(3m);
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, toppingId: unrelatedTopping.ToppingId),
            CancellationToken.None));

        Assert.Contains("not available for this food", ex.Message);
    }

    [Fact]
    public async Task Create_Throws_WhenTableAlreadyHasAnOpenOrder()
    {
        // Defect #20: không cho mở thêm đơn thứ hai trên cùng bàn khi đơn trước chưa đóng.
        var s = SeedOrderScenario();
        await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId),
            CancellationToken.None));

        Assert.Equal("Table already has an open order", ex.Message);
    }

    [Fact]
    public async Task Create_Throws_WhenTableIsUnderMaintenance()
    {
        // Defect #20: trạng thái Maintenance phải bị chặn kể cả khi IsActive bị đặt sai.
        var s = SeedOrderScenario();
        s.Table.Status = "Maintenance";
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId),
            CancellationToken.None));

        Assert.Equal("Table is under maintenance", ex.Message);
    }

    [Fact]
    public async Task CreateIdempotent_SameKeyAndPayload_ReturnsExistingOrder_AndDeductsStockOnce()
    {
        var s = SeedOrderScenario(sizePrice: 10m, stock: 100m);
        var request = Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2);

        var first = await Orders.CreateIdempotentAsync(request, "retry-key", CancellationToken.None);
        var replay = Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2);
        var second = await Orders.CreateIdempotentAsync(replay, "retry-key", CancellationToken.None);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(first.TotalAmount, second.TotalAmount);

        using var fresh = NewContext();
        Assert.Single(fresh.Orders);
        // Recipe dùng 2 đơn vị/nguyên liệu cho mỗi món × 2 món = 4, chỉ trừ một lần.
        Assert.Equal(96m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task CreateIdempotent_SameKeyDifferentPayload_ThrowsConflict()
    {
        var s = SeedOrderScenario(sizePrice: 10m, stock: 100m);
        var first = Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2);
        await Orders.CreateIdempotentAsync(first, "retry-key", CancellationToken.None);

        var different = Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 3);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.CreateIdempotentAsync(different, "retry-key", CancellationToken.None));

        Assert.Contains("different order payload", ex.Message);
        using var fresh = NewContext();
        Assert.Single(fresh.Orders);
        Assert.Equal(96m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task OrderIdempotencyStore_ConcurrentSameKey_CreatesOnce()
    {
        var store = new InMemoryOrderIdempotencyStore();
        var calls = 0;
        var created = new OrderCreatedResponse { OrderId = 123, Status = "SentToKitchen", TotalAmount = 10m };

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => store.ExecuteAsync(
                "actor:key",
                "fingerprint",
                async _ =>
                {
                    Interlocked.Increment(ref calls);
                    await Task.Delay(25);
                    return created;
                },
                CancellationToken.None))
            .ToList();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(results, x => Assert.Equal(123, x.OrderId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(
            "actor:key",
            "other-fingerprint",
            _ => Task.FromResult(created),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateItems_ReplacesItems_AndAdjustsStockAndKitchenTicket()
    {
        var s = SeedOrderScenario(sizePrice: 50m, toppingPrice: 5m, stock: 100m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2, toppingId: s.Topping.ToppingId),
            CancellationToken.None);

        var updated = await Orders.UpdateItemsAsync(
            order.OrderId,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 3, toppingId: s.Topping.ToppingId),
            CancellationToken.None);

        Assert.True(updated);
        var detail = await Orders.GetAsync(order.OrderId, CancellationToken.None);
        Assert.NotNull(detail);
        var item = Assert.Single(detail!.Items);
        Assert.Equal(3, item.Quanlity);
        Assert.Equal(55m, item.UnitPrice); // 50 size + 5 topping
        Assert.Equal("updated", item.Note);
        Assert.Single(item.Toppings);

        Assert.Equal(165m, detail.Subtotal);
        Assert.Equal(0m, detail.DiscountAmount);
        Assert.Equal(165m, detail.TotalAmount);

        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        var kitchenItems = Db.KitchenOrderItems.Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId).ToList();
        var kitchenItem = Assert.Single(kitchenItems);
        Assert.Equal(3, kitchenItem.Quanlity);
        Assert.Equal(item.Id, kitchenItem.OrderItemId);

        using var fresh = NewContext();
        Assert.Equal(94m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
        var transactions = await fresh.InventoryTransactions.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.Equal(4m, transactions[0].Quanlity);
        Assert.Equal(2m, transactions[1].Quanlity);
        Assert.All(transactions, x => Assert.Equal("Sale", x.Type));
    }

    [Fact]
    public async Task UpdateItems_DecreaseQuantity_ReturnsStockAndWritesAdjustmentIn()
    {
        var s = SeedOrderScenario(sizePrice: 10m, stock: 100m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 3),
            CancellationToken.None);

        Assert.True(await Orders.UpdateItemsAsync(
            order.OrderId,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 1),
            CancellationToken.None));

        using var fresh = NewContext();
        Assert.Equal(98m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
        var transactions = await fresh.InventoryTransactions.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.Equal("Sale", transactions[0].Type);
        Assert.Equal(6m, transactions[0].Quanlity);
        Assert.Equal("AdjustmentIn", transactions[1].Type);
        Assert.Equal(4m, transactions[1].Quanlity);
        Assert.Equal(94m, transactions[1].QuanlityBefore);
        Assert.Equal(98m, transactions[1].QuanlityAfter);
    }

    [Fact]
    public async Task UpdateItems_RejectsWhenKitchenHasStarted()
    {
        var s = SeedOrderScenario(sizePrice: 10m, stock: 100m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2),
            CancellationToken.None);

        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        var kitchenItem = Db.KitchenOrderItems.Single(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId);
        kitchenItem.Status = "Preparing";
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateItemsAsync(
            order.OrderId,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 3),
            CancellationToken.None));

        Assert.Equal("Kitchen has already started this order", ex.Message);
        using var fresh = NewContext();
        Assert.Equal(20m, fresh.Orders.Single(x => x.OrderId == order.OrderId).Subtotal);
        Assert.Equal(96m, fresh.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
        Assert.Single(fresh.OrderItems.Where(x => x.OrderId == order.OrderId));
    }

    [Fact]
    public async Task UpdateItems_ResetsAppliedDiscount()
    {
        var s = SeedOrderScenario(sizePrice: 10m, stock: 100m);
        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2),
            CancellationToken.None);

        Db.Promotions.Add(new Promotion
        {
            Name = "edit-reset",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 5m,
            MinimumOrderAmount = 0m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        Db.SaveChanges();
        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, Db.Promotions.Single().Id, CancellationToken.None));
        Assert.Equal(5m, Db.Orders.Single(x => x.OrderId == order.OrderId).DiscountAmount);

        Assert.True(await Orders.UpdateItemsAsync(
            order.OrderId,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 4),
            CancellationToken.None));

        var updated = await Orders.GetAsync(order.OrderId, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(40m, updated!.Subtotal);
        Assert.Equal(0m, updated.DiscountAmount);
        Assert.Equal(40m, updated.TotalAmount);
    }

    [Fact]
    public async Task UpdateItems_RejectsTerminalOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateItemsAsync(
            order.OrderId,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 1),
            CancellationToken.None));

        Assert.Contains("can no longer be edited", ex.Message);
    }

    [Fact]
    public async Task UpdateItems_ReturnsFalse_WhenOrderMissing()
    {
        var s = SeedOrderScenario();

        Assert.False(await Orders.UpdateItemsAsync(
            999999,
            UpdateRequest(s.Staff.Id, s.Size.SizeId, quantity: 1),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_StartsTransaction_BeforeReadingInventory()
    {
        // Defect #3: kiểm tra tồn kho phải nằm trong transaction, không được đọc trước
        // rồi mới mở transaction (khoảng trống cho hai đơn cùng vượt check).
        var s = SeedOrderScenario(stock: 1m);
        var state = new InterceptorState();
        using var db = NewContext(
            new TransactionWatchInterceptor(state),
            new CommandWatchInterceptor(state));
        var service = new OrderService(
            db,
            new RestaurantTableService(db),
            CurrentUser,
            NullLogger<OrderService>.Instance,
            OrderIdempotency);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId),
            CancellationToken.None));

        Assert.True(state.TransactionStartedAt >= 0);
        Assert.True(
            state.InventoryReadAt > state.TransactionStartedAt,
            string.Join("\n---\n", state.Commands));
    }

    private sealed class InterceptorState
    {
        public int Sequence;
        public int TransactionStartedAt = -1;
        public int InventoryReadAt = -1;
        public ConcurrentQueue<string> Commands { get; } = new();

        public void MarkTransaction()
        {
            if (TransactionStartedAt < 0)
            {
                TransactionStartedAt = Interlocked.Increment(ref Sequence);
            }
        }

        public void MarkCommand(DbCommand command)
        {
            Commands.Enqueue(command.CommandText);
            if (command.CommandText.Contains("FROM \"inventory\"", StringComparison.OrdinalIgnoreCase))
            {
                InventoryReadAt = Interlocked.Increment(ref Sequence);
            }
        }
    }

    private sealed class TransactionWatchInterceptor : DbTransactionInterceptor
    {
        private readonly InterceptorState _state;

        public TransactionWatchInterceptor(InterceptorState state) => _state = state;

        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction transaction)
        {
            _state.MarkTransaction();
            return base.TransactionStarted(connection, eventData, transaction);
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            _state.MarkTransaction();
            return base.TransactionStartedAsync(connection, eventData, transaction, cancellationToken);
        }
    }

    private sealed class CommandWatchInterceptor : DbCommandInterceptor
    {
        private readonly InterceptorState _state;

        public CommandWatchInterceptor(InterceptorState state) => _state = state;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _state.MarkCommand(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _state.MarkCommand(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
