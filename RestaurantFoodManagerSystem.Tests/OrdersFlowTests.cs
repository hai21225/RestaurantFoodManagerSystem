using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Pins the most critical use case: creating an order (recipe check, price snapshot,
/// kitchen ticket, stock deduction, transaction) plus status/promotion updates.
/// </summary>
public sealed class OrdersFlowTests : TestBase
{
    private static CreateOrderRequest Request(int tableId, int createdBy, int sizeId, int quantity = 2, int? toppingId = null, int toppingQuantity = 1)
    {
        var item = new CreateOrderItemRequest
        {
            SizeId = sizeId,
            Quantity = quantity,
            Note = "no onion"
        };
        if (toppingId.HasValue)
        {
            item.Toppings.Add(new CreateOrderToppingRequest { ToppingId = toppingId.Value, Quantity = toppingQuantity });
        }
        var request = new CreateOrderRequest { TableId = tableId, CreatedBy = createdBy };
        request.Items.Add(item);
        return request;
    }

    [Fact]
    public async Task Create_SnapshotsPrice_CreatesKitchenTicket_AndDeductsStock()
    {
        var s = SeedOrderScenario(sizePrice: 50m, toppingPrice: 5m, stock: 100m);

        var order = await Orders.CreateAsync(
            Request(s.Table.TableId, s.Staff.Id, s.Size.SizeId, quantity: 2, toppingId: s.Topping.ToppingId, toppingQuantity: 1),
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
        request.Items.Add(new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quantity = 2 });
        request.Items.Add(new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quantity = 3 });

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
    public async Task UpdateStatus_AcceptsAnyStatusString_CurrentlyUnvalidated()
    {
        // Baseline: no allow-list is enforced for order status transitions.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var result = await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest
        {
            Status = "TotallyInvalid",
            ChangedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal("TotallyInvalid", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
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
}
