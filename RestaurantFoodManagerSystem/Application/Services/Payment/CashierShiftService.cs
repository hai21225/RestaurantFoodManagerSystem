using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class CashierShiftService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CashierShiftService> _logger;

    public CashierShiftService(AppDbContext db, ICurrentUser currentUser, ILogger<CashierShiftService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<CashierShiftResponse> OpenAsync(OpenCashierShiftRequest request, CancellationToken ct)
    {
        if (request.OpeningCash < 0)
            throw new ArgumentException("OpeningCash must be >= 0");

        var cashierUserId = await _currentUser.ResolveExistingActorAsync(
            _db, request.CashierUserId, _logger, nameof(request.CashierUserId), ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var hasOpenShift = await _db.CashierShifts.AnyAsync(
            x => x.CashierUserId == cashierUserId && x.Status == CashierShiftStatuses.Open, ct);
        if (hasOpenShift)
            throw new InvalidOperationException("Cashier already has an open shift");

        var shift = new CashierShift
        {
            CashierUserId = cashierUserId,
            OpenedAt = DateTime.UtcNow,
            OpeningCash = request.OpeningCash,
            ExpectedCash = request.OpeningCash,
            Status = CashierShiftStatuses.Open,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        _db.CashierShifts.Add(shift);
        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            if (await _db.CashierShifts.AnyAsync(
                    x => x.CashierUserId == cashierUserId && x.Status == CashierShiftStatuses.Open, ct))
                throw new InvalidOperationException("Cashier already has an open shift");
            throw;
        }

        var name = await _db.Users.AsNoTracking()
            .Where(u => u.Id == cashierUserId)
            .Select(u => u.UserFullName)
            .FirstOrDefaultAsync(ct);

        return CashierShiftResponse.From(shift, name);
    }

    public async Task<CashierShiftResponse?> GetCurrentAsync(int? cashierUserId, CancellationToken ct)
    {
        var targetUserId = _currentUser.UserId ?? cashierUserId ?? 0;
        if (targetUserId <= 0)
            return null;

        var shift = await _db.CashierShifts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CashierUserId == targetUserId && x.Status == CashierShiftStatuses.Open, ct);
        if (shift is null)
            return null;

        await PopulateLiveTotalsAsync(shift, ct);
        var name = await _db.Users.AsNoTracking()
            .Where(u => u.Id == shift.CashierUserId)
            .Select(u => u.UserFullName)
            .FirstOrDefaultAsync(ct);

        return CashierShiftResponse.From(shift, name);
    }

    public async Task<CashierShiftResponse?> CloseAsync(int shiftId, CloseCashierShiftRequest request, CancellationToken ct)
    {
        if (request.ActualCash < 0)
            throw new ArgumentException("ActualCash must be >= 0");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var shift = await _db.CashierShifts.FirstOrDefaultAsync(x => x.ShiftId == shiftId, ct);
        if (shift is null)
            return null;

        if (shift.Status != CashierShiftStatuses.Open)
            throw new InvalidOperationException("Shift is already closed");

        var isAdmin = _currentUser.Roles.Contains(AuthPolicies.Roles.Admin, StringComparer.OrdinalIgnoreCase);
        if (_currentUser.UserId is int uid && !isAdmin && shift.CashierUserId != uid)
            throw new UnauthorizedAccessException("You can only close your own shift");

        await PopulateLiveTotalsAsync(shift, ct);

        shift.ActualCash = request.ActualCash;
        shift.Variance = request.ActualCash - shift.ExpectedCash;
        shift.Status = CashierShiftStatuses.Closed;
        shift.ClosedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Note))
            shift.Note = request.Note.Trim();

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var name = await _db.Users.AsNoTracking()
            .Where(u => u.Id == shift.CashierUserId)
            .Select(u => u.UserFullName)
            .FirstOrDefaultAsync(ct);

        return CashierShiftResponse.From(shift, name);
    }

    public async Task<List<CashierShiftResponse>> GetListAsync(CancellationToken ct)
    {
        return await (
            from s in _db.CashierShifts.AsNoTracking()
            join u in _db.Users.AsNoTracking() on s.CashierUserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            orderby s.OpenedAt descending
            select CashierShiftResponse.From(s, u != null ? u.UserFullName : null))
            .ToListAsync(ct);
    }

    public async Task<CashierShiftResponse?> GetByIdAsync(int shiftId, CancellationToken ct)
    {
        var shift = await _db.CashierShifts.AsNoTracking().FirstOrDefaultAsync(x => x.ShiftId == shiftId, ct);
        if (shift is null)
            return null;

        if (shift.Status == CashierShiftStatuses.Open)
            await PopulateLiveTotalsAsync(shift, ct);

        var name = await _db.Users.AsNoTracking()
            .Where(u => u.Id == shift.CashierUserId)
            .Select(u => u.UserFullName)
            .FirstOrDefaultAsync(ct);

        return CashierShiftResponse.From(shift, name);
    }

    private async Task PopulateLiveTotalsAsync(CashierShift shift, CancellationToken ct)
    {
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.ShiftId == shift.ShiftId
                     && (p.Status == PaymentStatuses.Paid || p.Status == PaymentStatuses.Refunded))
            .Select(p => new { p.PaymentMethod, p.Provider, p.Status, p.Amount })
            .ToListAsync(ct);

        decimal cashPaid = 0m, cashRefunded = 0m, card = 0m, momo = 0m, other = 0m, refundTotal = 0m;

        foreach (var p in payments)
        {
            var isCash = string.Equals(p.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);
            if (p.Status == PaymentStatuses.Refunded)
            {
                refundTotal += p.Amount;
                if (isCash)
                {
                    cashPaid += p.Amount;
                    cashRefunded += p.Amount;
                }
                continue;
            }

            if (isCash)
                cashPaid += p.Amount;
            else if (string.Equals(p.PaymentMethod, "Card", StringComparison.OrdinalIgnoreCase))
                card += p.Amount;
            else if (string.Equals(p.Provider, PaymentProviders.MoMo, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(p.PaymentMethod, PaymentProviders.MoMo, StringComparison.OrdinalIgnoreCase))
                momo += p.Amount;
            else
                other += p.Amount;
        }

        shift.CashTotal = cashPaid;
        shift.CardTotal = card;
        shift.MomoTotal = momo;
        shift.OtherTotal = other;
        shift.RefundTotal = refundTotal;
        shift.ExpectedCash = shift.OpeningCash + cashPaid - cashRefunded;
    }
}
