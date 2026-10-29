using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class SupplierService
{
    private readonly AppDbContext _db;

    public SupplierService(AppDbContext db) => _db = db;

    public Task<List<Supplier>> GetAllAsync(CancellationToken ct) =>
        _db.Suppliers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public Task<Supplier?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.SupplierId == id, ct);

    public async Task<Supplier> CreateAsync(SupplierRequest r, CancellationToken ct)
    {
        var x = new Supplier
        {
            Name = r.Name.Trim(),
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

        x.Name = r.Name.Trim();
        x.Phone = r.Phone.Trim();
        x.Email = r.Email.Trim();
        x.Address = r.Address.Trim();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var x = await _db.Suppliers.FindAsync([id], ct);
        if (x is null) return false;

        x.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

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
