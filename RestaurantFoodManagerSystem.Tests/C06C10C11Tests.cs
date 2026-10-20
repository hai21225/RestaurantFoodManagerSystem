using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-06 (mở lại món), C-10 (audit dòng món) và C-11 (bỏ Accepted ở cấp món).
/// </summary>
public sealed class C06C10C11Tests : TestBase
{
    private sealed record KitchenScenario(
        OrderScenario Scenario,
        Order Order,
        KitchenOrder KitchenOrder,
        OrderItem OrderItem,
        KitchenOrderItem KitchenItem);

    private KitchenScenario SeedKitchen(int quantity = 1, string orderStatus = "SentToKitchen")
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = SeedOrder(scenario.Table.TableId, scenario.Staff.Id, status: orderStatus);

        var kitchenOrder = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Pending",
            SentAt = DateTime.UtcNow,
            Priority = 0
        };
        Db.KitchenOrders.Add(kitchenOrder);
        Db.SaveChanges();

        var orderItem = new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = scenario.Size.SizeId,
            Quanlity = quantity,
            UnitPrice = 50m,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        Db.OrderItems.Add(orderItem);
        Db.SaveChanges();

        var kitchenItem = new KitchenOrderItem
        {
            KitchenOrderId = kitchenOrder.KitchenOrderId,
            OrderItemId = orderItem.Id,
            Quanlity = quantity,
            Status = "Pending"
        };
        Db.KitchenOrderItems.Add(kitchenItem);
        Db.SaveChanges();

        return new KitchenScenario(scenario, order, kitchenOrder, orderItem, kitchenItem);
    }

    private async Task<(OrderScenario Scenario, Order Order, OrderItem Item)> CreateOrderAsync(int quantity)
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
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
        return (scenario, order, item);
    }

    // ============================ C-06 ============================

    [Fact]
    public async Task ReopenItem_ReadyItem_MovesTicketAndOrderBackToPreparing()
    {
        var scenario = SeedKitchen();

        Assert.True(await Kitchen.UpdateItemStatusAsync(
            scenario.KitchenItem.Id,
            new KitchenItemStatusRequest { Status = "Ready" },
            CancellationToken.None));

        Assert.Equal("Ready", scenario.KitchenOrder.Status);
        Assert.Equal("Ready", scenario.Order.Status);

        Assert.True(await Kitchen.ReopenItemAsync(
            scenario.KitchenItem.Id,
            new ReopenKitchenItemRequest { Reason = "Khách yêu cầu làm lại" },
            CancellationToken.None));

        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.Id == scenario.KitchenItem.Id);
        var orderItem = await Db.OrderItems.SingleAsync(x => x.Id == scenario.OrderItem.Id);
        var kitchenOrder = await Db.KitchenOrders.SingleAsync(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId);
        var order = await Db.Orders.SingleAsync(x => x.OrderId == scenario.Order.OrderId);

        Assert.Equal("Preparing", kitchenItem.Status);
        Assert.Equal("Preparing", orderItem.Status);
        Assert.Equal("Preparing", kitchenOrder.Status);
        Assert.Equal("Preparing", order.Status);
        Assert.Null(kitchenOrder.CompletedAt);

        var history = await Orders.GetItemHistoryAsync(order.OrderId);
        Assert.NotNull(history);
        var itemHistory = history
            .Where(x => x.OrderItemId == orderItem.Id)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToList();
        Assert.Equal(2, itemHistory.Count);
        var row = itemHistory[^1];
        Assert.Equal("Ready", row.OldStatus);
        Assert.Equal("Preparing", row.NewStatus);
        Assert.Equal("Khách yêu cầu làm lại", row.Reason);
        Assert.Equal(kitchenItem.Id, row.KitchenOrderItemId);
        Assert.Equal(scenario.Scenario.Staff.Id, row.ChangedBy);
    }

    [Fact]
    public async Task ReopenItem_RejectsPaidOrder()
    {
        var scenario = SeedKitchen();
        scenario.KitchenItem.Status = "Ready";
        scenario.OrderItem.Status = "Ready";
        scenario.KitchenOrder.Status = "Ready";
        scenario.Order.Status = "Paid";
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.ReopenItemAsync(
            scenario.KitchenItem.Id,
            new ReopenKitchenItemRequest(),
            CancellationToken.None));

        Assert.Contains("Order is Paid", ex.Message);
    }

    [Fact]
    public async Task ReopenItem_RejectsTerminalTicket()
    {
        var scenario = SeedKitchen();
        scenario.KitchenItem.Status = "Completed";
        scenario.OrderItem.Status = "Completed";
        scenario.KitchenOrder.Status = "Completed";
        scenario.Order.Status = "Ready";
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.ReopenItemAsync(
            scenario.KitchenItem.Id,
            new ReopenKitchenItemRequest(),
            CancellationToken.None));

        Assert.Contains("Kitchen order is Completed", ex.Message);
    }

    [Fact]
    public async Task ReopenItem_RejectsItemThatIsNotDone()
    {
        var scenario = SeedKitchen();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.ReopenItemAsync(
            scenario.KitchenItem.Id,
            new ReopenKitchenItemRequest(),
            CancellationToken.None));

        Assert.Contains("Only a Ready/Completed item", ex.Message);
    }

    // ============================ C-10 ============================

    [Fact]
    public async Task CancelItem_WritesReasonAndReasonCodeToItemHistory()
    {
        var (scenario, order, item) = await CreateOrderAsync(quantity: 1);

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            item.Id,
            new CancelOrderItemRequest
            {
                Reason = "Hết nguyên liệu",
                ReasonCode = "OutOfStock",
                ChangedBy = scenario.Staff.Id
            },
            CancellationToken.None));

        var history = await Orders.GetItemHistoryAsync(order.OrderId);
        Assert.NotNull(history);
        var row = Assert.Single(history, x => x.NewStatus == "Cancelled");
        Assert.Equal("Pending", row.OldStatus);
        Assert.Equal(item.Id, row.OrderItemId);
        Assert.Equal("Hết nguyên liệu", row.Reason);
        Assert.Equal("OutOfStock", row.ReasonCode);
        Assert.Equal(scenario.Staff.Id, row.ChangedBy);
        Assert.Equal("Test User", row.ChangedByName);
    }

    [Fact]
    public async Task AdjustQuantity_PartialReduction_WritesHistoryWithoutCancellingItem()
    {
        var (scenario, order, item) = await CreateOrderAsync(quantity: 3);

        Assert.True(await Orders.AdjustItemQuantityAsync(
            order.OrderId,
            item.Id,
            new AdjustOrderItemQuantityRequest
            {
                Quantity = 2,
                ChangedBy = scenario.Staff.Id,
                Reason = "Khách giảm 1 phần",
                ReasonCode = "CustomerChange"
            },
            CancellationToken.None));

        var updatedItem = await Db.OrderItems.SingleAsync(x => x.Id == item.Id);
        Assert.Equal(2, updatedItem.Quanlity);
        Assert.NotEqual("Cancelled", updatedItem.Status);

        var history = await Orders.GetItemHistoryAsync(order.OrderId);
        Assert.NotNull(history);
        var row = Assert.Single(history, x => x.OrderItemId == item.Id);
        Assert.Equal("Pending", row.OldStatus);
        Assert.Equal("Pending", row.NewStatus);
        Assert.Contains("3 -> 2", row.Reason);
        Assert.Equal("CustomerChange", row.ReasonCode);
    }

    [Fact]
    public async Task PreparedQuantity_Split_WritesHistoryForRemainingAndPreparedLines()
    {
        var (scenario, order, item) = await CreateOrderAsync(quantity: 2);
        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.OrderItemId == item.Id);

        Assert.True(await Kitchen.UpdatePreparedQuantityAsync(
            kitchenItem.Id,
            new PreparedQuantityRequest
            {
                PreparedQuantity = 1,
                ChangedBy = scenario.Staff.Id,
                Reason = "Mới nấu được 1 phần"
            },
            CancellationToken.None));

        var history = await Orders.GetItemHistoryAsync(order.OrderId);
        Assert.NotNull(history);
        Assert.Contains(history, x => x.OrderItemId == item.Id && x.Reason!.Contains("Prepared 1"));
        Assert.Contains(history, x => x.NewStatus == "Completed" && x.OrderItemId != item.Id);
    }

    [Fact]
    public async Task KitchenCancelTicket_WritesReasonToOrderAndItemHistory()
    {
        var scenario = SeedKitchen();

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            scenario.KitchenOrder.KitchenOrderId,
            new KitchenStatusRequest
            {
                Status = "Cancelled",
                Reason = "Hết nguyên liệu",
                ReasonCode = "OutOfStock"
            },
            CancellationToken.None));

        var orderHistory = await Orders.GetHistoryAsync(scenario.Order.OrderId);
        Assert.NotNull(orderHistory);
        var orderRow = Assert.Single(orderHistory, x => x.NewStatus == "Cancelled");
        Assert.Equal("Hết nguyên liệu", orderRow.Note);

        var itemHistory = await Orders.GetItemHistoryAsync(scenario.Order.OrderId);
        Assert.NotNull(itemHistory);
        var itemRow = Assert.Single(itemHistory, x => x.NewStatus == "Cancelled");
        Assert.Equal("Hết nguyên liệu", itemRow.Reason);
        Assert.Equal("OutOfStock", itemRow.ReasonCode);
    }

    [Fact]
    public async Task ItemHistory_ReturnsNullForMissingOrder()
    {
        Assert.Null(await Orders.GetItemHistoryAsync(999_999));
    }

    // ============================ C-11 ============================

    [Fact]
    public async Task UpdateItemStatus_RejectsAcceptedAtItemLevel()
    {
        var scenario = SeedKitchen();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Kitchen.UpdateItemStatusAsync(
            scenario.KitchenItem.Id,
            new KitchenItemStatusRequest { Status = "Accepted" },
            CancellationToken.None));

        Assert.Equal("Invalid kitchen item status", ex.Message);
    }

    [Fact]
    public void Accepted_IsOnlyAllowedAtTicketLevel()
    {
        Assert.Contains("Accepted", KitchenStatuses.AllowedTicket);
        Assert.DoesNotContain("Accepted", KitchenStatuses.AllowedItem);
    }
}
