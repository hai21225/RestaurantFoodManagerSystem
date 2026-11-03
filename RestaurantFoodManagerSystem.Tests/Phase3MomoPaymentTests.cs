using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 3 — luồng thanh toán qua provider (MoMo) + chế độ giả lập + hoàn tiền.
///
/// <para>Ở chế độ <c>Mock</c> (<c>Payments:Provider=Mock</c>) không có lệnh gọi mạng nào:
/// backend tự sinh payload IPN **đã ký chữ ký đúng** rồi đi qua đúng đường verify của
/// IPN thật ⇒ sai chữ ký / sai số tiền / IPN trùng đều bị bắt như ngoài đời.</para>
/// </summary>
public sealed class Phase3MomoPaymentTests : TestBase
{
    private Order SeedPayableOrder(int createdBy, decimal total = 100m) =>
        SeedOrder(SeedTable().TableId, createdBy, status: OrderStatuses.Ready, subtotal: total);

    private async Task<MomoIntentResponse> CreateIntentAsync(int orderId, int createdBy)
    {
        var intent = await Payments.CreateMomoIntentAsync(
            new CreateMomoPaymentRequest { OrderId = orderId, CreatedBy = createdBy },
            CancellationToken.None);

        Assert.NotNull(intent);
        return intent!;
    }

    // ==========================================================
    // Tạo intent
    // ==========================================================

    [Fact]
    public async Task CreateIntent_LeavesOrderUnpaid_AndReturnsMockUrl()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);

        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        Assert.Equal(PaymentStatuses.Pending, intent.Status);
        Assert.Equal(PaymentProviders.MoMo, intent.Provider);
        Assert.True(intent.IsMock);
        Assert.Contains($"/api/payments/momo/mock/{intent.PaymentId}", intent.RedirectUrl);

        using var check = NewContext();
        var payment = check.Payments.Single(x => x.PaymentId == intent.PaymentId);
        Assert.Equal(PaymentStatuses.Pending, payment.Status);
        Assert.False(payment.PaidAt.HasValue);

        // Đơn CHƯA được đánh dấu đã thu.
        Assert.Equal(OrderStatuses.Ready, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task CreateIntent_CalledTwice_ReusesTheSameIntent()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);

        var first = await CreateIntentAsync(order.OrderId, s.Staff.Id);
        var second = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.RedirectUrl, second.RedirectUrl);

        using var check = NewContext();
        Assert.Single(check.Payments.Where(x => x.OrderId == order.OrderId));
    }

    [Fact]
    public async Task CreateIntent_OrderNotPayable_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: OrderStatuses.SentToKitchen);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateMomoIntentAsync(
            new CreateMomoPaymentRequest { OrderId = order.OrderId, CreatedBy = s.Staff.Id },
            CancellationToken.None));
    }

    // ==========================================================
    // Giả lập thanh toán (đi qua đúng đường verify của IPN thật)
    // ==========================================================

    [Fact]
    public async Task SimulatePayment_Success_MarksPaymentAndOrderPaid()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var outcome = await Payments.SimulateMomoPaymentAsync(intent.PaymentId, success: true, CancellationToken.None);

        Assert.True(outcome.Accepted);
        Assert.True(outcome.WasApplied);

        using var check = NewContext();
        var payment = check.Payments.Single(x => x.PaymentId == intent.PaymentId);
        Assert.Equal(PaymentStatuses.Paid, payment.Status);
        Assert.True(payment.PaidAt.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(payment.ProviderTransactionId));

        Assert.Equal(OrderStatuses.Paid, check.Orders.Single(x => x.OrderId == order.OrderId).Status);

        // Lịch sử đơn có dòng "→ Paid" (P0-02).
        Assert.Contains(
            check.OrderStatusHistories.Where(x => x.OrderId == order.OrderId),
            x => x.NewStatus == OrderStatuses.Paid);
    }

    [Fact]
    public async Task SimulatePayment_Failure_LeavesOrderUnpaid()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var outcome = await Payments.SimulateMomoPaymentAsync(intent.PaymentId, success: false, CancellationToken.None);

        Assert.True(outcome.Accepted);
        Assert.False(outcome.WasApplied);

        using var check = NewContext();
        Assert.Equal(
            PaymentStatuses.Failed,
            check.Payments.Single(x => x.PaymentId == intent.PaymentId).Status);
        Assert.Equal(OrderStatuses.Ready, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task SimulatePayment_CalledTwice_IsIdempotent()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var first = await Payments.SimulateMomoPaymentAsync(intent.PaymentId, success: true, CancellationToken.None);
        var second = await Payments.SimulateMomoPaymentAsync(intent.PaymentId, success: true, CancellationToken.None);

        Assert.True(first.WasApplied);
        Assert.False(second.WasApplied);

        using var check = NewContext();
        Assert.Single(check.Payments.Where(x => x.OrderId == order.OrderId && x.Status == PaymentStatuses.Paid));
    }

    // ==========================================================
    // Chữ ký & số tiền — các trường hợp phải bị từ chối
    // ==========================================================

    [Fact]
    public async Task Notification_WithWrongSignature_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var notification = BuildNotification(intent);
        notification.Signature = "0123456789abcdef0123456789abcdef";

        var outcome = await Payments.ApplyMomoNotificationAsync(notification, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Contains("signature", outcome.Message, StringComparison.OrdinalIgnoreCase);

        using var check = NewContext();
        Assert.Equal(OrderStatuses.Ready, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task Notification_WithWrongAmount_IsRejected_EvenWhenSignatureIsValid()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var notification = BuildNotification(intent);
        notification.Amount = "999999";
        // Ký lại cho đúng để chỉ kiểm tra lỗi SỐ TIỀN, không phải lỗi chữ ký.
        notification.Signature = Momo.Sign(Momo.BuildNotificationCanonical(notification));

        var outcome = await Payments.ApplyMomoNotificationAsync(notification, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Contains("amount", outcome.Message, StringComparison.OrdinalIgnoreCase);

        using var check = NewContext();
        Assert.Equal(OrderStatuses.Ready, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task Notification_WithUnknownRequestId_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        var notification = BuildNotification(intent);
        notification.RequestId = "khong-ton-tai";
        notification.Signature = Momo.Sign(Momo.BuildNotificationCanonical(notification));

        var outcome = await Payments.ApplyMomoNotificationAsync(notification, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Contains("Unknown payment", outcome.Message);
    }

    // ==========================================================
    // Hoàn tiền
    // ==========================================================

    [Fact]
    public async Task Refund_MarksPaymentRefunded_AndReopensOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        var refunded = await Payments.RefundAsync(payment!.PaymentId, new RefundPaymentRequest
        {
            Reason = "Khách huỷ món",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.NotNull(refunded);
        Assert.Equal(PaymentStatuses.Refunded, refunded!.Status);
        Assert.True(refunded.RefundedAt.HasValue);

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        // Đơn mở lại để thu lại.
        Assert.Equal(OrderStatuses.Ready, saved.Status);
        Assert.Contains(
            check.OrderStatusHistories.Where(x => x.OrderId == order.OrderId),
            x => x.OldStatus == OrderStatuses.Paid && x.NewStatus == OrderStatuses.Ready);
    }

    [Fact]
    public async Task Refund_Twice_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        await Payments.RefundAsync(payment!.PaymentId, new RefundPaymentRequest
        {
            Reason = "Lý do 1",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.RefundAsync(
            payment.PaymentId,
            new RefundPaymentRequest { Reason = "Lý do 2", CreatedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("cannot be refunded", ex.Message);
    }

    [Fact]
    public async Task Refund_MomoPayment_RefundsThroughTheSameFlow()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);
        var intent = await CreateIntentAsync(order.OrderId, s.Staff.Id);

        await Payments.SimulateMomoPaymentAsync(intent.PaymentId, success: true, CancellationToken.None);

        var refunded = await Payments.RefundAsync(intent.PaymentId, new RefundPaymentRequest
        {
            Reason = "Khách đổi ý",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.Equal(PaymentStatuses.Refunded, refunded!.Status);
    }

    [Fact]
    public async Task Refund_RequiresReason()
    {
        var s = SeedOrderScenario();
        var order = SeedPayableOrder(s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => Payments.RefundAsync(
            payment!.PaymentId,
            new RefundPaymentRequest { Reason = "   ", CreatedBy = s.Staff.Id },
            CancellationToken.None));
    }

    // ==========================================================
    // Helper
    // ==========================================================

    /// <summary>Dựng payload IPN hợp lệ (đã ký đúng) cho giao dịch của intent.</summary>
    private MomoNotificationRequest BuildNotification(MomoIntentResponse intent)
    {
        var payment = Db.Payments.Single(x => x.PaymentId == intent.PaymentId);

        var create = Momo.BuildCreatePayload(
            payment.Amount,
            payment.OrderId.ToString(),
            MomoPaymentService.BuildOrderInfo(payment.OrderId),
            payment.RequestId!,
            payment.OrderId.ToString(),
            payment.PaymentId.ToString());

        return Momo.BuildMockNotification(create, success: true, "Mock payment succeeded");
    }
}
