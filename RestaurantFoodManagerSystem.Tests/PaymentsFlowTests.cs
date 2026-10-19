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
    public async Task Create_UsesEmptyString_WhenTransactionCodeIsMissing()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Card",
            TransactionCode = null,
            CreatedBy = s.Staff.Id
        }, CancellationToken.None);

        Assert.Equal(string.Empty, payment!.TransactionCode);
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
}
