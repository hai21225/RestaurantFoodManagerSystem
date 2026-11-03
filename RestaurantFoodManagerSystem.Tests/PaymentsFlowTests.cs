using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins current PaymentService behaviour (server-side amount, duplicate payment guard, table status refresh).</summary>
public sealed class PaymentsFlowTests : TestBase
{
    [Fact]
    public async Task Create_UsesServerSideOrderTotal_AndMarksOrderPaid()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 250m);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "  Cash  ",
            TransactionCode = "TXN-1",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal(250m, payment!.Amount);
        Assert.Equal("Cash", payment.PaymentMethod);
        Assert.Equal("Paid", payment.Status);
        Assert.Equal("TXN-1", payment.TransactionCode);
        Assert.Equal(s.Staff.Id, payment.CreatedBy);

        Assert.Equal("Paid", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
        // No unpaid order left on the table -> back to Available
        Assert.Equal("Available", Db.RestaurantTables.Single(x => x.TableId == s.Table.TableId).Status);
    }

    [Fact]
    public async Task Create_GeneratesTransactionCode_WhenMissing()
    {
        // C-19: mã giao dịch phải luôn có và luôn duy nhất — client không gửi thì
        // server sinh mã nội bộ (trước đây để chuỗi rỗng ⇒ nhiều dòng trùng nhau).
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Card",
            TransactionCode = null,
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.NotNull(payment);
        Assert.False(string.IsNullOrWhiteSpace(payment!.TransactionCode));
        Assert.StartsWith("PAY", payment.TransactionCode);
    }

    [Fact]
    public async Task Create_ReturnsNull_WhenOrderMissing()
    {
        Assert.Null(await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = 999999,
            PaymentMethod = "Cash",
            CreatedBy = 1
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Create_Throws_WhenOrderAlreadyPaid()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);
        var request = new CreatePaymentRequest { OrderId = order.OrderId, PaymentMethod = "Cash", CreatedBy = s.Staff.Id };

        await Payments.CreateAsync(request, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(request, CancellationToken.None));
        Assert.Equal("Order is already paid", ex.Message);
    }

    // ==================== C-19: mã giao dịch ====================

    [Fact]
    public async Task Create_KeepsClientTransactionCode()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Bank",
            TransactionCode = "  NHANHANG-001  ",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.Equal("NHANHANG-001", payment!.TransactionCode);
    }

    [Fact]
    public async Task Create_Rejects_DuplicateTransactionCode()
    {
        // C-19: trùng mã giao dịch = thu 2 lần cùng một mã ngân hàng ⇒ phải báo lỗi.
        var s = SeedOrderScenario();
        var first = SeedOrder(s.Table.TableId, s.Staff.Id);

        await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = first.OrderId,
            PaymentMethod = "Bank",
            TransactionCode = "NHANHANG-001",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        var second = SeedOrder(s.Table.TableId, s.Staff.Id);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(
            new CreatePaymentRequest
            {
                OrderId = second.OrderId,
                PaymentMethod = "Bank",
                TransactionCode = "NHANHANG-001",
                CreatedBy = s.Staff.Id
            },
            CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Single(Db.Payments);
    }

    [Fact]
    public async Task Create_GeneratedTransactionCodes_AreDifferentPerPayment()
    {
        var s = SeedOrderScenario();
        var first = SeedOrder(s.Table.TableId, s.Staff.Id);
        var second = SeedOrder(s.Table.TableId, s.Staff.Id);

        var a = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = first.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        var b = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = second.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.NotEqual(a!.TransactionCode, b!.TransactionCode);
        Assert.All(Db.Payments, x => Assert.False(string.IsNullOrWhiteSpace(x.TransactionCode)));
    }

    [Fact]
    public async Task Create_Throws_WhenOrderStatusPaidButPaymentRowMissing()
    {
        // Dữ liệu lệch: order đã Paid nhưng chưa có payment row (import tay/migration lỗi).
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(
            new CreatePaymentRequest { OrderId = order.OrderId, PaymentMethod = "Cash", CreatedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Order is already paid", ex.Message);
        Assert.Empty(Db.Payments.Where(x => x.OrderId == order.OrderId));
    }

    [Fact]
    public async Task Create_Throws_WhenOrderIsCancelled()
    {
        // Nhóm B: đơn đã huỷ không được thanh toán.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Cancelled");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(
            new CreatePaymentRequest { OrderId = order.OrderId, PaymentMethod = "Cash", CreatedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Cannot pay a cancelled order", ex.Message);
    }

    [Fact]
    public async Task GetByOrder_ReturnsOnlyPaymentsOfThatOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);
        var otherTable = SeedTable();
        var otherOrder = SeedOrder(otherTable.TableId, s.Staff.Id);
        Db.Payments.Add(new Payment
        {
            OrderId = order.OrderId,
            Amount = 10m,
            PaymentMethod = "Cash",
            Status = "Paid",
            TransactionCode = "A",
            PaidAt = DateTime.UtcNow,
            CreatedBy = s.Staff.Id
        });
        Db.Payments.Add(new Payment
        {
            OrderId = otherOrder.OrderId,
            Amount = 20m,
            PaymentMethod = "Cash",
            Status = "Paid",
            TransactionCode = "B",
            PaidAt = DateTime.UtcNow,
            CreatedBy = s.Staff.Id
        });
        Db.SaveChanges();

        var payments = await Payments.GetByOrderAsync(order.OrderId, CancellationToken.None);

        Assert.Single(payments);
        Assert.Equal(10m, payments[0].Amount);
    }

    [Theory]
    [InlineData("cash", "Cash")]
    [InlineData("CARD", "Card")]
    [InlineData(" bank ", "Bank")]
    [InlineData("eWallet", "EWallet")]
    public async Task Create_NormalizesPaymentMethod_ToAllowListValue(string input, string expected)
    {
        // Defect #26: frontend gửi chữ thường, API cũ dùng chữ hoa; lưu tên chuẩn để lọc nhất quán.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = input,
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.Equal(expected, payment!.PaymentMethod);
    }

    [Fact]
    public async Task Create_RejectsUnknownPaymentMethod()
    {
        // Defect #26: không nhận chuỗi tùy ý (trước đây chỉ Trim).
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Payments.CreateAsync(
            new CreatePaymentRequest
            {
                OrderId = order.OrderId,
                PaymentMethod = "Bitcoin",
                CreatedBy = s.Staff.Id
            },
            CancellationToken.None));

        Assert.Contains("Invalid payment method", ex.Message);
        Assert.Empty(Db.Payments);
    }
}
