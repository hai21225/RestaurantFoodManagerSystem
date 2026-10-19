using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins current KitchenService behaviour, including status validation and order status propagation.</summary>
public sealed class KitchenFlowTests : TestBase
{
    private sealed record KitchenScenario(Order Order, KitchenOrder KitchenOrder, KitchenOrderItem First, KitchenOrderItem Second);

    private KitchenScenario SeedKitchen(int itemCount = 2)
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "SentToKitchen");
        var kitchenOrder = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Pending",
            SentAt = DateTime.UtcNow,
            Priority = 0
        };
        Db.KitchenOrders.Add(kitchenOrder);
        Db.SaveChanges();

        var items = new List<KitchenOrderItem>();
        for (var i = 0; i < itemCount; i++)
        {
            var orderItem = new OrderItem
            {
                OrderId = order.OrderId,
                SizeId = s.Size.SizeId,
                Quanlity = 1,
                UnitPrice = 10m,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            Db.OrderItems.Add(orderItem);
            Db.SaveChanges();

            var kitchenItem = new KitchenOrderItem
            {
                KitchenOrderId = kitchenOrder.KitchenOrderId,
                OrderItemId = orderItem.Id,
                Quanlity = 1,
                Status = "Pending"
            };
            Db.KitchenOrderItems.Add(kitchenItem);
            items.Add(kitchenItem);
        }
        Db.SaveChanges();

        return new KitchenScenario(order, kitchenOrder, items[0], items[itemCount - 1]);
    }

    [Fact]
    public async Task GetOrders_OrdersByPriorityThenSentAt()
    {
        var s = SeedOrderScenario();
        var orderA = SeedOrder(s.Table.TableId, s.Staff.Id);
        var orderB = SeedOrder(s.Table.TableId, s.Staff.Id);
        Db.KitchenOrders.Add(new KitchenOrder { OrderId = orderA.OrderId, Status = "Pending", SentAt = DateTime.UtcNow, Priority = 5 });
        Db.KitchenOrders.Add(new KitchenOrder { OrderId = orderB.OrderId, Status = "Pending", SentAt = DateTime.UtcNow, Priority = 1 });
        Db.SaveChanges();

        var result = await Kitchen.GetOrdersAsync(CancellationToken.None);

        Assert.Equal(new[] { 1, 5 }, result.Select(x => x.Priority));
    }

    [Fact]
    public async Task GetItems_ReturnsItemsOfKitchenOrder()
    {
        var scenario = SeedKitchen(itemCount: 2);

        var items = await Kitchen.GetItemsAsync(scenario.KitchenOrder.KitchenOrderId, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Empty(await Kitchen.GetItemsAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task GetItems_IncludesFoodAndSizeNames_AndCorrectlySpelledQuantity()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "SentToKitchen");
        var kitchenOrder = new KitchenOrder { OrderId = order.OrderId, Status = "Pending", SentAt = DateTime.UtcNow, Priority = 0 };
        Db.KitchenOrders.Add(kitchenOrder);
        Db.SaveChanges();

        var orderItem = new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = s.Size.SizeId,
            Quanlity = 4,
            UnitPrice = 12m,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        Db.OrderItems.Add(orderItem);
        Db.SaveChanges();

        Db.KitchenOrderItems.Add(new KitchenOrderItem
        {
            KitchenOrderId = kitchenOrder.KitchenOrderId,
            OrderItemId = orderItem.Id,
            Quanlity = 4,
            Status = "Pending",
            Note = "extra"
        });
        Db.SaveChanges();

        var item = Assert.Single(await Kitchen.GetItemsAsync(kitchenOrder.KitchenOrderId, CancellationToken.None));

        Assert.Equal(s.Food.FoodName, item.FoodName);
        Assert.Equal(s.Size.SizeName, item.SizeName);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(4, item.Quanlity); // legacy alias kept
        Assert.Equal("extra", item.Note);
    }

    [Fact]
    public async Task UpdateOrderStatus_RejectsStatusOutsideAllowList()
    {
        var scenario = SeedKitchen();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Kitchen.UpdateOrderStatusAsync(
            scenario.KitchenOrder.KitchenOrderId,
            new KitchenStatusRequest { Status = "NotAStatus" },
            CancellationToken.None));

        Assert.Equal("Invalid kitchen order status", ex.Message);
    }

    [Fact]
    public async Task UpdateOrderStatus_ReturnsFalse_WhenKitchenOrderMissing()
    {
        Assert.False(await Kitchen.UpdateOrderStatusAsync(999999, new KitchenStatusRequest { Status = "Accepted" }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateOrderStatus_Accepted_SetsStartedAt_AndOrderStaysSentToKitchen()
    {
        var scenario = SeedKitchen();

        var result = await Kitchen.UpdateOrderStatusAsync(
            scenario.KitchenOrder.KitchenOrderId,
            new KitchenStatusRequest { Status = "Accepted" },
            CancellationToken.None);

        Assert.True(result);
        var kitchen = Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId);
        Assert.Equal("Accepted", kitchen.Status);
        Assert.NotNull(kitchen.StartedAt);
        Assert.Equal("SentToKitchen", Db.Orders.Single(x => x.OrderId == scenario.Order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_Completed_SetsCompletedAt_AndOrderBecomesReady()
    {
        var scenario = SeedKitchen();

        await Kitchen.UpdateOrderStatusAsync(scenario.KitchenOrder.KitchenOrderId, new KitchenStatusRequest { Status = "Completed" }, CancellationToken.None);

        var kitchen = Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId);
        Assert.Equal("Completed", kitchen.Status);
        Assert.NotNull(kitchen.CompletedAt);
        Assert.Equal("Ready", Db.Orders.Single(x => x.OrderId == scenario.Order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_Cancelled_CancelsOrder()
    {
        // Nhóm B1: trước đây "Cancelled" bị ánh xạ về "SentToKitchen" → huỷ phiếu bếp lại mở lại đơn.
        var scenario = SeedKitchen();

        await Kitchen.UpdateOrderStatusAsync(scenario.KitchenOrder.KitchenOrderId, new KitchenStatusRequest { Status = "Cancelled" }, CancellationToken.None);

        Assert.Equal("Cancelled", Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId).Status);
        Assert.Equal("Cancelled", Db.Orders.Single(x => x.OrderId == scenario.Order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_TrimsIncomingStatus()
    {
        var scenario = SeedKitchen();

        await Kitchen.UpdateOrderStatusAsync(scenario.KitchenOrder.KitchenOrderId, new KitchenStatusRequest { Status = "  Preparing  " }, CancellationToken.None);

        Assert.Equal("Preparing", Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId).Status);
        Assert.Equal("Preparing", Db.Orders.Single(x => x.OrderId == scenario.Order.OrderId).Status);
    }

    [Fact]
    public async Task UpdateItemStatus_RejectsStatusOutsideAllowList()
    {
        var scenario = SeedKitchen();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Kitchen.UpdateItemStatusAsync(
            scenario.First.Id,
            new KitchenItemStatusRequest { Status = "Delivered" },
            CancellationToken.None));

        Assert.Equal("Invalid kitchen item status", ex.Message);
    }

    [Fact]
    public async Task UpdateItemStatus_ReturnsFalse_WhenItemMissing()
    {
        Assert.False(await Kitchen.UpdateItemStatusAsync(999999, new KitchenItemStatusRequest { Status = "Ready" }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateItemStatus_PropagatesToOrderItem()
    {
        var scenario = SeedKitchen();

        await Kitchen.UpdateItemStatusAsync(scenario.First.Id, new KitchenItemStatusRequest { Status = "Preparing" }, CancellationToken.None);

        var kitchenItem = Db.KitchenOrderItems.Single(x => x.Id == scenario.First.Id);
        Assert.Equal("Preparing", kitchenItem.Status);
        Assert.Equal("Preparing", Db.OrderItems.Single(x => x.Id == kitchenItem.OrderItemId).Status);
        Assert.Equal("Preparing", Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId).Status);
    }

    [Fact]
    public async Task UpdateItemStatus_WhenAllItemsReady_KitchenAndOrderBecomeReady()
    {
        var scenario = SeedKitchen(itemCount: 2);

        await Kitchen.UpdateItemStatusAsync(scenario.First.Id, new KitchenItemStatusRequest { Status = "Ready" }, CancellationToken.None);
        await Kitchen.UpdateItemStatusAsync(scenario.Second.Id, new KitchenItemStatusRequest { Status = "Completed" }, CancellationToken.None);

        Assert.Equal("Ready", Db.KitchenOrders.Single(x => x.KitchenOrderId == scenario.KitchenOrder.KitchenOrderId).Status);
        Assert.Equal("Ready", Db.Orders.Single(x => x.OrderId == scenario.Order.OrderId).Status);
    }
}
