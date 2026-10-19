using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public static class RestaurantTableStatuses { public const string Available="Available"; public const string Occupied="Occupied"; public const string Maintenance="Maintenance"; }

public class RestaurantTableService
{
    private readonly AppDbContext _db;
    public RestaurantTableService(AppDbContext db) { _db=db; }
    public async Task<RestaurantTableDto?> GetRestaurantTableByIdAsync(int id) { var x=await _db.RestaurantTables.FindAsync(id); if(x is null)return null; await SaveRefreshedStatusAsync(x); return Map(x); }
    public async Task<List<RestaurantTableDto>> GetAllRestaurantTablesAsync() { var xs=await _db.RestaurantTables.AsNoTracking().ToListAsync(); foreach(var x in xs) await RefreshStatusAsync(x); await _db.SaveChangesAsync(); return xs.Select(Map).ToList(); }
    // Nhóm B6: bàn đã soft-delete không còn chiếm số bàn (unique index có HasFilter IsActive).
    public async Task<RestaurantTableDto> AddRestaurantTableAsync(RestaurantTableDto r) { if(string.IsNullOrWhiteSpace(r.TableNumber)||r.Capacity<=0)throw new ArgumentException("Table number and positive capacity are required"); var number=r.TableNumber.Trim(); if(await _db.RestaurantTables.AnyAsync(x=>x.TableNumber==number&&x.IsActive))throw new InvalidOperationException("Table number already exists"); var x=new RestaurantTable{TableNumber=number,Capacity=r.Capacity,IsActive=true,Status=RestaurantTableStatuses.Available}; await _db.RestaurantTables.AddAsync(x); await _db.SaveChangesAsync(); return Map(x); }
    public async Task<bool> UpdateRestaurantTableAsync(int id, RestaurantTableDto r) { var x=await _db.RestaurantTables.FindAsync(id); if(x is null)return false; if(!string.IsNullOrWhiteSpace(r.TableNumber))x.TableNumber=r.TableNumber.Trim(); if(r.Capacity>0)x.Capacity=r.Capacity; await RefreshStatusAsync(x); return await _db.SaveChangesAsync()>0; }
    public async Task<bool> DeleteRestaurantTableAsync(int id) { var x=await _db.RestaurantTables.FindAsync(id); if(x is null)return false; x.IsActive=false; x.Status=RestaurantTableStatuses.Maintenance; return await _db.SaveChangesAsync()>0; }
    public async Task<bool> SetMaintenanceAsync(int id,bool enabled) { var x=await _db.RestaurantTables.FindAsync(id); if(x is null)return false; x.IsActive=!enabled; await RefreshStatusAsync(x); return await _db.SaveChangesAsync()>0; }

    /// <summary>
    /// Tính lại trạng thái bàn. Được gọi như hệ quả phụ SAU khi transaction chính đã commit
    /// (payment/order), nên xung đột <c>RowVersion</c> ở đây không được làm hỏng kết quả nghiệp vụ:
    /// thử lại trên dữ liệu mới thay vì ném 409.
    /// </summary>
    public async Task RefreshStatusForTableAsync(int id)
    {
        for (var attempt = 0; ; attempt++)
        {
            var x = await _db.RestaurantTables.FindAsync(id);
            if (x is null) return;
            await RefreshStatusAsync(x);

            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 2)
            {
                // Bỏ bản ghi cũ để lần sau đọc lại trạng thái mới nhất.
                _db.Entry(x).State = EntityState.Detached;
            }
        }
    }

    private async Task SaveRefreshedStatusAsync(RestaurantTable x)
    {
        for (var attempt = 0; ; attempt++)
        {
            await RefreshStatusAsync(x);
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 2)
            {
                await _db.Entry(x).ReloadAsync();
            }
        }
    }

    private async Task RefreshStatusAsync(RestaurantTable x) { if(!x.IsActive){x.Status=RestaurantTableStatuses.Maintenance;return;} var occupied=await _db.Orders.AnyAsync(o=>o.TableId==x.TableId&&o.Status!="Paid"&&o.Status!="Completed"&&o.Status!="Cancelled"); x.Status=occupied?RestaurantTableStatuses.Occupied:RestaurantTableStatuses.Available; }
    private static RestaurantTableDto Map(RestaurantTable x)=>new(){TableId=x.TableId,TableNumber=x.TableNumber,Capacity=x.Capacity,Status=x.Status,IsActive=x.IsActive};
}
