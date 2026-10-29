using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P1a–P1e: huỷ theo từng món, hoàn kho đúng phần chưa nấu, lý do huỷ, phiếu bếp không
/// còn treo khi mọi món bị huỷ, ma trận trạng thái và điều kiện thanh toán.
/// </summary>
public sealed class OrderCancellationTests : TestBase
{
    private async Task<(OrderScenario Scenario, Order Order, List<OrderItem> Items)> CreateOrderAsync(
        int itemCount = 2,
        int quantityPerItem = 1)
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var request = new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id
        };

        for (var i = 0; i < itemCount; i++)
        {
            request.Items.Add(new CreateOrderItemRequest
            {
                SizeId = scenario.Size.SizeId,
                Quanlity = quantityPerItem
            });
        }

        var order = await Orders.CreateAsync(request, CancellationToken.None);
        var items = await Db.OrderItems
            .Where(x => x.OrderId == order.OrderId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        return (scenario, order, items);
    }

    private void SetItemPreparing(OrderItem item)
    {
        item.Status = "Preparing";
        var kitchenItem = Db.KitchenOrderItems.Single(x => x.OrderItemId == item.Id);
        kitchenItem.Status = "Preparing";
        var kitchenOrder = Db.KitchenOrders.Single(x => x.KitchenOrderId == kitchenItem.KitchenOrderId);
        kitchenOrder.Status = "Preparing";
        Db.Orders.Single(x => x.OrderId == item.OrderId).Status = "Preparing";
        Db.SaveChanges();
    }

    // ============================ P1d: MA TRẬN TRẠNG THÁI ============================

    [Fact]
    public async Task UpdateStatus_RejectsBackwardTransition_ReadyToPending()
    {
        var scenario = SeedOrderScenario();
        var order = SeedOrder(scenario.Table.TableId, scenario.Staff.Id, status: "Ready");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Pending", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Contains("Cannot change order status", ex.Message);
        Assert.Equal("Ready", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateStatus_RejectsSkipTransition_PendingToCompleted()
    {
        var scenario = SeedOrderScenario();
        var order = SeedOrder(scenario.Table.TableId, scenario.Staff.Id, status: "Pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Completed", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Pending", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateStatus_Cancel_RequiresReason()
    {
        var scenario = SeedOrderScenario();
        var order = SeedOrder(scenario.Table.TableId, scenario.Staff.Id, status: "SentToKitchen");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Cancelled", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Cancel reason is required", ex.Message);
    }

    [Fact]
    public async Task UpdateStatus_RejectsReady_WhenKitchenItemsNotDone()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Ready", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Contains("while dishes are not finished", ex.Message);
        Assert.Equal("SentToKitchen", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateStatus_RejectsCompleted_WhenKitchenItemsNotDone()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        items[0].Status = "Preparing";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Preparing";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Preparing";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Preparing";
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Completed", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Contains("while dishes are not finished", ex.Message);
        Assert.Equal("Preparing", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateStatus_AllowsReady_WhenAllKitchenItemsDone()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        items[0].Status = "Ready";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.SaveChanges();

        Assert.True(await Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = "Ready", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Ready", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    // ============================ P1a/P1b: HUỶ MỘT MÓN ============================

    [Fact]
    public async Task CancelItem_ReturnsOnlyThatItemsStock_AndReducesBill()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 2);
        Assert.Equal(96m, Stock(scenario));

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(98m, check.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);

        var cancelled = check.OrderItems.Single(x => x.Id == items[0].Id);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Khách đổi món", cancelled.CancelReason);
        Assert.Equal(scenario.Staff.Id, cancelled.CancelledBy);
        Assert.NotNull(cancelled.CancelledAt);

        Assert.Equal("Pending", check.OrderItems.Single(x => x.Id == items[1].Id).Status);

        var stored = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("SentToKitchen", stored.Status);
        Assert.Equal(50m, stored.Subtotal);
        Assert.Equal(50m, stored.TotalAmount);
        Assert.Equal("Pending", check.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status);

        // Đúng một dòng hoàn kho, gắn với món bị huỷ.
        var returnRow = Assert.Single(check.InventoryTransactions.Where(x =>
            x.Type == "AdjustmentIn" && x.ReferenceId == order.OrderId));
        Assert.Equal(2m, returnRow.Quanlity);
        Assert.Equal(items[0].Id, returnRow.OrderItemId);
    }

    [Fact]
    public async Task CancelItem_FromPos_SyncsTicketAndOrder_WhenRemainingItemIsReady()
    {
        // C-01: huỷ một món từ POS phải dùng effective statuses, không đọc lại DB cũ.
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 2);
        items[0].Status = "Ready";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Preparing";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Preparing";
        Db.SaveChanges();

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[1].Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal("Ready", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Ready", check.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Cancelled", check.OrderItems.Single(x => x.Id == items[1].Id).Status);
        Assert.Equal(50m, check.Orders.Single(x => x.OrderId == order.OrderId).Subtotal);

        // Lịch sử có dòng "đơn được tạo" (P0-03) rồi mới tới dòng đổi trạng thái.
        var history = check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .OrderBy(x => x.Id)
            .Last();
        Assert.Equal("Preparing", history.OldStatus);
        Assert.Equal("Ready", history.NewStatus);
        Assert.Equal("Khách đổi món", history.Note);
    }

    [Fact]
    public async Task CancelItem_OnCompletedTicket_DoesNotReopenTicket()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 2);
        items[0].Status = "Completed";
        items[1].Status = "Ready";

        // Gán đúng trạng thái kitchen item theo order item.
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Completed";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[1].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Completed";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Completed";
        Db.SaveChanges();

        CurrentUser.UserId = scenario.Staff.Id;
        CurrentUser.RoleList.Add("Admin");

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Admin huỷ món đã phục vụ", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal("Completed", check.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Completed", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Cancelled", check.OrderItems.Single(x => x.Id == items[0].Id).Status);
        Assert.Equal(50m, check.Orders.Single(x => x.OrderId == order.OrderId).Subtotal);
    }

    [Fact]
    public async Task CancelItem_WhenPreparing_DoesNotReturnStock_AndAutoCancelsLastItem()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        SetItemPreparing(items[0]);

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Hết nguyên liệu", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        // Đã nấu thì không hoàn kho.
        Assert.Equal(98m, check.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);

        var stored = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Cancelled", stored.Status);
        Assert.Equal("Available", check.RestaurantTables.Single(x => x.TableId == scenario.Table.TableId).Status);

        Assert.Equal("Cancelled", check.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Cancelled", check.OrderItems.Single().Status);
        Assert.Equal("Hết nguyên liệu", check.OrderItems.Single().CancelReason);
    }

    // ============================ P1b: LUẬT THEO VAI TRÒ ============================

    [Fact]
    public async Task CancelItem_Preparing_IsRejectedForWaiter_ButAllowedForChef()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        SetItemPreparing(items[0]);

        CurrentUser.UserId = scenario.Staff.Id;
        CurrentUser.RoleList.Add("Waiter");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món" },
            CancellationToken.None));

        CurrentUser.RoleList.Clear();
        CurrentUser.RoleList.Add("Chef");

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Hết nguyên liệu" },
            CancellationToken.None));
    }

    [Fact]
    public async Task CancelItem_Ready_IsRejectedForChef_ButAllowedForAdmin()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        items[0].Status = "Ready";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.SaveChanges();

        CurrentUser.UserId = scenario.Staff.Id;
        CurrentUser.RoleList.Add("Chef");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món" },
            CancellationToken.None));

        CurrentUser.RoleList.Clear();
        CurrentUser.RoleList.Add("Admin");

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món" },
            CancellationToken.None));
    }

    [Fact]
    public async Task KitchenCancelOrder_IsAllowedForChef_BeforeAnyItemIsReady()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);
        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);

        CurrentUser.UserId = scenario.Staff.Id;
        CurrentUser.RoleList.Add("Chef");

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            kitchenOrder.KitchenOrderId,
            new KitchenStatusRequest { Status = "Cancelled", Reason = "Hết nguyên liệu" },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal(100m, check.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task KitchenCancelOrder_IsRejectedForChef_AfterAnItemIsReady()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        items[0].Status = "Ready";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.SaveChanges();

        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        CurrentUser.UserId = scenario.Staff.Id;
        CurrentUser.RoleList.Add("Chef");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Kitchen.UpdateOrderStatusAsync(
            kitchenOrder.KitchenOrderId,
            new KitchenStatusRequest { Status = "Cancelled", Reason = "Khách đổi ý" },
            CancellationToken.None));

        Assert.Equal("Ready", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    // ============================ C-07: PAID KHÔNG ĐƯỢC HUỶ TICKET ============================

    [Fact]
    public async Task KitchenCancelTicket_AfterPaid_IsRejected()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);
        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);

        order.Status = "Paid";
        kitchenOrder.Status = "Preparing";
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.UpdateOrderStatusAsync(
            kitchenOrder.KitchenOrderId,
            new KitchenStatusRequest { Status = "Cancelled", Reason = "Huỷ sau khi thu tiền" },
            CancellationToken.None));

        Assert.Contains("paid", ex.Message);
        Assert.Equal("Preparing", Db.KitchenOrders
            .Single(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId).Status);
        Assert.Equal("Paid", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal(98m, Stock(scenario)); // Không hoàn kho vì request bị chặn.
    }

    // ============================ C-08: HUỶ THEO LÝ DO ============================

    [Fact]
    public async Task CancelItem_OutOfStock_DoesNotReturnStock_AndRecordsWastage()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 2);
        Assert.Equal(96m, Stock(scenario));

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            items[0].Id,
            new CancelOrderItemRequest
            {
                Reason = "Hết nguyên liệu",
                ReasonCode = "OutOfStock",
                ChangedBy = scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == order.OrderId && x.Type == "AdjustmentIn");

        var wastage = Assert.Single(check.InventoryTransactions.Where(x =>
            x.OrderItemId == items[0].Id && x.Type == "Wastage"));
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Contains("OutOfStock", wastage.Note);
    }

    [Fact]
    public async Task CancelOrder_OutOfStock_DoesNotReturnStock_AndRecordsWastage()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 2);
        Assert.Equal(96m, Stock(scenario));

        Assert.True(await Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest
            {
                Status = "Cancelled",
                ChangedBy = scenario.Staff.Id,
                Note = "Hết nguyên liệu",
                ReasonCode = "OutOfStock"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == order.OrderId && x.Type == "AdjustmentIn");

        var wastage = check.InventoryTransactions
            .Where(x => x.ReferenceId == order.OrderId && x.Type == "Wastage")
            .ToList();
        Assert.Equal(2, wastage.Count);
        Assert.All(wastage, x => Assert.Equal(2m, x.Quanlity));
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task KitchenCancelItem_PhysicalShortage_DoesNotReturnStock_AndRecordsWastage()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);
        var kitchenItem = Db.KitchenOrderItems.Single(x => x.OrderItemId == Db.OrderItems
            .Single(i => i.OrderId == order.OrderId).Id);

        Assert.True(await Kitchen.UpdateItemStatusAsync(
            kitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = "Cancelled",
                Reason = "Nguyên liệu hỏng",
                ReasonCode = "PhysicalShortage"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == order.OrderId && x.Type == "AdjustmentIn");

        var wastage = Assert.Single(check.InventoryTransactions.Where(x =>
            x.ReferenceId == order.OrderId && x.Type == "Wastage"));
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task KitchenCancelTicket_OutOfStock_DoesNotReturnStock_AndRecordsWastage()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);
        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            kitchenOrder.KitchenOrderId,
            new KitchenStatusRequest
            {
                Status = "Cancelled",
                Reason = "Hết nguyên liệu",
                ReasonCode = "OutOfStock"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(98m, check.Inventories
            .Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions,
            x => x.ReferenceId == order.OrderId && x.Type == "AdjustmentIn");

        var wastage = Assert.Single(check.InventoryTransactions.Where(x =>
            x.ReferenceId == order.OrderId && x.Type == "Wastage"));
        Assert.Equal(2m, wastage.Quanlity);
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    // ============================ P1c: PHIẾU BẾP KHI MỌI MÓN BỊ HUỶ ============================

    [Fact]
    public async Task Kitchen_CancellingAllItems_ClosesTicketAndOrder()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 2);
        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        var kitchenItems = Db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .OrderBy(x => x.Id)
            .ToList();

        await Kitchen.UpdateItemStatusAsync(
            kitchenItems[0].Id,
            new KitchenItemStatusRequest { Status = "Cancelled", Reason = "Hết nguyên liệu" },
            CancellationToken.None);

        using (var mid = NewContext())
        {
            // Còn một món Pending ⇒ phiếu chưa được đóng.
            Assert.Equal("Pending", mid.KitchenOrders.Single(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId).Status);
            Assert.Equal("SentToKitchen", mid.Orders.Single(x => x.OrderId == order.OrderId).Status);
        }

        await Kitchen.UpdateItemStatusAsync(
            kitchenItems[1].Id,
            new KitchenItemStatusRequest { Status = "Cancelled", Reason = "Hết nguyên liệu" },
            CancellationToken.None);

        using var check = NewContext();
        Assert.Equal("Cancelled", check.KitchenOrders.Single(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId).Status);
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.All(check.KitchenOrderItems, x => Assert.Equal("Cancelled", x.Status));

        // Cả hai món còn Pending khi bị huỷ ⇒ hoàn đủ 4 đơn vị nguyên liệu.
        Assert.Equal(100m, check.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);
        Assert.Equal("Available", check.RestaurantTables.Single(x => x.TableId == scenario.Table.TableId).Status);
    }

    [Fact]
    public async Task Kitchen_CancellingOneOfTwoItems_RecalculatesBillAndResetsDiscount()
    {
        var (_, order, _) = await CreateOrderAsync(itemCount: 2);
        var kitchenOrder = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        var kitchenItems = Db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == kitchenOrder.KitchenOrderId)
            .OrderBy(x => x.Id)
            .ToList();

        // Giả lập đơn đã áp khuyến mãi trước khi bếp huỷ món.
        order.DiscountAmount = 10m;
        order.TotalAmount = 90m;
        Db.SaveChanges();

        await Kitchen.UpdateItemStatusAsync(
            kitchenItems[0].Id,
            new KitchenItemStatusRequest { Status = "Cancelled", Reason = "Hết nguyên liệu" },
            CancellationToken.None);

        using var check = NewContext();
        var stored = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(50m, stored.Subtotal);
        Assert.Equal(0m, stored.DiscountAmount);
        Assert.Equal(50m, stored.TotalAmount);
        Assert.Equal("SentToKitchen", stored.Status);

        var activeItems = check.OrderItems
            .Where(x => x.OrderId == order.OrderId && x.Status != "Cancelled")
            .ToList();
        Assert.Single(activeItems);
    }

    // ============================ P1b: HUỶ CẢ ĐƠN CHỈ HOÀN MÓN CHƯA NẤU ============================

    [Fact]
    public async Task CancelOrder_ReturnsStockOnlyForPendingItems()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 2);
        SetItemPreparing(items[0]);
        Assert.Equal(96m, Stock(scenario));

        Assert.True(await Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest
            {
                Status = "Cancelled",
                ChangedBy = scenario.Staff.Id,
                Note = "Khách huỷ đơn"
            },
            CancellationToken.None));

        using var check = NewContext();
        // Món 0 đang nấu: giữ nguyên tiêu hao 2. Món 1 Pending: hoàn 2.
        Assert.Equal(98m, check.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity);

        var returnRow = Assert.Single(check.InventoryTransactions.Where(x =>
            x.Type == "AdjustmentIn" && x.ReferenceId == order.OrderId));
        Assert.Equal(2m, returnRow.Quanlity);
        Assert.Equal(items[1].Id, returnRow.OrderItemId);

        Assert.All(check.OrderItems, x => Assert.Equal("Cancelled", x.Status));
        Assert.Equal("Cancelled", check.KitchenOrders.Single().Status);
        Assert.Equal("Khách huỷ đơn", check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .OrderBy(x => x.Id)
            .Last().Note);
    }

    // ============================ P1e: THANH TOÁN KHI BẾP CHƯA XONG ============================

    [Fact]
    public async Task Payment_Rejected_WhenKitchenIsNotDone()
    {
        var (scenario, order, _) = await CreateOrderAsync(itemCount: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(
            new CreatePaymentRequest
            {
                OrderId = order.OrderId,
                PaymentMethod = "Cash",
                CreatedBy = scenario.Staff.Id
            },
            CancellationToken.None));

        Assert.Equal("Order is not ready for payment", ex.Message);
        Assert.Equal("SentToKitchen", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task Payment_Allowed_WhenAllItemsReady()
    {
        var (scenario, order, items) = await CreateOrderAsync(itemCount: 1);
        items[0].Status = "Ready";
        Db.KitchenOrderItems.Single(x => x.OrderItemId == items[0].Id).Status = "Ready";
        Db.KitchenOrders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = "Ready";
        Db.SaveChanges();

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = scenario.Staff.Id
        }, CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal("Paid", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    private decimal Stock(OrderScenario scenario) =>
        Db.Inventories.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Quanlity;
}
