using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class ReservationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MomoPaymentService _momo;

    public ReservationService(AppDbContext db, ICurrentUser currentUser, MomoPaymentService momo)
    {
        _db = db;
        _currentUser = currentUser;
        _momo = momo;
    }

    private bool IsCustomerOnlyCaller() =>
        _currentUser.Roles.Contains(AuthPolicies.Roles.Customer, StringComparer.OrdinalIgnoreCase)
        && !_currentUser.Roles.Contains(AuthPolicies.Roles.Admin, StringComparer.OrdinalIgnoreCase)
        && !_currentUser.Roles.Contains(AuthPolicies.Roles.Waiter, StringComparer.OrdinalIgnoreCase);

    private void EnsureCustomerOwnership(TableReservation r)
    {
        if (IsCustomerOnlyCaller() && r.CustomerUserId != _currentUser.UserId)
            throw new UnauthorizedAccessException("You do not have access to this reservation");
    }

    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("CustomerName is required");
        if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            throw new ArgumentException("CustomerPhone is required");
        if (request.PartySize < 1)
            throw new ArgumentException("PartySize must be at least 1");
        if (request.DepositAmount < 0)
            throw new ArgumentException("DepositAmount must be >= 0");

        var reservedFrom = VietnamTime.ToUtcFromApi(request.ReservedFrom);
        var reservedTo = VietnamTime.ToUtcFromApi(request.ReservedTo);
        if (reservedTo <= reservedFrom)
            throw new ArgumentException("ReservedTo must be after ReservedFrom");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var table = await _db.RestaurantTables.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TableId == request.TableId && x.IsActive, ct);
        if (table is null)
            throw new KeyNotFoundException("Table not found");
        if (string.Equals(table.Status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Table is under maintenance");
        if (request.PartySize > table.Capacity)
            throw new ArgumentException($"PartySize ({request.PartySize}) exceeds table capacity ({table.Capacity})");

        var hasOverlap = await _db.TableReservations.AnyAsync(
            x => x.TableId == request.TableId
              && (x.Status == TableReservationStatuses.Pending
                  || x.Status == TableReservationStatuses.Confirmed
                  || x.Status == TableReservationStatuses.Seated)
              && x.ReservedFrom < reservedTo
              && x.ReservedTo > reservedFrom,
            ct);
        if (hasOverlap)
            throw new InvalidOperationException("Table already has a reservation in this time slot");

        var now = DateTime.UtcNow;
        if (reservedFrom <= now.AddHours(2))
        {
            var hasOpenOrder = await _db.Orders.AnyAsync(
                o => o.TableId == request.TableId
                  && o.Status != OrderStatuses.Paid
                  && o.Status != OrderStatuses.Cancelled,
                ct);
            if (hasOpenOrder)
                throw new InvalidOperationException("Table currently has an open order");
        }

        var customerUserId = IsCustomerOnlyCaller()
            ? _currentUser.UserId
            : (request.CustomerUserId > 0 ? request.CustomerUserId : null);

        var reservation = new TableReservation
        {
            TableId = request.TableId,
            CustomerUserId = customerUserId,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            PartySize = request.PartySize,
            ReservedFrom = reservedFrom,
            ReservedTo = reservedTo,
            Status = TableReservationStatuses.Pending,
            DepositAmount = request.DepositAmount,
            DepositStatus = request.DepositAmount > 0 ? DepositStatuses.Pending : DepositStatuses.None,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.TableReservations.Add(reservation);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ReservationResponse.From(reservation, table.TableNumber);
    }

    public async Task<List<ReservationResponse>> GetMyAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not int userId)
            throw new UnauthorizedAccessException("Authenticated customer is required");

        return await (
            from r in _db.TableReservations.AsNoTracking()
            join t in _db.RestaurantTables.AsNoTracking() on r.TableId equals t.TableId into tables
            from t in tables.DefaultIfEmpty()
            where r.CustomerUserId == userId
            orderby r.ReservedFrom descending
            select ReservationResponse.From(r, t != null ? t.TableNumber : null))
            .ToListAsync(ct);
    }

    public async Task<List<ReservationResponse>> GetListAsync(ReservationListQuery query, CancellationToken ct)
    {
        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (query.Date.HasValue)
        {
            var resolved = VietnamTime.Resolve("day", query.Date);
            fromUtc = resolved.FromUtc;
            toUtc = resolved.ToUtc;
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();

        return await (
            from r in _db.TableReservations.AsNoTracking()
            join t in _db.RestaurantTables.AsNoTracking() on r.TableId equals t.TableId into tables
            from t in tables.DefaultIfEmpty()
            where (query.TableId == null || r.TableId == query.TableId)
               && (status == null || r.Status.ToLower() == status)
               && (fromUtc == null || (r.ReservedFrom >= fromUtc && r.ReservedFrom < toUtc))
            orderby r.ReservedFrom
            select ReservationResponse.From(r, t != null ? t.TableNumber : null))
            .ToListAsync(ct);
    }

    public async Task<ReservationResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        var r = await _db.TableReservations.AsNoTracking().FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        EnsureCustomerOwnership(r);

        var tableNumber = await _db.RestaurantTables.AsNoTracking()
            .Where(t => t.TableId == r.TableId)
            .Select(t => t.TableNumber)
            .FirstOrDefaultAsync(ct);

        return ReservationResponse.From(r, tableNumber);
    }

    public async Task<ReservationResponse?> CancelAsync(int id, CancelReservationRequest? request, CancellationToken ct)
    {
        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        EnsureCustomerOwnership(r);

        if (r.Status is TableReservationStatuses.Cancelled or TableReservationStatuses.NoShow or TableReservationStatuses.Completed)
            throw new InvalidOperationException($"Reservation is already {r.Status}");

        var now = DateTime.UtcNow;
        r.Status = TableReservationStatuses.Cancelled;
        r.CancelledBy = _currentUser.UserId;
        r.CancelledAt = now;
        r.CancelReason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        if (r.DepositStatus == DepositStatuses.Paid)
            r.DepositStatus = DepositStatuses.Refunded;
        else if (r.DepositStatus == DepositStatuses.Pending)
            r.DepositStatus = DepositStatuses.None;
        r.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ReservationResponse.From(r);
    }

    public async Task<ReservationResponse?> ConfirmAsync(int id, CancellationToken ct)
    {
        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        if (r.Status != TableReservationStatuses.Pending)
            throw new InvalidOperationException($"Only Pending reservations can be confirmed (current: {r.Status})");

        var now = DateTime.UtcNow;
        r.Status = TableReservationStatuses.Confirmed;
        r.ConfirmedBy = _currentUser.UserId;
        r.ConfirmedAt = now;
        r.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ReservationResponse.From(r);
    }

    public async Task<ReservationResponse?> SeatAsync(int id, CancellationToken ct)
    {
        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        if (r.Status is not (TableReservationStatuses.Pending or TableReservationStatuses.Confirmed))
            throw new InvalidOperationException($"Only Pending/Confirmed reservations can be seated (current: {r.Status})");

        var now = DateTime.UtcNow;
        r.Status = TableReservationStatuses.Seated;
        r.SeatedAt = now;
        r.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ReservationResponse.From(r);
    }

    public async Task<ReservationResponse?> NoShowAsync(int id, CancellationToken ct)
    {
        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        if (r.Status is not (TableReservationStatuses.Pending or TableReservationStatuses.Confirmed))
            throw new InvalidOperationException($"Only Pending/Confirmed reservations can be marked NoShow (current: {r.Status})");

        var now = DateTime.UtcNow;
        r.Status = TableReservationStatuses.NoShow;
        if (r.DepositStatus == DepositStatuses.Paid)
            r.DepositStatus = DepositStatuses.Forfeited;
        r.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ReservationResponse.From(r);
    }

    public async Task<ReservationResponse?> ConfirmDepositAsync(int id, ConfirmDepositRequest? request, CancellationToken ct)
    {
        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        if (r.DepositAmount <= 0)
            throw new InvalidOperationException("Reservation does not require a deposit");
        if (r.DepositStatus != DepositStatuses.Pending)
            throw new InvalidOperationException($"Deposit is {r.DepositStatus} and cannot be confirmed");

        var now = DateTime.UtcNow;
        var code = PaymentTransactionCodes.Normalize(request?.TransactionCode) ?? PaymentTransactionCodes.Generate(now);
        r.DepositStatus = DepositStatuses.Paid;
        r.DepositTransactionCode = code;
        r.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ReservationResponse.From(r);
    }

    public async Task<ReservationMomoDepositResponse?> CreateMomoDepositAsync(int id, CancellationToken ct)
    {
        if (!_momo.IsMockMode && !_momo.HasRealCredentials)
            throw new InvalidOperationException("MoMo is not configured.");

        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == id, ct);
        if (r is null) return null;
        EnsureCustomerOwnership(r);

        if (r.Status is not (TableReservationStatuses.Pending or TableReservationStatuses.Confirmed))
            throw new InvalidOperationException($"Reservation is {r.Status}");
        if (r.DepositAmount <= 0)
            throw new InvalidOperationException("Reservation does not require a deposit");
        if (r.DepositStatus != DepositStatuses.Pending)
            throw new InvalidOperationException($"Deposit is {r.DepositStatus}");

        var requestId = Guid.NewGuid().ToString("N");
        var orderRef = $"RES-{r.ReservationId}";
        var payload = _momo.BuildCreatePayload(
            r.DepositAmount,
            orderRef,
            $"Deposit for reservation #{r.ReservationId}",
            requestId,
            orderRef,
            string.Empty);

        if (_momo.IsMockMode)
        {
            return new ReservationMomoDepositResponse
            {
                ReservationId = r.ReservationId,
                RequestId = requestId,
                Amount = r.DepositAmount,
                RedirectUrl = $"/api/reservations/{r.ReservationId}/deposit/momo/mock?requestId={requestId}&success=true",
                IsMock = true
            };
        }

        var result = await _momo.CreateAsync(payload, ct);
        if (!result.Success)
            throw new InvalidOperationException($"MoMo create failed: {result.Error}");

        return new ReservationMomoDepositResponse
        {
            ReservationId = r.ReservationId,
            RequestId = requestId,
            Amount = r.DepositAmount,
            RedirectUrl = result.RedirectUrl,
            QrCodeUrl = result.QrCodeUrl,
            IsMock = false
        };
    }

    public async Task<PaymentService.MomoNotificationOutcome> ApplyMomoDepositNotificationAsync(MomoNotificationRequest request, CancellationToken ct)
    {
        if (!_momo.VerifyNotification(request))
            return PaymentService.MomoNotificationOutcome.Rejected("Invalid signature");

        if (string.IsNullOrWhiteSpace(request.OrderId)
            || !request.OrderId.StartsWith("RES-", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(request.OrderId.AsSpan(4), out var reservationId))
        {
            return PaymentService.MomoNotificationOutcome.Rejected("Invalid reservation reference");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var r = await _db.TableReservations.FirstOrDefaultAsync(x => x.ReservationId == reservationId, ct);
        if (r is null)
            return PaymentService.MomoNotificationOutcome.Rejected("Reservation not found");

        if (r.DepositStatus == DepositStatuses.Paid)
            return PaymentService.MomoNotificationOutcome.AlreadyApplied(r.ReservationId);

        if (!request.IsSuccess)
            return PaymentService.MomoNotificationOutcome.Failed(r.ReservationId, request.Message ?? "Payment failed");

        var notifiedAmount = MomoPaymentService.FormatAmount(
            decimal.TryParse(request.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m);
        if (!string.Equals(notifiedAmount, MomoPaymentService.FormatAmount(r.DepositAmount), StringComparison.Ordinal))
            return PaymentService.MomoNotificationOutcome.Rejected("Amount mismatch");

        r.DepositStatus = DepositStatuses.Paid;
        r.DepositTransactionCode = string.IsNullOrWhiteSpace(request.TransId) ? request.RequestId : request.TransId.Trim();
        r.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PaymentService.MomoNotificationOutcome.Applied(r.ReservationId);
    }
}
