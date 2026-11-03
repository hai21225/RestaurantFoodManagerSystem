using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public static class RestaurantTableStatuses { public const string Available="Available"; public const string Occupied="Occupied"; public const string Maintenance="Maintenance"; }

public class RestaurantTableService
{
    private readonly AppDbContext _db;
    public RestaurantTableService(AppDbContext db) { _db=db; }

    public async Task<RestaurantTableDto?> GetRestaurantTableByIdAsync(int id, CancellationToken ct = default)
    {
        var x = await _db.RestaurantTables
            .Include(t => t.Area)
            .FirstOrDefaultAsync(t => t.TableId == id, ct);
        if (x is null) return null;
        await SaveRefreshedStatusAsync(x, ct);
        return Map(x);
    }

    public async Task<List<RestaurantTableDto>> GetAllRestaurantTablesAsync(CancellationToken ct = default)
    {
        // Include khu vực trong cùng truy vấn bàn; vẫn chỉ thêm MỘT truy vấn cho occupied.
        var xs = await _db.RestaurantTables
            .AsNoTracking()
            .Include(x => x.Area)
            .ToListAsync(ct);

        // Defect #11: một truy vấn nhóm duy nhất thay vì `Orders.AnyAsync` cho từng bàn (N+1).
        // Vẫn giữ nguyên hành vi cũ: danh sách chỉ TÍNH trạng thái để trả về, không ghi DB.
        var occupied = await LoadOccupiedTableIdsAsync(xs.Select(x => x.TableId).ToList(), ct);
        foreach (var x in xs)
        {
            x.Status = ResolveStatus(x, occupied.Contains(x.TableId));
        }

        return xs.Select(Map).ToList();
    }

    // Nhóm B6: bàn đã soft-delete không còn chiếm số bàn (unique index có HasFilter IsActive).
    public async Task<RestaurantTableDto> AddRestaurantTableAsync(RestaurantTableDto r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.TableNumber) || r.Capacity <= 0)
            throw new ArgumentException("Table number and positive capacity are required");

        var number = r.TableNumber.Trim();
        if (await HasActiveTableNumberAsync(number, null, ct))
            throw new InvalidOperationException("Table number already exists");

        if (r.AreaId.HasValue)
        {
            await EnsureAreaExistsAsync(r.AreaId.Value, requireActive: true, ct);
        }

        var x = new RestaurantTable
        {
            TableNumber = number,
            Capacity = r.Capacity,
            AreaId = r.AreaId,
            IsActive = true,
            Status = RestaurantTableStatuses.Available
        };
        await _db.RestaurantTables.AddAsync(x, ct);
        await _db.SaveChangesAsync(ct);

        var dto = Map(x);
        if (x.AreaId.HasValue)
        {
            dto.AreaName = await _db.TableAreas.AsNoTracking()
                .Where(a => a.AreaId == x.AreaId.Value)
                .Select(a => a.AreaName)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        }

        return dto;
    }

    public async Task<bool> UpdateRestaurantTableAsync(int id, RestaurantTableDto r, CancellationToken ct = default)
    {
        var x = await _db.RestaurantTables.FindAsync([id], ct);
        if (x is null) return false;

        if (!string.IsNullOrWhiteSpace(r.TableNumber))
        {
            var number = r.TableNumber.Trim();

            // Batch A2 (15.7): kiểm trùng trước khi ghi để trả lỗi nghiệp vụ rõ ràng thay vì
            // để unique index ném DbUpdateException kèm message SQL ra controller.
            if (!string.Equals(x.TableNumber, number, StringComparison.Ordinal) &&
                await HasActiveTableNumberAsync(number, id, ct))
            {
                throw new InvalidOperationException("Table number already exists");
            }

            x.TableNumber = number;
        }

        if (r.Capacity > 0) x.Capacity = r.Capacity;

        if (r.AreaId.HasValue)
        {
            await EnsureAreaExistsAsync(r.AreaId.Value, requireActive: false, ct);
        }
        // null = bỏ phân khu; luôn gán để client có thể xoá AreaId cũ.
        x.AreaId = r.AreaId;

        await RefreshStatusAsync(x, ct);
        return await _db.SaveChangesAsync(ct) > 0;
    }

    public async Task<bool> DeleteRestaurantTableAsync(int id, CancellationToken ct = default)
    {
        // Serializable để kiểm tra "bàn có đơn mở" và soft-delete là một khối, tránh
        // một đơn vừa được tạo chen giữa lúc kiểm tra và lúc xoá.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var x = await _db.RestaurantTables.FirstOrDefaultAsync(t => t.TableId == id, ct);
        if (x is null) return false;

        if (await HasOpenOrderAsync(id, ct))
            throw new InvalidOperationException("Table has an open order and cannot be deleted");

        x.IsActive = false;
        x.Status = RestaurantTableStatuses.Maintenance;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SetMaintenanceAsync(int id, bool enabled, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var x = await _db.RestaurantTables.FirstOrDefaultAsync(t => t.TableId == id, ct);
        if (x is null) return false;

        // Ngừng phục vụ bàn đang có đơn mở sẽ làm đơn mất bàn hợp lệ.
        if (enabled && await HasOpenOrderAsync(id, ct))
            throw new InvalidOperationException("Table has an open order and cannot be put in maintenance");

        if (enabled)
        {
            // Bảo trì là trạng thái vận hành, KHÔNG phải soft-delete: bàn vẫn active
            // và vẫn hiển thị trên frontend với nhãn "Bảo trì".
            x.Status = RestaurantTableStatuses.Maintenance;
        }
        else
        {
            // Đây cũng là thao tác khôi phục bàn đã soft-delete.
            x.IsActive = true;
            x.Status = RestaurantTableStatuses.Available;
            await RefreshStatusAsync(x, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Tính lại trạng thái bàn. Được gọi như hệ quả phụ SAU khi transaction chính đã commit
    /// (payment/order), nên xung đột <c>RowVersion</c> ở đây không được làm hỏng kết quả nghiệp vụ:
    /// thử lại trên dữ liệu mới thay vì ném 409.
    /// </summary>
    public async Task RefreshStatusForTableAsync(int id, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var x = await _db.RestaurantTables.FindAsync([id], ct);
            if (x is null) return;
            await RefreshStatusAsync(x, ct);

            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 2)
            {
                // Bỏ bản ghi cũ để lần sau đọc lại trạng thái mới nhất.
                _db.Entry(x).State = EntityState.Detached;
            }
        }
    }

    private async Task SaveRefreshedStatusAsync(RestaurantTable x, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await RefreshStatusAsync(x, ct);
            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 2)
            {
                await _db.Entry(x).ReloadAsync(ct);
            }
        }
    }

    private async Task RefreshStatusAsync(RestaurantTable x, CancellationToken ct)
    {
        // Bàn đã soft-delete hoặc đang bảo trì chủ động: giữ nguyên Maintenance.
        // Khi kết thúc bảo trì, SetMaintenanceAsync đặt Status=Available trước khi gọi hàm này.
        if (!x.IsActive || IsMaintenance(x.Status))
        {
            x.Status = RestaurantTableStatuses.Maintenance;
            return;
        }

        var occupied = (await LoadOccupiedTableIdsAsync([x.TableId], ct)).Contains(x.TableId);
        x.Status = occupied ? RestaurantTableStatuses.Occupied : RestaurantTableStatuses.Available;
    }

    private Task<bool> HasActiveTableNumberAsync(
        string tableNumber,
        int? excludeTableId,
        CancellationToken ct) =>
        _db.RestaurantTables.AnyAsync(
            x => x.TableNumber == tableNumber
                 && x.IsActive
                 && (!excludeTableId.HasValue || x.TableId != excludeTableId.Value),
            ct);

    /// <summary>
    /// Bàn chỉ được trả về trạng thái trống sau khi đơn đã <c>Paid</c> hoặc <c>Cancelled</c>.
    /// <c>Completed</c> chỉ là bếp/khách đã xong món, chưa thu tiền nên bàn vẫn đang sử dụng.
    /// </summary>
    private Task<bool> HasOpenOrderAsync(int tableId, CancellationToken ct) =>
        _db.Orders.AnyAsync(
            o => o.TableId == tableId
                 && o.Status != OrderStatuses.Paid
                 && o.Status != OrderStatuses.Cancelled,
            ct);

    /// <summary>
    /// Trả về các bàn đang có đơn mở trong MỘT truy vấn. Dùng chung cho danh sách (tránh N+1)
    /// và cho từng bàn. Đơn <c>Completed</c> vẫn tính là mở cho tới khi <c>Paid</c>.
    /// </summary>
    private async Task<HashSet<int>> LoadOccupiedTableIdsAsync(
        IReadOnlyCollection<int> tableIds,
        CancellationToken ct)
    {
        if (tableIds.Count == 0)
        {
            return [];
        }

        var occupied = await _db.Orders.AsNoTracking()
            .Where(o => tableIds.Contains(o.TableId)
                        && o.Status != OrderStatuses.Paid
                        && o.Status != OrderStatuses.Cancelled)
            .Select(o => o.TableId)
            .Distinct()
            .ToListAsync(ct);

        return occupied.ToHashSet();
    }

    private async Task EnsureAreaExistsAsync(
        int areaId,
        bool requireActive,
        CancellationToken ct)
    {
        var query = _db.TableAreas.AsNoTracking().Where(x => x.AreaId == areaId);
        if (requireActive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!await query.AnyAsync(ct))
        {
            throw new ArgumentException(requireActive
                ? "Table area not found or inactive"
                : "Table area not found");
        }
    }

    private static string ResolveStatus(RestaurantTable x, bool occupied) =>
        !x.IsActive || IsMaintenance(x.Status)
            ? RestaurantTableStatuses.Maintenance
            : occupied ? RestaurantTableStatuses.Occupied : RestaurantTableStatuses.Available;

    private static bool IsMaintenance(string? status) =>
        string.Equals(status, RestaurantTableStatuses.Maintenance, StringComparison.OrdinalIgnoreCase);

    private static RestaurantTableDto Map(RestaurantTable x) => new()
    {
        TableId = x.TableId,
        TableNumber = x.TableNumber,
        Capacity = x.Capacity,
        Status = x.Status,
        AreaId = x.AreaId,
        AreaName = x.Area?.AreaName ?? string.Empty,
        IsActive = x.IsActive
    };
}
