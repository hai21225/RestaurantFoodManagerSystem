using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt sửa P0 — đồng bộ đơn ↔ phiếu bếp, audit đầy đủ, dữ liệu không âm.
/// Mọi case ở đây đều là hành vi **đúng** theo luật chốt 2026-10-21
/// (<c>docs/architecture/kitchen-stock-rule-2026-10-21.md</c> và
/// <c>docs/architecture/backend-gap-review-2026-10-21.md</c>).
/// </summary>
public sealed class CoreOrderKitchenTests : TestBase
{
    private sealed record CreatedOrder(
        OrderScenario Scenario,
        Order Order,
        OrderItem Item,
        KitchenOrder Ticket,
        KitchenOrderItem KitchenItem);

    private async Task<CreatedOrder> CreateOrderAsync(int quantity = 1, decimal stock = 100m)
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

    // ==================== P0-02 / P0-03: lịch sử đơn ====================

    [Fact]
    public async Task CreateOrder_WritesOrderStatusHistory_StartingFromPending()
    {
        var created = await CreateOrderAsync();

        using var check = NewContext();
        var history = check.OrderStatusHistories
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .ToList();

        // P0-03: lịch sử đơn có điểm bắt đầu, không bắt đầu giữa chừng.
        var first = history.First();
        Assert.Equal(OrderStatuses.Pending, first.OldStatus);
        Assert.Equal(OrderStatuses.SentToKitchen, first.NewStatus);
        Assert.Equal(created.Scenario.Staff.Id, first.ChangedBy);
    }

    [Fact]
    public async Task Payment_WritesOrderStatusHistory_ToPaid()
    {
        var created = await CreateOrderAsync();

        // Bếp xong món ⇒ đơn Ready ⇒ được thu tiền.
        created.Item.Status = KitchenStatuses.Completed;
        created.KitchenItem.Status = KitchenStatuses.Completed;
        created.Ticket.Status = KitchenStatuses.Completed;
        created.Order.Status = OrderStatuses.Ready;
        await Db.SaveChangesAsync();

        Assert.NotNull(await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = created.Order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = created.Scenario.Staff.Id
        }, CancellationToken.None));

        using var check = NewContext();
        var paid = check.OrderStatusHistories
            .Where(x => x.OrderId == created.Order.OrderId)
            .OrderBy(x => x.Id)
            .Last();

        // P0-02: lịch sử đơn phải có dòng "→ Paid", nếu không mất dấu vết thu tiền.
        Assert.Equal(OrderStatuses.Ready, paid.OldStatus);
        Assert.Equal(OrderStatuses.Paid, paid.NewStatus);
        Assert.Equal(created.Scenario.Staff.Id, paid.ChangedBy);
    }

    // ==================== P0-04: lịch sử dòng món ====================

    [Fact]
    public async Task CreateOrder_WritesItemHistory_ForEveryNewDish()
    {
        var created = await CreateOrderAsync(quantity: 2);

        using var check = NewContext();
        var rows = check.OrderItemStatusHistories
            .Where(x => x.OrderId == created.Order.OrderId && x.OrderItemId == created.Item.Id)
            .OrderBy(x => x.Id)
            .ToList();

        // P0-04: dòng món vừa tạo cũng có dòng audit (biết món vào bếp lúc nào).
        Assert.Single(rows);
        Assert.Equal(string.Empty, rows[0].OldStatus);
        Assert.Equal(KitchenStatuses.Pending, rows[0].NewStatus);
        Assert.Equal(created.Scenario.Staff.Id, rows[0].ChangedBy);
    }

    // ==================== P0-08: huỷ đơn không đụng phiếu đã đóng ====================

    [Fact]
    public async Task CancelOrder_KeepsCompletedTicket_AndCancelsOpenTicket()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items = { new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 } }
        }, CancellationToken.None);

        // Phiếu 1 đã đóng vận hành (Completed), phiếu 2 đang mở (Preparing).
        var closedTicket = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = KitchenStatuses.Completed,
            SentAt = DateTime.UtcNow.AddMinutes(-30),
            CompletedAt = DateTime.UtcNow.AddMinutes(-20)
        };
        Db.KitchenOrders.Add(closedTicket);
        await Db.SaveChangesAsync();

        var closedItem = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        Db.KitchenOrderItems.Add(new KitchenOrderItem
        {
            KitchenOrderId = closedTicket.KitchenOrderId,
            OrderItemId = closedItem.Id,
            Quanlity = 1,
            Status = KitchenStatuses.Completed
        });
        await Db.SaveChangesAsync();

        var openTicket = await Db.KitchenOrders.SingleAsync(x => x.OrderId == order.OrderId && x.Status != KitchenStatuses.Completed);
        var openItem = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        Db.KitchenOrderItems.Add(new KitchenOrderItem
        {
            KitchenOrderId = openTicket.KitchenOrderId,
            OrderItemId = openItem.Id,
            Quanlity = 1,
            Status = KitchenStatuses.Preparing
        });
        await Db.SaveChangesAsync();

        await Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest
            {
                Status = OrderStatuses.Cancelled,
                Note = "Khách huỷ đơn",
                ChangedBy = scenario.Staff.Id
            },
            CancellationToken.None);

        using var check = NewContext();

        // P0-08: phiếu đã Completed là terminal — không được đánh dấu huỷ.
        Assert.Equal(
            KitchenStatuses.Completed,
            check.KitchenOrders.Single(x => x.KitchenOrderId == closedTicket.KitchenOrderId).Status);
        Assert.Equal(
            KitchenStatuses.Cancelled,
            check.KitchenOrders.Single(x => x.KitchenOrderId == openTicket.KitchenOrderId).Status);
    }

    // ==================== P0-11: bếp ghi đúng người ====================

    [Fact]
    public async Task KitchenAction_RecordsTheActorFromRequest_NotTheOrderCreator()
    {
        var created = await CreateOrderAsync();
        var chef = SeedUser();

        // Không có token ⇒ actor lấy từ body (ChangedBy), không phải người tạo đơn.
        Assert.True(await Kitchen.UpdateItemStatusAsync(
            created.KitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = KitchenStatuses.Preparing,
                ChangedBy = chef.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var row = check.OrderItemStatusHistories
            .Where(x => x.OrderItemId == created.Item.Id)
            .OrderBy(x => x.Id)
            .Last();

        Assert.Equal(chef.Id, row.ChangedBy);
        Assert.NotEqual(created.Scenario.Staff.Id, row.ChangedBy);
    }

    // ==================== P0-06: shortage không tạo số lượng âm ====================

    [Fact]
    public async Task ReportShortage_WhenOrderItemQuantityDrifted_RejectsInsteadOfCreatingNegativeLine()
    {
        var created = await CreateOrderAsync(quantity: 1);
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        // Dữ liệu lệch: kitchen item nhiều hơn order item.
        created.KitchenItem.Quanlity = 3;
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Kitchen.ReportShortageAsync(
            created.KitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 2,
                ReasonCode = "PhysicalShortage",
                Reason = "Nguyên liệu hư"
            },
            CancellationToken.None));

        using var check = NewContext();
        // Không được tạo dòng món số lượng âm.
        Assert.All(check.OrderItems, x => Assert.True(x.Quanlity >= 0));
    }

    // ==================== GetAsync: LEFT JOIN (phòng thủ) ====================

    /// <summary>
    /// <c>GetAsync</c> dùng LEFT JOIN nên dòng món không biến mất khỏi chi tiết đơn khi
    /// món/size không còn tồn tại (thiếu món ⇒ POS hiển thị sai, tổng tiền lệch).
    ///
    /// <para>
    /// Hiện EF/DB đã chặn sẵn trường hợp này (<c>order_items.size_id</c> là <c>Restrict</c>,
    /// EF không cho cắt quan hệ bắt buộc) nên **không dựng được** tình huống trong test.
    /// Đây là lưới an toàn phòng thủ, không phải sửa một lỗi đang xảy ra.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetOrderDetail_ReturnsDishRow_WithFoodAndSizeNames()
    {
        var created = await CreateOrderAsync();

        var detail = await Orders.GetAsync(created.Order.OrderId, CancellationToken.None);

        Assert.NotNull(detail);
        var item = Assert.Single(detail!.Items);
        Assert.Equal(created.Item.Id, item.Id);
        Assert.Equal(1, item.Quanlity);
        Assert.Equal(created.Scenario.Food.FoodName, item.FoodName);
        Assert.Equal(created.Scenario.Size.SizeName, item.SizeName);
    }
}
