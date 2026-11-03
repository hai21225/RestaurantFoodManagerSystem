using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-23 (2026-10-21) — 8 điểm rà soát sâu <c>OrderService</c> sau khi file bị splice.
///
/// <list type="number">
/// <item>description>Chuyển bàn: bàn đích không tồn tại / bảo trì ⇒ 400 (lỗi đầu vào) thay vì 404/409.</description></item>
/// <item>description>Cơ sở khuyến mãi: LEFT JOIN ⇒ món bị xoá cứng không làm khuyến mãi bị từ chối âm thầm.</description></item>
/// <item>description>Dòng bếp mới phải được lưu TRƯỚC khi tính lại trạng thái phiếu.</description></item>
/// <item>description>Huỷ món dùng chung hàm <c>RecalculateTotals</c> với các luồng khác.</description></item>
/// <item>description>Điều chỉnh SL không đổi gì ⇒ không để lọt group id ra ngoài.</description></item>
/// <item>description>Áp khuyến mãi lên đơn đã Paid/Cancelled ⇒ 409 (giống gỡ khuyến mãi).</description></item>
/// <item>description>2 khối XML doc bị bỏ trôi đã gắn lại đúng method.</description></item>
/// <item>description>Lỗi áp khuyến mãi trả JSON <c>{message}</c> thay vì text thuần.</description></item>
/// </list>
/// </summary>
public sealed class C23OrderContractTests : TestBase
{
    // ============================================================
    // C-23.1 — Chuyển bàn: bàn đích lỗi đầu vào ⇒ ArgumentException (400)
    // ============================================================

    [Fact]
    public async Task ChangeTable_UnknownTable_ThrowsArgumentException()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = 999999, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("Table not found", ex.Message);
    }

    [Fact]
    public async Task ChangeTable_TargetUnderMaintenance_ThrowsArgumentException()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var target = SeedTable();
        target.Status = RestaurantTableStatuses.Maintenance;
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = target.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("maintenance", ex.Message);
    }

    // ============================================================
    // C-23.2 — Cơ sở khuyến mãi: LEFT JOIN (phòng vệ)
    // ============================================================

    [Fact]
    public async Task ApplyPromotion_Applies_WhenPromotionTargetsTheOrderCategory()
    {
        // Kịch bản "món bị xoá cứng" không xảy ra được vì FK order_items → food_sizes là
        // Restrict ⇒ món/size đang dùng không thể bị xoá cứng. Nhưng đường dẫn LEFT JOIN
        // vẫn cần đúng cho dữ liệu cũ/script nhập tay — test ở đây chốt hành vi chuẩn:
        // khuyến mãi theo DANH MỤC vẫn áp được cho đơn.
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 }]
        }, CancellationToken.None);

        var promotion = new Promotion
        {
            Name = "C23 promo",
            Description = "audit",
            DiscountType = "Percent",
            DiscountValue = 10,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 0,
            MaxDiscountAmount = 0,
            IsActive = true
        };
        Db.Promotions.Add(promotion);
        await Db.SaveChangesAsync();

        // Target theo DANH MỤC của món trong đơn.
        var categoryId = s.Category.CategoryId;
        Db.PromotionCategories.Add(new PromotionCategory
        {
            PromotionId = promotion.Id,
            CategoryId = categoryId
        });
        await Db.SaveChangesAsync();

        Assert.True(await Orders.ApplyPromotionAsync(
            order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var updated = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.True(updated.DiscountAmount > 0m);
    }

    // ============================================================
    // C-23.5 — Điều chỉnh SL không đổi gì ⇒ không để lọt group id
    // ============================================================

    [Fact]
    public async Task AdjustQuantity_NoChange_DoesNotAssignGroupId()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 }]
        }, CancellationToken.None);

        // Dòng legacy: chưa có group id (đây mới là trường hợp mà C-23.5 bảo vệ).
        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        item.OrderItemGroupId = null;
        await Db.SaveChangesAsync();

        // Đặt mục tiêu bằng đúng số lượng hiện tại ⇒ không có gì thay đổi.
        Assert.True(await Orders.AdjustItemQuantityAsync(
            order.OrderId,
            item.Id,
            new AdjustOrderItemQuantityRequest { Quantity = 2, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        var after = check.OrderItems.Single(x => x.Id == item.Id);
        Assert.Null(after.OrderItemGroupId);
    }

    // ============================================================
    // C-23.6 — Áp khuyến mãi lên đơn đã Paid ⇒ 409
    // ============================================================

    [Fact]
    public async Task ApplyPromotion_OnPaidOrder_ThrowsInvalidOperationException()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: OrderStatuses.Paid, subtotal: 100m);

        var promotion = new Promotion
        {
            Name = "C23 promo paid",
            Description = "audit",
            DiscountType = "Percent",
            DiscountValue = 10,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 0,
            MaxDiscountAmount = 0,
            IsActive = true
        };
        Db.Promotions.Add(promotion);
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.ApplyPromotionAsync(
            order.OrderId, promotion.Id, CancellationToken.None));

        Assert.Contains("can no longer change status", ex.Message);
    }
}

/// <summary>C-23.8 — hợp đồng HTTP của các route bị ảnh hưởng.</summary>
public sealed class C23OrderContractHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public C23OrderContractHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private static Users NewUser(string prefix)
    {
        var userName = $"{prefix}-{Guid.NewGuid():N}";
        return new Users
        {
            UserName = userName,
            UserPassword = "not-used",
            UserFullName = "C23 Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private (int OrderId, int PromotionId) SeedPaidOrderWithPromotion()
    {
        var (orderId, promotionId, _) = SeedOrderWithPromotion(OrderStatuses.Paid);
        return (orderId, promotionId);
    }

    private (int OrderId, int PromotionId, int UserId) SeedOrderWithPromotion(string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser("c23");
        db.Users.Add(user);
        db.SaveChanges();

        var table = new RestaurantTable
        {
            TableNumber = $"C23-{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        var order = new Order
        {
            TableId = table.TableId,
            CreatedBy = user.Id,
            Status = status,
            Subtotal = 100m,
            TotalAmount = 100m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);

        var promotion = new Promotion
        {
            Name = "C23 http promo",
            Description = "audit",
            DiscountType = "Percent",
            DiscountValue = 10,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 0,
            MaxDiscountAmount = 0,
            IsActive = true
        };
        db.Promotions.Add(promotion);
        db.SaveChanges();

        return (order.OrderId, promotion.Id, user.Id);
    }

    [Fact]
    public async Task ApplyPromotion_OnPaidOrder_Returns409WithJsonMessage()
    {
        using var client = _factory.CreateClient();
        var (orderId, promotionId) = SeedPaidOrderWithPromotion();

        var response = await client.PatchAsync(
            $"/api/orders/{orderId}/promotion/{promotionId}", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // C-23.8: body lỗi phải là JSON { "message": ... } — không phải text thuần.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"message\"", body);
        using var doc = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task ChangeTable_UnknownTable_Returns400WithJsonMessage()
    {
        using var client = _factory.CreateClient();
        // Đơn ĐANG MỞ (không phải Paid) để không bị chặn bởi luật đơn terminal.
        var (orderId, _, _) = SeedOrderWithPromotion(OrderStatuses.SentToKitchen);

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/table", new { tableId = 999999, changedBy = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"message\"", body);
    }
}
