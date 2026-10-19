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
        if (await _db.Payments.AnyAsync(x => x.OrderId == request.OrderId && x.Status == "Paid", ct)) throw new InvalidOperationException("Order is already paid");
        var createdBy = _currentUser.ResolveActorId(request.CreatedBy, _logger, nameof(request.CreatedBy));
        var payment = new Payment { OrderId = order.OrderId, Amount = order.TotalAmount, PaymentMethod = request.PaymentMethod.Trim(), TransactionCode = request.TransactionCode ?? string.Empty, Status = "Paid", PaidAt = DateTime.UtcNow, CreatedBy = createdBy };
        _db.Payments.Add(payment); order.Status = "Paid"; order.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); await _tables.RefreshStatusForTableAsync(order.TableId); return payment;
    }
    public Task<List<Payment>> GetByOrderAsync(int orderId, CancellationToken ct) => _db.Payments.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);
}
