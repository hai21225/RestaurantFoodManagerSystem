public sealed class PurchaseOrderService
{
    private readonly ManagementService _management;
    public PurchaseOrderService(ManagementService management) => _management = management;
    public Task<List<PurchaseOrder>> GetAllAsync(CancellationToken ct) => _management.Purchases(ct);
    public Task<PurchaseOrder> CreateAsync(PurchaseOrderRequest request, CancellationToken ct) => _management.CreatePurchase(request, ct);
    public Task<bool> ReceiveAsync(int id, ReceivePurchaseRequest request, CancellationToken ct) => _management.ReceivePurchase(id, request, ct);
}
