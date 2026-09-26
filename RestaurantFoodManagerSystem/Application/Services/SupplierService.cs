public sealed class SupplierService
{
    private readonly ManagementService _management;
    public SupplierService(ManagementService management) => _management = management;
    public Task<List<Supplier>> GetAllAsync(CancellationToken ct) => _management.Suppliers(ct);
    public Task<Supplier> CreateAsync(SupplierRequest request, CancellationToken ct) => _management.CreateSupplier(request, ct);
    public Task<bool> UpdateAsync(int id, SupplierRequest request, CancellationToken ct) => _management.UpdateSupplier(id, request, ct);
    public Task<bool> DeleteAsync(int id, CancellationToken ct) => _management.DeleteSupplier(id, ct);
}
