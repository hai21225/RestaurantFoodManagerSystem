using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class SupplierService
{
    private readonly AppDbContext _db;

    public SupplierService(AppDbContext db) => _db = db;

    public Task<List<Supplier>> GetAllAsync(CancellationToken ct = default) =>
        GetAllAsync(includeInactive: false, ct);

    public Task<List<Supplier>> GetAllAsync(bool includeInactive, CancellationToken ct = default) =>
        _db.Suppliers.AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public Task<Supplier?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.SupplierId == id, ct);

    public async Task<Supplier> CreateAsync(SupplierRequest r, CancellationToken ct)
    {
        var name = r.Name.Trim();
        await EnsureNameAvailableAsync(name, excludeId: null, ct);

        var x = new Supplier
        {
            Name = name,
            Phone = r.Phone.Trim(),
            Email = r.Email.Trim(),
            Address = r.Address.Trim(),
            IsActive = true
        };
        _db.Suppliers.Add(x);
        await _db.SaveChangesAsync(ct);
        return x;
    }

    public async Task<bool> UpdateAsync(int id, SupplierRequest r, CancellationToken ct)
    {
        var x = await _db.Suppliers.FindAsync([id], ct);
        if (x is null) return false;

        var name = r.Name.Trim();
        await EnsureNameAvailableAsync(name, excludeId: id, ct);

        x.Name = name;
        x.Phone = r.Phone.Trim();
        x.Email = r.Email.Trim();
        x.Address = r.Address.Trim();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureNameAvailableAsync(string name, int? excludeId, CancellationToken ct)
    {
        var normalized = name.ToLowerInvariant();
        if (await _db.Suppliers.AnyAsync(
                x => x.IsActive
                  && (excludeId == null || x.SupplierId != excludeId.Value)
                  && x.Name.ToLower() == normalized, ct))
        {
            throw new InvalidOperationException($"Supplier '{name}' already exists");
        }
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct) =>
        SetActiveAsync(id, false, ct);

    /// <summary>
    /// Khôi phục/tắt nhà cung cấp bằng soft-delete. Phiếu nhập cũ vẫn giữ SupplierId
    /// nên báo cáo/lịch sử không bị ảnh hưởng.
    /// </summary>
    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var x = await _db.Suppliers.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
