using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Quản lý khu vực bàn. Khu vực dùng soft-delete (<c>IsActive=false</c>) để bàn và
/// lịch sử báo cáo vẫn giữ được <c>AreaId</c>; danh sách bàn không bị ẩn theo khu vực.
/// </summary>
public sealed class TableAreaService
{
    private readonly AppDbContext _db;

    public TableAreaService(AppDbContext db) => _db = db;

    public Task<List<TableAreaResponse>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _db.TableAreas.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.AreaName)
            .Select(x => new TableAreaResponse
            {
                AreaId = x.AreaId,
                AreaName = x.AreaName,
                Description = x.Description,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<TableAreaResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var area = await _db.TableAreas.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AreaId == id, ct);

        return area is null ? null : TableAreaResponse.From(area);
    }

    public async Task<TableAreaResponse> CreateAsync(
        TableAreaRequest request,
        CancellationToken ct = default)
    {
        var name = NormalizeName(request.AreaName);

        if (await ActiveNameExistsAsync(name, excludeId: null, ct))
        {
            throw new InvalidOperationException("Table area name already exists");
        }

        var area = new TableArea
        {
            AreaName = name,
            Description = (request.Description ?? string.Empty).Trim(),
            DisplayOrder = request.DisplayOrder,
            IsActive = true
        };

        _db.TableAreas.Add(area);
        await _db.SaveChangesAsync(ct);
        return TableAreaResponse.From(area);
    }

    public async Task<bool> UpdateAsync(
        int id,
        TableAreaRequest request,
        CancellationToken ct = default)
    {
        var area = await _db.TableAreas.FindAsync([id], ct);
        if (area is null)
        {
            return false;
        }

        var name = NormalizeName(request.AreaName);

        if (await ActiveNameExistsAsync(name, id, ct))
        {
            throw new InvalidOperationException("Table area name already exists");
        }

        area.AreaName = name;
        area.Description = (request.Description ?? string.Empty).Trim();
        area.DisplayOrder = request.DisplayOrder;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetActiveAsync(
        int id,
        bool active,
        CancellationToken ct = default)
    {
        var area = await _db.TableAreas.FindAsync([id], ct);
        if (area is null)
        {
            return false;
        }

        if (active && await ActiveNameExistsAsync(area.AreaName, id, ct))
        {
            throw new InvalidOperationException("Table area name already exists");
        }

        area.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var area = await _db.TableAreas.FindAsync([id], ct);
        if (area is null)
        {
            return false;
        }

        area.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private Task<bool> ActiveNameExistsAsync(
        string name,
        int? excludeId,
        CancellationToken ct) =>
        _db.TableAreas.AnyAsync(
            x => x.AreaName == name
                 && x.IsActive
                 && (!excludeId.HasValue || x.AreaId != excludeId.Value),
            ct);

    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException("Table area name is required");
        }

        return value;
    }
}
