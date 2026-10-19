using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class PaymentService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext db, RestaurantTableService tables, ICurrentUser currentUser, ILogger<PaymentService> logger)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
    }
    public async Task<Payment?> CreateAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == request.OrderId, ct);
        if (order is null) return null;

        // Defect #26: PaymentMethod không chỉ Trim mà phải thuộc allow-list và được chuẩn hoá
        // (frontend gửi "cash"/"card"/"bank"/"ewallet"; API cũ dùng "Cash"/"Card").
        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);

        // Nhóm B: không thanh toán đơn đã huỷ; không thanh toán trùng.
        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot pay a cancelled order");
        if (await _db.Payments.AnyAsync(x => x.OrderId == request.OrderId && x.Status == OrderStatuses.Paid, ct))
            throw new InvalidOperationException("Order is already paid");

        // Nhóm B4: payment + order-status phải là nguyên tử.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var createdBy = _currentUser.ResolveActorId(request.CreatedBy, _logger, nameof(request.CreatedBy));

        // Nhóm B3: đơn tổng 0đ không phải là một khoản thu; tránh tạo payment "Paid" vô nghĩa.
        if (order.TotalAmount <= 0)
            throw new InvalidOperationException("Order total must be greater than zero");

        var payment = new Payment { OrderId = order.OrderId, Amount = order.TotalAmount, PaymentMethod = paymentMethod, TransactionCode = request.TransactionCode ?? string.Empty, Status = OrderStatuses.Paid, PaidAt = DateTime.UtcNow, CreatedBy = createdBy };
        _db.Payments.Add(payment); order.Status = OrderStatuses.Paid; order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Nhóm B3: unique index lọc (OrderId, Status='Paid') là chốt chặn cuối khi hai request
            // đồng thời cùng vượt qua kiểm tra phía trên. Phân biệt lỗi trùng thanh toán với lỗi
            // khác (FK...) bằng cách truy vấn lại sau khi rollback.
            try { await tx.RollbackAsync(ct); } catch (InvalidOperationException) { /* tx đã đóng */ }

            _db.ChangeTracker.Clear();

            if (await _db.Payments.AsNoTracking().AnyAsync(x => x.OrderId == order.OrderId && x.Status == OrderStatuses.Paid, ct))
                throw new InvalidOperationException("Order is already paid", ex);

            throw;
        }

        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return payment;
    }
    /// <summary>
    /// Allow-list phương thức thanh toán (Defect #26). So khớp không phân biệt hoa/thường và
    /// trả về tên chuẩn để DB không chứa lẫn lộn "cash", "Cash", "CASH".
    /// </summary>
    private static readonly string[] AllowedPaymentMethods = ["Cash", "Card", "Bank", "EWallet", "Transfer"];

    private static string NormalizePaymentMethod(string? raw)
    {
        var method = raw?.Trim() ?? string.Empty;
        if (method.Length == 0)
            throw new ArgumentException("Payment method is required");

        var canonical = AllowedPaymentMethods.FirstOrDefault(
            x => string.Equals(x, method, StringComparison.OrdinalIgnoreCase));

        return canonical
            ?? throw new ArgumentException($"Invalid payment method: {method}");
    }

    public Task<List<Payment>> GetByOrderAsync(int orderId, CancellationToken ct) => _db.Payments.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);

    /// <summary>
    /// Danh sách thanh toán có lọc + phân trang cho <c>GET /api/payments</c> (P1).
    /// Lọc theo <c>PaidAt</c> khi có <c>from</c>/<c>to</c>.
    /// </summary>
    public async Task<PagedResponse<PaymentResponse>> GetListAsync(PaymentListQuery query, CancellationToken ct = default)
    {
        var skip = Paging.ResolveSkip(query.Page, query.PageSize);
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();
        var method = string.IsNullOrWhiteSpace(query.Method) ? null : query.Method.Trim().ToLowerInvariant();
        var orderId = query.OrderId;
        var from = query.From;
        var to = query.To;

        var filtered = _db.Payments.AsNoTracking()
            .Where(x => (status == null || x.Status.ToLower() == status)
                     && (method == null || x.PaymentMethod.ToLower() == method)
                     && (orderId == null || x.OrderId == orderId)
                     && (from == null || x.PaidAt >= from)
                     && (to == null || x.PaidAt <= to))
            .Select(x => new PaymentResponse
            {
                PaymentId = x.PaymentId,
                OrderId = x.OrderId,
                Amount = x.Amount,
                PaymentMethod = x.PaymentMethod,
                Status = x.Status,
                TransactionCode = x.TransactionCode,
                PaidAt = x.PaidAt,
                CreatedBy = x.CreatedBy
            });

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(x => x.PaidAt)
            .ThenByDescending(x => x.PaymentId)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResponse<PaymentResponse>
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total,
            Items = items
        };
    }
}
