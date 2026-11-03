using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Phase 2 — gọi thêm, điều chỉnh số lượng, prepared quantity và thiếu nguyên liệu
/// theo Cách A (tách dòng + OrderItemGroupId).
/// </summary>
public sealed class Phase2LifecycleTests : TestBase
{
    private sealed record CreatedOrder(
        OrderScenario Scenario,
        Order Order,
        OrderItem Item,
        KitchenOrder Ticket,
        KitchenOrderItem KitchenItem);

    private async Task<CreatedOrder> CreateOrderAsync(int quantity = 2, decimal stock = 100m)
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: stock);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = scenario.Size.SizeId,
                    Quanlity = quantity
                }
            ]
        }, CancellationToken.None);

        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        var ticket = await Db.KitchenOrders.SingleAsync(x => x.OrderId == order.OrderId);
        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.OrderItemId == item.Id);

        return new CreatedOrder(scenario, order, item, ticket, kitchenItem);
    }

    private static AddOrderItemsRequest AddOne(CreatedOrder created, int quantity = 1) =>
        new()
        {
            ChangedBy = created.Scenario.Staff.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = created.Scenario.Size.SizeId,
                    Quanlity = quantity
                }
            ]
        };

    private async Task CompleteOrderAsync(CreatedOrder created)
    {
        await Kitchen.UpdateItemStatusAsync(
            created.KitchenItem.Id,
            new KitchenItemStatusRequest { Status = KitchenStatuses.Completed },
            CancellationToken.None);

        await Kitchen.UpdateOrderStatusAsync(
            created.Ticket.KitchenOrderId,
            new KitchenStatusRequest { Status = KitchenStatuses.Completed },
            CancellationToken.None);

        await Orders.UpdateStatusAsync(
            created.Order.OrderId,
            new UpdateOrderStatusRequest
            {
                Status = OrderStatuses.Completed,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task AddItems_AfterOrderCompleted_CreatesNewRoundAndReopensOrder()
    {
        var created = await CreateOrderAsync(quantity: 1);
        await CompleteOrderAsync(created);

        Assert.True(await Orders.AddItemsAsync(
            created.Order.OrderId,
            AddOne(created),
            CancellationToken.None));

        using var check = NewContext();
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        var tickets = check.KitchenOrders
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.KitchenOrderId)
            .ToList();
        var items = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Equal(OrderStatuses.SentToKitchen, order.Status);
        Assert.Equal(2, tickets.Count);
        Assert.Equal(KitchenStatuses.Completed, tickets[0].Status);
        Assert.Equal(KitchenStatuses.Pending, tickets[1].Status);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.NotNull(item.OrderItemGroupId));
        Assert.NotEqual(items[0].OrderItemGroupId, items[1].OrderItemGroupId);
        Assert.Equal(100m, order.Subtotal);
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal(RestaurantTableStatuses.Occupied, check.RestaurantTables
            .Single(x => x.TableId == created.Scenario.Table.TableId).Status);

        Assert.Contains(check.OrderStatusHistories.Where(x => x.OrderId == order.OrderId),
            x => x.Note != null && x.Note.Contains("new kitchen round", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddItems_OnPaidOrder_IsRejected()
    {
        var created = await CreateOrderAsync(quantity: 1);

        created.Order.Status = OrderStatuses.Paid;
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.AddItemsAsync(created.Order.OrderId, AddOne(created), CancellationToken.None));
    }

    [Fact]
    public async Task AddItems_WhenTableWasReleased_IsRejected()
    {
        var created = await CreateOrderAsync(quantity: 1);

        var table = await Db.RestaurantTables.SingleAsync(x => x.TableId == created.Scenario.Table.TableId);
        table.Status = RestaurantTableStatuses.Available;
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.AddItemsAsync(created.Order.OrderId, AddOne(created), CancellationToken.None));

        Assert.Contains("no longer occupied", ex.Message);
    }

    [Fact]
    public async Task ReduceQuantity_BeforeKitchen_ReturnsStockAndUpdatesKitchenItem()
    {
        var created = await CreateOrderAsync(quantity: 3);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 2,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        var item = check.OrderItems.Single(x => x.Id == created.Item.Id);
        var kitchenItem = check.KitchenOrderItems.Single(x => x.Id == created.KitchenItem.Id);
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);

        Assert.Equal(2, item.Quanlity);
        Assert.Equal(2, kitchenItem.Quanlity);
        Assert.Equal(100m, order.Subtotal);
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        var returned = Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == created.Item.Id && x.Type == "AdjustmentIn"));
        Assert.Equal(2m, returned.Quanlity);
    }

    [Fact]
    public async Task IncreaseQuantity_BeforeKitchen_DeductsStockWithoutNewRound()
    {
        var created = await CreateOrderAsync(quantity: 2);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 3,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        var item = check.OrderItems.Single(x => x.Id == created.Item.Id);
        var kitchenItem = check.KitchenOrderItems.Single(x => x.Id == created.KitchenItem.Id);
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);

        Assert.Equal(3, item.Quanlity);
        Assert.Equal(3, kitchenItem.Quanlity);
        Assert.Equal(150m, order.Subtotal);
        Assert.Equal(94m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Single(check.KitchenOrders.Where(x => x.OrderId == created.Order.OrderId));
    }

    /// <summary>
    /// C-20: món đã vào nấu (Preparing) mà POS tăng số lượng ⇒ TÁCH thành dòng `Pending`
    /// MỚI nằm trong cùng phiếu, KHÔNG cộng vào dòng đang nấu (giống cách bếp thật làm).
    /// </summary>
    [Fact]
    public async Task IncreaseQuantity_WhilePreparing_SplitsIntoNewPendingLineInSameTicket()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 5,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        var orderItems = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();
        var tickets = check.KitchenOrders
            .Where(x => x.OrderId == created.Order.OrderId)
            .ToList();
        var kitchenItems = check.KitchenOrderItems
            .Where(x => orderItems.Select(i => i.Id).Contains(x.OrderItemId))
            .OrderBy(x => x.Id)
            .ToList();

        // Dòng gốc giữ nguyên 2 phần, vẫn đang nấu.
        Assert.Equal(2, orderItems.Count);
        Assert.Equal(2, orderItems[0].Quanlity);
        Assert.Equal(KitchenStatuses.Preparing, orderItems[0].Status);

        // 3 phần mới nằm ở dòng RIÊNG, trạng thái Pending, cùng nhóm với dòng gốc.
        Assert.Equal(3, orderItems[1].Quanlity);
        Assert.Equal(KitchenStatuses.Pending, orderItems[1].Status);
        Assert.Equal(orderItems[0].OrderItemGroupId, orderItems[1].OrderItemGroupId);

        // VẪN CHỈ 1 phiếu bếp — dòng mới nằm trong chính phiếu đang nấu.
        Assert.Single(tickets);
        Assert.Equal(KitchenStatuses.Preparing, tickets[0].Status);
        Assert.Equal(2, kitchenItems.Count);
        Assert.Equal(2, kitchenItems[0].Quanlity);
        Assert.Equal(KitchenStatuses.Preparing, kitchenItems[0].Status);
        Assert.Equal(3, kitchenItems[1].Quanlity);
        Assert.Equal(KitchenStatuses.Pending, kitchenItems[1].Status);
        Assert.Equal(tickets[0].KitchenOrderId, kitchenItems[0].KitchenOrderId);
        Assert.Equal(tickets[0].KitchenOrderId, kitchenItems[1].KitchenOrderId);

        // Tiền và kho vẫn đúng (5 phần × 50đ; công thức 2 đơn vị/phan ⇒ 100 − 5×2 = 90).
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        Assert.Equal(250m, order.Subtotal);
        Assert.Equal(250m, order.TotalAmount);
        Assert.Equal(90m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
    }

    /// <summary>
    /// C-20: món còn `Pending` (bếp chưa bắt đầu) ⇒ GỘP chung vào dòng Pending, không tách dòng.
    /// </summary>
    [Fact]
    public async Task IncreaseQuantity_WhilePending_MergesIntoExistingPendingLine()
    {
        var created = await CreateOrderAsync(quantity: 2);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 3,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        var orderItems = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .ToList();
        var kitchenItems = check.KitchenOrderItems
            .Where(x => x.OrderItemId == orderItems[0].Id)
            .ToList();

        Assert.Single(orderItems);
        Assert.Single(kitchenItems);
        Assert.Equal(3, orderItems[0].Quanlity);
        Assert.Equal(3, kitchenItems[0].Quanlity);
        Assert.Single(check.KitchenOrders.Where(x => x.OrderId == created.Order.OrderId));
    }

    /// <summary>
    /// C-20: dòng `Pending` mới phải hiện NGAY DƯỚI dòng đang nấu trên màn bếp.
    /// </summary>
    [Fact]
    public async Task IncreaseQuantity_WhilePreparing_NewPendingLineListedLastInKitchenTicket()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 5,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        var items = await Kitchen.GetItemsAsync(created.Ticket.KitchenOrderId, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(KitchenStatuses.Preparing, items[0].Status);
        Assert.Equal(2, items[0].Quanlity);
        Assert.Equal(KitchenStatuses.Pending, items[1].Status);
        Assert.Equal(3, items[1].Quanlity);
    }

    [Fact]
    public async Task AdjustQuantity_OnLegacyLineWithoutGroup_StillWorks()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.OrderItemGroupId = null;
        await Db.SaveChangesAsync();

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var item = check.OrderItems.Single(x => x.Id == created.Item.Id);
        Assert.Equal(1, item.Quanlity);
        Assert.NotNull(item.OrderItemGroupId);
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task IncreaseQuantity_BeyondStock_IsRejectedAndRollsBack()
    {
        var created = await CreateOrderAsync(quantity: 2, stock: 5m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.AdjustItemQuantityAsync(
                created.Order.OrderId,
                created.Item.Id,
                new AdjustOrderItemQuantityRequest
                {
                    Quantity = 3,
                    ChangedBy = created.Scenario.Staff.Id
                },
                CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(1m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal(2, check.OrderItems.Single(x => x.Id == created.Item.Id).Quanlity);
    }

    [Fact]
    public async Task SequentialReductions_DoNotOverReturnStockAfterWastage()
    {
        var created = await CreateOrderAsync(quantity: 3);

        // Giảm 1 phần do hết nguyên liệu: không hoàn kho, ghi wastage.
        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 2,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "OutOfStock",
                Reason = "Thiếu một phần"
            },
            CancellationToken.None));

        // Giảm tiếp 1 phần do khách đổi: chỉ hoàn 2kg của phần này, không hoàn
        // thêm phần đã wastage trước đó.
        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using (var afterSecond = NewContext())
        {
            Assert.Equal(96m, afterSecond.Inventories
                .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
            Assert.Equal(1, afterSecond.OrderItems.Single(x => x.Id == created.Item.Id).Quanlity);
        }

        // Huỷ nốt phần còn lại: hoàn đúng 2kg.
        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 0,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange",
                Reason = "Khách huỷ nốt"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal(OrderStatuses.Cancelled, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Status);
    }

    /// <summary>
    /// C-13: món đã bấm "Preparing" thì nguyên liệu đã tiêu thật ⇒ KHÔNG hoàn kho.
    /// Giảm số lượng chỉ ghi hao hụt (wastage), tồn kho giữ nguyên.
    /// </summary>
    [Fact]
    public async Task ReduceQuantity_WhilePreparingButNotCooked_DoesNotReturnStock_RecordsWastage()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(1, check.OrderItems.Single(x => x.Id == created.Item.Id).Quanlity);
        Assert.Equal(1, check.KitchenOrderItems.Single(x => x.Id == created.KitchenItem.Id).Quanlity);

        // Tồn kho KHÔNG được hoàn lại: đã gọi 2 phần thì vẫn còn 96 (100 - 2*2).
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        // Phần bị giảm được ghi hao hụt để báo cáo không mất dấu vết.
        var wastage = check.InventoryTransactions
            .Single(x => x.Type == "Wastage" && x.OrderItemId == created.Item.Id);
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Equal(96m, wastage.QuanlityBefore);
        Assert.Equal(96m, wastage.QuanlityAfter);

        Assert.Equal(OrderStatuses.Preparing, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Status);
        Assert.Equal(KitchenStatuses.Preparing, check.KitchenOrders
            .Single(x => x.KitchenOrderId == created.Ticket.KitchenOrderId).Status);
    }

    /// <summary>
    /// C-13: món đang nấu bị khóa với POS — chỉ bếp (Chef) hoặc Admin mới đổi được số lượng.
    /// </summary>
    [Fact]
    public async Task ReduceQuantity_WhilePreparing_ByWaiter_IsForbidden()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();

        CurrentUser.RoleList.Add(AuthPolicies.Roles.Waiter);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Orders.AdjustItemQuantityAsync(
                created.Order.OrderId,
                created.Item.Id,
                new AdjustOrderItemQuantityRequest
                {
                    Quantity = 1,
                    ChangedBy = created.Scenario.Staff.Id,
                    ReasonCode = "CustomerChange"
                },
                CancellationToken.None));
    }

    /// <summary>C-13: bếp vẫn dùng được món đang nấu (khóa chỉ áp cho POS).</summary>
    [Fact]
    public async Task ReduceQuantity_WhilePreparing_ByChef_IsAllowed()
    {
        var created = await CreateOrderAsync(quantity: 2);

        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();

        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(1, check.OrderItems.Single(x => x.Id == created.Item.Id).Quanlity);
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task PreparedQuantity_SplitsLine_AndReducingPendingReturnsOnlyPendingStock()
    {
        var created = await CreateOrderAsync(quantity: 2);

        Assert.True(await Kitchen.UpdatePreparedQuantityAsync(
            created.KitchenItem.Id,
            new PreparedQuantityRequest
            {
                PreparedQuantity = 1,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using (var afterSplit = NewContext())
        {
            var items = afterSplit.OrderItems
                .Where(x => x.OrderId == created.Order.OrderId)
                .OrderBy(x => x.Id)
                .ToList();
            Assert.Equal(2, items.Count);
            var completed = items.Single(x => x.Status == KitchenStatuses.Completed);
            var pending = items.Single(x => x.Status == KitchenStatuses.Pending);
            Assert.Equal(1, completed.Quanlity);
            Assert.Equal(1, pending.Quanlity);
            Assert.NotNull(completed.OrderItemGroupId);
            Assert.Equal(completed.OrderItemGroupId, pending.OrderItemGroupId);
            Assert.Equal(96m, afterSplit.Inventories
                .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        }

        var pendingItem = await Db.OrderItems.SingleAsync(x =>
            x.OrderId == created.Order.OrderId && x.Status == KitchenStatuses.Pending);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            pendingItem.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id,
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        using var check = NewContext();
        var storedItems = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();
        var storedCompleted = storedItems.Single(x => x.Status == KitchenStatuses.Completed);
        var storedCancelled = storedItems.Single(x => x.Status == KitchenStatuses.Cancelled);
        Assert.Equal(1, storedCompleted.Quanlity);
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal(50m, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Subtotal);
        Assert.Equal(OrderStatuses.Ready, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Status);

        var returned = Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == storedCancelled.Id && x.Type == "AdjustmentIn"));
        Assert.Equal(2m, returned.Quanlity);
    }

    [Fact]
    public async Task ReduceBelowPrepared_RequiresAdminWastageOverride()
    {
        var created = await CreateOrderAsync(quantity: 2);

        await Kitchen.UpdatePreparedQuantityAsync(
            created.KitchenItem.Id,
            new PreparedQuantityRequest
            {
                PreparedQuantity = 2,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None);

        CurrentUser.UserId = created.Scenario.Staff.Id;
        CurrentUser.RoleList.Add("Chef");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Orders.AdjustItemQuantityAsync(
                created.Order.OrderId,
                created.Item.Id,
                new AdjustOrderItemQuantityRequest
                {
                    Quantity = 1,
                    ChangedBy = created.Scenario.Staff.Id,
                    AllowWastage = true,
                    Reason = "Khách chỉ ăn một phần"
                },
                CancellationToken.None));

        CurrentUser.RoleList.Clear();
        CurrentUser.RoleList.Add("Admin");

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 1,
                ChangedBy = created.Scenario.Staff.Id,
                AllowWastage = true,
                Reason = "Khách chỉ ăn một phần"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(1, check.OrderItems.Single(x => x.Id == created.Item.Id).Quanlity);
        Assert.Equal(50m, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Subtotal);
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        var wastage = Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == created.Item.Id && x.Type == "Wastage"));
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Equal(wastage.QuanlityBefore, wastage.QuanlityAfter);
    }

    [Fact]
    public async Task Shortage_SplitsLine_CancelsRemainingAndRecordsWastageWithoutStockReturn()
    {
        var created = await CreateOrderAsync(quantity: 2);

        Assert.True(await Kitchen.ReportShortageAsync(
            created.KitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 1,
                ReasonCode = "PhysicalShortage",
                Reason = "Thực tế chỉ đủ một phần",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var items = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();
        var kitchenItems = check.KitchenOrderItems
            .Where(x => x.KitchenOrderId == created.Ticket.KitchenOrderId)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(KitchenStatuses.Completed, items[0].Status);
        Assert.Equal(1, items[0].Quanlity);
        Assert.Equal(KitchenStatuses.Cancelled, items[1].Status);
        Assert.Equal(1, items[1].Quanlity);
        Assert.Equal(items[0].OrderItemGroupId, items[1].OrderItemGroupId);
        Assert.Equal(KitchenStatuses.Completed, kitchenItems[0].Status);
        Assert.Equal(KitchenStatuses.Cancelled, kitchenItems[1].Status);
        Assert.Equal(OrderStatuses.Ready, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Status);
        Assert.Equal(50m, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Subtotal);

        // Không hoàn kho ảo: tồn vẫn là 96 sau khi tạo đơn 2 phần.
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == created.Order.OrderId && x.Type == "AdjustmentIn");

        var wastage = Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == items[1].Id && x.Type == "Wastage"));
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Equal(wastage.QuanlityBefore, wastage.QuanlityAfter);
    }

    [Fact]
    public async Task Shortage_AfterPreparedSplit_DoesNotReturnStockAndKeepsPreparedPortion()
    {
        var created = await CreateOrderAsync(quantity: 3);

        await Kitchen.UpdatePreparedQuantityAsync(
            created.KitchenItem.Id,
            new PreparedQuantityRequest
            {
                PreparedQuantity = 1,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None);

        var remainingKitchenItem = await Db.KitchenOrderItems.SingleAsync(x =>
            x.KitchenOrderId == created.Ticket.KitchenOrderId
            && x.Status == KitchenStatuses.Pending);

        Assert.True(await Kitchen.ReportShortageAsync(
            remainingKitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 1,
                ReasonCode = "OutOfStock",
                Reason = "Chỉ còn đủ một phần",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        var items = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .ToList();

        // 1 phần đã nấu trước đó + 1 phần làm được sau khi báo thiếu = 2 phần tính tiền.
        Assert.Equal(100m, order.Subtotal);
        Assert.Equal(OrderStatuses.Ready, order.Status);
        Assert.Equal(94m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == created.Order.OrderId && x.Type == "AdjustmentIn");
        Assert.Equal(2, items.Count(x => x.Status == KitchenStatuses.Completed));
        Assert.Equal(1, items.Count(x => x.Status == KitchenStatuses.Cancelled));
        Assert.Single(check.InventoryTransactions.Where(x =>
            x.ReferenceId == created.Order.OrderId
            && x.Type == "Wastage"
            && x.Quanlity == 2m));
    }

    [Fact]
    public async Task Shortage_WhenNoPortionCanBeMade_CancelsItemAndOrderWithoutStockReturn()
    {
        var created = await CreateOrderAsync(quantity: 1);

        Assert.True(await Kitchen.ReportShortageAsync(
            created.KitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 0,
                ReasonCode = "OutOfStock",
                Reason = "Hết nguyên liệu",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var storedOrder = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        Assert.Equal(OrderStatuses.Cancelled, storedOrder.Status);
        Assert.Equal(50m, storedOrder.Subtotal);
        Assert.Equal(KitchenStatuses.Cancelled, check.OrderItems
            .Single(x => x.Id == created.Item.Id).Status);
        Assert.Equal(RestaurantTableStatuses.Available, check.RestaurantTables
            .Single(x => x.TableId == created.Scenario.Table.TableId).Status);
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == created.Order.OrderId && x.Type == "AdjustmentIn");
        Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == created.Item.Id && x.Type == "Wastage" && x.Quanlity == 2m));
    }

    [Fact]
    public async Task IncreaseAfterTicketCompleted_CreatesNewRoundInsteadOfReopeningOldLine()
    {
        var created = await CreateOrderAsync(quantity: 1);
        await CompleteOrderAsync(created);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 2,
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var tickets = check.KitchenOrders
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.KitchenOrderId)
            .ToList();
        var items = check.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Equal(2, tickets.Count);
        Assert.Equal(KitchenStatuses.Completed, tickets[0].Status);
        Assert.Equal(KitchenStatuses.Pending, tickets[1].Status);
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].Quanlity);
        Assert.Equal(KitchenStatuses.Completed, items[0].Status);
        Assert.Equal(1, items[1].Quanlity);
        Assert.Equal(KitchenStatuses.Pending, items[1].Status);
        Assert.Equal(items[0].OrderItemGroupId, items[1].OrderItemGroupId);
        Assert.Equal(OrderStatuses.SentToKitchen, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Status);
        Assert.Equal(100m, check.Orders
            .Single(x => x.OrderId == created.Order.OrderId).Subtotal);
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task CancellingNewRound_DoesNotCancelOrderWhenOldRoundIsDone()
    {
        var created = await CreateOrderAsync(quantity: 1);
        await CompleteOrderAsync(created);
        await Orders.AddItemsAsync(created.Order.OrderId, AddOne(created), CancellationToken.None);

        var addedItem = await Db.OrderItems
            .Where(x => x.OrderId == created.Order.OrderId && x.Id != created.Item.Id)
            .SingleAsync();

        Assert.True(await Orders.CancelItemAsync(
            created.Order.OrderId,
            addedItem.Id,
            new CancelOrderItemRequest
            {
                Reason = "Khách đổi ý",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        var tickets = check.KitchenOrders
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.KitchenOrderId)
            .ToList();

        Assert.Equal(OrderStatuses.Ready, order.Status);
        Assert.Equal(50m, order.Subtotal);
        Assert.Equal(KitchenStatuses.Completed, tickets[0].Status);
        Assert.Equal(KitchenStatuses.Cancelled, tickets[1].Status);
    }

    [Fact]
    public async Task ReduceToZero_CancelsItemAndRequiresReason()
    {
        var created = await CreateOrderAsync(quantity: 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Orders.AdjustItemQuantityAsync(
                created.Order.OrderId,
                created.Item.Id,
                new AdjustOrderItemQuantityRequest
                {
                    Quantity = 0,
                    ChangedBy = created.Scenario.Staff.Id
                },
                CancellationToken.None));

        Assert.True(await Orders.AdjustItemQuantityAsync(
            created.Order.OrderId,
            created.Item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 0,
                ChangedBy = created.Scenario.Staff.Id,
                Reason = "Khách huỷ món"
            },
            CancellationToken.None));

        using var check = NewContext();
        var order = check.Orders.Single(x => x.OrderId == created.Order.OrderId);
        Assert.Equal(OrderStatuses.Cancelled, order.Status);
        Assert.Equal(50m, order.Subtotal);
        Assert.Equal(KitchenStatuses.Cancelled, check.OrderItems
            .Single(x => x.Id == created.Item.Id).Status);
        Assert.Equal(100m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal(RestaurantTableStatuses.Available, check.RestaurantTables
            .Single(x => x.TableId == created.Scenario.Table.TableId).Status);
    }
}
