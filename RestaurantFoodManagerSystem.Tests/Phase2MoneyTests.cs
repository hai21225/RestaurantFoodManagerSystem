using System.Collections;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 2 (2026-10-21) — "Tiền đúng": settings + VAT + phí dịch vụ, khuyến mãi 2 chiều,
/// idempotency cho lệnh tiền–số lượng, múi giờ khuyến mãi, mã lý do ở lịch sử đơn.
///
/// <para>Luật đã chốt (chủ dự án):</para>
/// <list type="bullet">
/// <item><description>Giá menu <b>đã bao gồm</b> VAT ⇒ VAT chỉ tách ra để in, không cộng thêm.</description></item>
/// <item><description>Phí dịch vụ tính trên <b>tiền hàng sau giảm giá</b>, luôn cộng thêm vào tổng.</description></item>
/// <item><description>Khuyến mãi: client gửi giờ VN ⇒ server lưu UTC; response trả cả giờ VN lẫn UTC.</description></item>
/// </list>
/// </summary>
public sealed class Phase2MoneyTests : TestBase
{
    // ==================== A1 — công thức tiền ====================

    private static RestaurantSettings MakeSettings(
        decimal taxRate = 10m,
        bool taxIncluded = true,
        decimal serviceChargeRate = 5m) => new()
        {
            Id = RestaurantSettings.SingletonId,
            TaxRate = taxRate,
            TaxIncluded = taxIncluded,
            ServiceChargeRate = serviceChargeRate,
            Currency = "VND"
        };

    [Fact]
    public void Totals_TaxIncluded_SplitsVatOutWithoutAddingToTotal()
    {
        // 100.000 đã gồm VAT 10% ⇒ VAT trong đó là 100.000/1,1*10% = 9.090,91.
        // Phí DV 5% = 5.000. Khách trả 105.000 (VAT không cộng thêm).
        var totals = OrderTotalsCalculator.Calculate(100_000m, 0m, MakeSettings());

        Assert.Equal(100_000m, totals.Subtotal);
        Assert.Equal(9_090.91m, totals.TaxAmount);
        Assert.Equal(5_000m, totals.ServiceChargeAmount);
        Assert.Equal(105_000m, totals.TotalAmount);
    }

    [Fact]
    public void Totals_TaxNotIncluded_AddsVatToTotal()
    {
        // 100.000 chưa gồm VAT ⇒ VAT 10.000 cộng thêm, phí DV 5.000 ⇒ 115.000.
        var totals = OrderTotalsCalculator.Calculate(100_000m, 0m, MakeSettings(taxIncluded: false));

        Assert.Equal(10_000m, totals.TaxAmount);
        Assert.Equal(5_000m, totals.ServiceChargeAmount);
        Assert.Equal(115_000m, totals.TotalAmount);
    }

    [Fact]
    public void Totals_ServiceChargeAndTaxComputedOnPostDiscountAmount()
    {
        // Giảm 20.000 ⇒ base = 80.000: VAT = 80.000/1,1*10% = 7.272,73; phí DV = 4.000.
        var totals = OrderTotalsCalculator.Calculate(100_000m, 20_000m, MakeSettings());

        Assert.Equal(20_000m, totals.DiscountAmount);
        Assert.Equal(7_272.73m, totals.TaxAmount);
        Assert.Equal(4_000m, totals.ServiceChargeAmount);
        Assert.Equal(84_000m, totals.TotalAmount);
    }

    [Fact]
    public void Totals_DefaultSettings_KeepLegacyBehaviour()
    {
        // Không cấu hình VAT/phí DV ⇒ tổng = tiền hàng − giảm giá (đúng hành vi hệ thống cũ).
        var totals = OrderTotalsCalculator.Calculate(100_000m, 20_000m, MakeSettings(taxRate: 0m, serviceChargeRate: 0m));

        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(0m, totals.ServiceChargeAmount);
        Assert.Equal(80_000m, totals.TotalAmount);
    }

    [Fact]
    public async Task Settings_ReturnDefaults_WhenNoRowExists()
    {
        var settings = await SettingsService.GetAsync(CancellationToken.None);

        Assert.Equal(RestaurantSettings.SingletonId, settings.Id);
        Assert.Equal(0m, settings.TaxRate);
        Assert.Equal(0m, settings.ServiceChargeRate);
        Assert.True(settings.TaxIncluded);
        Assert.Equal("VND", settings.Currency);

        // Đường đọc KHÔNG được tự ghi DB.
        Assert.Empty(Db.RestaurantSettings);
    }

    [Fact]
    public async Task Settings_Update_CreatesSingleSingletonRow()
    {
        await SettingsService.UpdateAsync(new RestaurantSettingsRequest
        {
            RestaurantName = "Quán Test",
            TaxCode = "0123456789",
            TaxRate = 10m,
            TaxIncluded = true,
            ServiceChargeRate = 5m
        }, CancellationToken.None);

        await SettingsService.UpdateAsync(new RestaurantSettingsRequest
        {
            RestaurantName = "Quán Test 2",
            TaxRate = 8m,
            TaxIncluded = false,
            ServiceChargeRate = 3m
        }, CancellationToken.None);

        // Chỉ có đúng một bản ghi dù cập nhật nhiều lần.
        Assert.Single(Db.RestaurantSettings);

        var settings = await SettingsService.GetAsync(CancellationToken.None);
        Assert.Equal("Quán Test 2", settings.RestaurantName);
        Assert.Equal(8m, settings.TaxRate);
        Assert.Equal(3m, settings.ServiceChargeRate);
        Assert.False(settings.TaxIncluded);
    }

    [Fact]
    public async Task CreateOrder_AppliesVatAndServiceCharge()
    {
        await SettingsService.UpdateAsync(new RestaurantSettingsRequest
        {
            TaxRate = 10m,
            TaxIncluded = true,
            ServiceChargeRate = 5m
        }, CancellationToken.None);

        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 }]
        }, CancellationToken.None);

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        Assert.Equal(100m, saved.Subtotal);
        Assert.Equal(9.09m, saved.TaxAmount);
        Assert.Equal(5m, saved.ServiceChargeAmount);
        Assert.Equal(105m, saved.TotalAmount);
    }

    // ==================== A2 + A3 — khuyến mãi 2 chiều ====================

    /// <summary>Tạo khuyến mãi đang hiệu lực (client gửi giờ VN ⇒ server lưu UTC).</summary>
    private async Task<Promotion> CreateActivePromotionAsync(
        string discountType = "Percent",
        decimal discountValue = 10m)
    {
        var nowVn = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        return await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "Khuyến mãi test",
            DiscountType = discountType,
            DiscountValue = discountValue,
            StartAt = nowVn.AddDays(-1),
            EndAt = nowVn.AddDays(1),
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m
        }, CancellationToken.None);
    }

    private async Task<Order> CreateOrderAsync(decimal sizePrice = 50m, int quantity = 2)
    {
        var s = SeedOrderScenario(sizePrice: sizePrice, stock: 100m);
        return await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = quantity }]
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyPromotion_StoresPromotionIdAndSnapshot()
    {
        await SettingsService.UpdateAsync(new RestaurantSettingsRequest
        {
            TaxRate = 10m,
            TaxIncluded = true,
            ServiceChargeRate = 5m
        }, CancellationToken.None);

        var order = await CreateOrderAsync();
        var promotion = await CreateActivePromotionAsync();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        // 10% của 100 = 10 ⇒ base 90: VAT = 8.181,82; phí DV = 4,50; tổng 94,50.
        Assert.Equal(10m, saved.DiscountAmount);
        Assert.Equal(90m, saved.Subtotal - saved.DiscountAmount);
        Assert.Equal(8.18m, saved.TaxAmount);
        Assert.Equal(4.5m, saved.ServiceChargeAmount);
        Assert.Equal(94.5m, saved.TotalAmount);

        // A3: FK + snapshot khuyến mãi.
        Assert.Equal(promotion.Id, saved.PromotionId);
        Assert.Equal("Percent", saved.PromotionDiscountType);
        Assert.Equal(10m, saved.PromotionDiscountValue);
    }

    [Fact]
    public async Task RemovePromotion_ClearsDiscountAndRecomputesTotal()
    {
        await SettingsService.UpdateAsync(new RestaurantSettingsRequest
        {
            TaxRate = 10m,
            TaxIncluded = true,
            ServiceChargeRate = 5m
        }, CancellationToken.None);

        var order = await CreateOrderAsync();
        var promotion = await CreateActivePromotionAsync();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));
        Assert.True(await Orders.RemovePromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        Assert.Equal(0m, saved.DiscountAmount);
        Assert.Null(saved.PromotionId);
        Assert.Null(saved.PromotionDiscountType);
        Assert.Null(saved.PromotionDiscountValue);

        // Quay lại công thức gốc: 100.000 + 5.000 phí DV.
        Assert.Equal(9.09m, saved.TaxAmount);
        Assert.Equal(5m, saved.ServiceChargeAmount);
        Assert.Equal(105m, saved.TotalAmount);
    }

    [Fact]
    public async Task RemovePromotion_WrongPromotionId_IsRejected()
    {
        var order = await CreateOrderAsync();
        var applied = await CreateActivePromotionAsync();
        var other = await CreateActivePromotionAsync(discountType: "Fixed", discountValue: 5m);

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, applied.Id, CancellationToken.None));

        // Gỡ nhầm khuyến mãi khác ⇒ không được đụng vào đơn.
        Assert.False(await Orders.RemovePromotionAsync(order.OrderId, other.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(applied.Id, saved.PromotionId);
        Assert.True(saved.DiscountAmount > 0);
    }

    // ==================== P0-05 — múi giờ khuyến mãi ====================

    [Fact]
    public async Task Promotion_StoresUtc_AndReturnsVietnamTime()
    {
        var startVn = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var endVn = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Unspecified);

        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "Khai trương",
            DiscountType = "Percent",
            DiscountValue = 15,
            StartAt = startVn,
            EndAt = endVn
        }, CancellationToken.None);

        // DB lưu UTC (giờ VN − 7).
        Assert.Equal(new DateTime(2026, 12, 1, 1, 0, 0, DateTimeKind.Utc), promotion.StartAt);
        Assert.Equal(new DateTime(2026, 12, 2, 1, 0, 0, DateTimeKind.Utc), promotion.EndAt);

        // Response trả cả giờ VN (đúng quy ước đầu vào) lẫn UTC (đúng thứ lưu trong DB).
        var response = PromotionResponse.From(promotion);
        Assert.Equal(startVn, response.StartAt);
        Assert.Equal(endVn, response.EndAt);
        Assert.Equal(promotion.StartAt, response.StartAtUtc);
        Assert.Equal(promotion.EndAt, response.EndAtUtc);
    }

    // ==================== A5 — idempotency lệnh tiền/số lượng ====================

    [Fact]
    public async Task AddItems_SameIdempotencyKey_AddsItemsOnlyOnce()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 }]
        }, CancellationToken.None);

        var request = new AddOrderItemsRequest
        {
            ChangedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        };

        var first = await Orders.AddItemsIdempotentAsync(order.OrderId, request, "add-key", CancellationToken.None);
        var second = await Orders.AddItemsIdempotentAsync(order.OrderId, request, "add-key", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.OrderId, second!.OrderId);

        using var check = NewContext();

        // Chỉ 1 round bếp mới (2 ticket: 1 của lúc tạo đơn + 1 của lần gọi thêm).
        Assert.Equal(2, check.KitchenOrders.Count(x => x.OrderId == order.OrderId));

        // Chỉ 1 lần trừ kho cho lần gọi thêm: 100 − (2×2) − (1×2) = 94.
        Assert.Equal(94m, check.Inventories
            .Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task Payment_SameIdempotencyKey_ChargesOnlyOnce()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        // Bếp đã xong ⇒ đơn được thu tiền.
        foreach (var item in Db.OrderItems.Where(x => x.OrderId == order.OrderId))
        {
            item.Status = KitchenStatuses.Completed;
        }
        Db.Orders.Single(x => x.OrderId == order.OrderId).Status = OrderStatuses.Ready;
        await Db.SaveChangesAsync();

        var request = new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        };

        var first = await Payments.CreateIdempotentAsync(request, "pay-key", CancellationToken.None);
        var second = await Payments.CreateIdempotentAsync(request, "pay-key", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.PaymentId, second!.PaymentId);

        using var check = NewContext();
        Assert.Single(check.Payments.Where(x => x.OrderId == order.OrderId));
        Assert.Equal(OrderStatuses.Paid, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    // ==================== P0-12 — mã lý do ở lịch sử đơn ====================

    [Fact]
    public async Task CancelOrder_StoresReasonCodeInOrderHistory()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var item = Db.OrderItems.Single(x => x.OrderId == order.OrderId);

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            item.Id,
            new CancelOrderItemRequest
            {
                Reason = "Hết hàng bò",
                ReasonCode = "OutOfStock",
                ChangedBy = s.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var history = check.OrderStatusHistories.Where(x => x.OrderId == order.OrderId).ToList();

        // Dòng huỷ đơn phải mang mã lý do để tra lại được "vì khách đổi hay vì hết hàng".
        var cancelled = history.Single(x => x.NewStatus == OrderStatuses.Cancelled);
        Assert.Equal("OutOfStock", cancelled.ReasonCode);

        // API lịch sử đơn cũng trả mã lý do.
        var viaApi = await Orders.GetHistoryAsync(order.OrderId, CancellationToken.None);
        Assert.NotNull(viaApi);
        Assert.Equal("OutOfStock", viaApi!.Single(x => x.NewStatus == OrderStatuses.Cancelled).ReasonCode);
    }

    // ==================== C1 — DisplayOrder là số ====================

    private static IReadOnlyList<object> AsList(object value)
    {
        var items = new List<object>();
        foreach (var item in (IEnumerable)value)
        {
            items.Add(item!);
        }
        return items;
    }

    private static object Prop(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target)!;

    [Fact]
    public async Task Category_DisplayOrder_IsInt_AndSortsMenu()
    {
        var late = SeedCategory(name: "Zeta");
        var early = SeedCategory(name: "Alpha");
        var middle = SeedCategory(name: "Mid");

        late.DisplayOrder = 3;
        early.DisplayOrder = 1;
        middle.DisplayOrder = 2;
        await Db.SaveChangesAsync();

        var menu = await Menu.GetMenuAsync(CancellationToken.None);

        // Sắp theo DisplayOrder, sau đó theo tên — 1, 2, 3.
        Assert.Equal(
            new[] { "Alpha", "Mid", "Zeta" },
            AsList(menu).Select(x => (string)Prop(x, "CategoryName")).ToArray());
    }
}
