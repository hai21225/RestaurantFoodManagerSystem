using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

public sealed class V2ReservationShiftQrTests : TestBase
{
    private ReservationService Reservations => new(Db, CurrentUser, Momo);
    private CashierShiftService Shifts => new(Db, CurrentUser, NullLogger<CashierShiftService>.Instance);

    [Fact]
    public async Task Reservation_Create_Cancel_NoShow_And_MomoDeposit_Work()
    {
        var customer = SeedUser();
        var table = SeedTable("T-RES-1", capacity: 4);
        CurrentUser.UserId = customer.Id;
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Customer);

        var from = DateTime.UtcNow.AddDays(1);
        var to = from.AddHours(2);

        var created = await Reservations.CreateAsync(new CreateReservationRequest
        {
            TableId = table.TableId,
            CustomerName = "Alice",
            CustomerPhone = "0901234567",
            PartySize = 2,
            ReservedFrom = from,
            ReservedTo = to,
            DepositAmount = 100_000m
        }, CancellationToken.None);

        Assert.Equal(TableReservationStatuses.Pending, created.Status);
        Assert.Equal(DepositStatuses.Pending, created.DepositStatus);
        Assert.Equal(customer.Id, created.CustomerUserId);

        // Overlapping reservation must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reservations.CreateAsync(new CreateReservationRequest
        {
            TableId = table.TableId,
            CustomerName = "Bob",
            CustomerPhone = "0909999999",
            PartySize = 2,
            ReservedFrom = from.AddMinutes(30),
            ReservedTo = to.AddMinutes(30)
        }, CancellationToken.None));

        // MoMo deposit intent + signed IPN
        var intent = await Reservations.CreateMomoDepositAsync(created.ReservationId, CancellationToken.None);
        Assert.NotNull(intent);
        var payload = Momo.BuildCreatePayload(
            100_000m,
            $"RES-{created.ReservationId}",
            $"Deposit for reservation #{created.ReservationId}",
            intent!.RequestId,
            $"RES-{created.ReservationId}",
            string.Empty);
        var ipn = Momo.BuildMockNotification(payload, success: true, "OK");
        var outcome = await Reservations.ApplyMomoDepositNotificationAsync(ipn, CancellationToken.None);
        Assert.True(outcome.Accepted);
        Assert.True(outcome.WasApplied);

        var afterPay = await Reservations.GetByIdAsync(created.ReservationId, CancellationToken.None);
        Assert.Equal(DepositStatuses.Paid, afterPay!.DepositStatus);

        // Cancel refunds paid deposit
        var cancelled = await Reservations.CancelAsync(created.ReservationId, new CancelReservationRequest { Reason = "Change of plans" }, CancellationToken.None);
        Assert.Equal(TableReservationStatuses.Cancelled, cancelled!.Status);
        Assert.Equal(DepositStatuses.Refunded, cancelled.DepositStatus);
    }

    [Fact]
    public async Task CustomerOrder_RequiresValidQrToken_And_EnforcesOwnership()
    {
        var s = SeedOrderScenario();
        var customer = SeedUser();
        var otherCustomer = SeedUser();
        s.Table.QrToken = "QR-SECRET-123";
        Db.SaveChanges();

        CurrentUser.UserId = customer.Id;
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Customer);

        // Missing or wrong QR token -> 403 UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            TableQrToken = "WRONG-QR",
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None));

        // Valid QR token succeeds
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            TableQrToken = "QR-SECRET-123",
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        Assert.Equal(customer.Id, order.CustomerUserId);

        // Other customer cannot read order
        CurrentUser.UserId = otherCustomer.Id;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Orders.GetAsync(order.OrderId, CancellationToken.None));
    }

    [Fact]
    public async Task CashierShift_And_ReservationDepositDeduction_Work()
    {
        var s = SeedOrderScenario();
        var cashier = SeedUser();
        CurrentUser.UserId = cashier.Id;
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Cashier);

        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 300m);
        order.Status = OrderStatuses.Ready;
        Db.SaveChanges();

        // Cashier without open shift cannot record payment
        await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash"
        }, CancellationToken.None));

        // Open shift
        var shift = await Shifts.OpenAsync(new OpenCashierShiftRequest { OpeningCash = 500m }, CancellationToken.None);

        // Link a paid-deposit seated reservation
        var res = new TableReservation
        {
            TableId = s.Table.TableId,
            CustomerName = "Guest",
            CustomerPhone = "0900000000",
            PartySize = 2,
            ReservedFrom = DateTime.UtcNow.AddMinutes(-15),
            ReservedTo = DateTime.UtcNow.AddHours(1),
            Status = TableReservationStatuses.Seated,
            DepositAmount = 100m,
            DepositStatus = DepositStatuses.Paid,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.TableReservations.Add(res);
        Db.SaveChanges();

        order.ReservationId = res.ReservationId;
        Db.SaveChanges();

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash"
        }, CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal(200m, payment!.Amount); // 300 - 100 deposit
        Assert.Equal(shift.ShiftId, payment.ShiftId);

        var updatedRes = Db.TableReservations.Single(r => r.ReservationId == res.ReservationId);
        Assert.Equal(DepositStatuses.AppliedToOrder, updatedRes.DepositStatus);
        Assert.Equal(TableReservationStatuses.Completed, updatedRes.Status);

        var closed = await Shifts.CloseAsync(shift.ShiftId, new CloseCashierShiftRequest { ActualCash = 710m }, CancellationToken.None);
        Assert.NotNull(closed);
        Assert.Equal(700m, closed!.ExpectedCash); // 500 + 200
        Assert.Equal(10m, closed.Variance);
    }
}
