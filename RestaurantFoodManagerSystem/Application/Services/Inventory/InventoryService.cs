public sealed class InventoryService
{
    private readonly ManagementService _management;
    public InventoryService(ManagementService management) => _management = management;
    public Task<List<Inventory>> GetAllAsync(CancellationToken ct) => _management.Inventory(ct);
    public Task<bool> AdjustAsync(int ingredientId, InventoryAdjustmentRequest request, CancellationToken ct) => _management.AdjustInventory(ingredientId, request, ct);
}
