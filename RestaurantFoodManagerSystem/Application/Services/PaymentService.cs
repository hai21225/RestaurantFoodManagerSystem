using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

public sealed class PaymentService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    public PaymentService(AppDbContext db, RestaurantTableService tables) { _db = db; _tables = tables; }
    public async Task<Payment?> CreateAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == request.OrderId, ct);
        if (order is null) return null;
        if (await _db.Payments.AnyAsync(x => x.OrderId == request.OrderId && x.Status == "Paid", ct)) throw new InvalidOperationException("Order is already paid");
        var payment = new Payment { OrderId = order.OrderId, Amount = order.TotalAmount, PaymentMethod = request.PaymentMethod.Trim(), TransactionCode = request.TransactionCode ?? string.Empty, Status = "Paid", PaidAt = DateTime.UtcNow, CreatedBy = request.CreatedBy };
        _db.Payments.Add(payment); order.Status = "Paid"; order.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); await _tables.RefreshStatusForTableAsync(order.TableId); return payment;
    }
    public Task<List<Payment>> GetByOrderAsync(int orderId, CancellationToken ct) => _db.Payments.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);
}
