using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

public static class RestaurantTableStatuses { public const string Available="Available"; public const string Occupied="Occupied"; public const string Maintenance="Maintenance"; }

public class RestaurantTableService
{
    private readonly IUnitOfWork _unitOfWork; private readonly IRepository<RestaurantTable> _repository; private readonly AppDbContext _db;
    public RestaurantTableService(IUnitOfWork unitOfWork, IRepository<RestaurantTable> repository, AppDbContext db) { _unitOfWork=unitOfWork; _repository=repository; _db=db; }
    public async Task<RestaurantTableDto?> GetRestaurantTableByIdAsync(int id) { var x=await _repository.GetByIdAsync(id); if(x is null)return null; await RefreshStatusAsync(x); await _db.SaveChangesAsync(); return Map(x); }
    public async Task<List<RestaurantTableDto>> GetAllRestaurantTablesAsync() { var xs=await _repository.GetAllAsync(); foreach(var x in xs) await RefreshStatusAsync(x); await _db.SaveChangesAsync(); return xs.Select(Map).ToList(); }
    public async Task<RestaurantTableDto> AddRestaurantTableAsync(RestaurantTableDto r) { if(string.IsNullOrWhiteSpace(r.TableNumber)||r.Capacity<=0)throw new ArgumentException("Table number and positive capacity are required"); var number=r.TableNumber.Trim(); if(await _repository.ExistsAsync(x=>x.TableNumber==number))throw new InvalidOperationException("Table number already exists"); var x=new RestaurantTable{TableNumber=number,Capacity=r.Capacity,IsActive=true,Status=RestaurantTableStatuses.Available}; await _repository.AddAsync(x); await _unitOfWork.SaveChangesAsync(); return Map(x); }
    public async Task<bool> UpdateRestaurantTableAsync(int id, RestaurantTableDto r) { var x=await _repository.GetByIdAsync(id); if(x is null)return false; if(!string.IsNullOrWhiteSpace(r.TableNumber))x.TableNumber=r.TableNumber.Trim(); if(r.Capacity>0)x.Capacity=r.Capacity; await RefreshStatusAsync(x); return await _unitOfWork.SaveChangesAsync()>0; }
    public async Task<bool> DeleteRestaurantTableAsync(int id) { var x=await _repository.GetByIdAsync(id); if(x is null)return false; x.IsActive=false; x.Status=RestaurantTableStatuses.Maintenance; return await _unitOfWork.SaveChangesAsync()>0; }
    public async Task<bool> SetMaintenanceAsync(int id,bool enabled) { var x=await _repository.GetByIdAsync(id); if(x is null)return false; x.IsActive=!enabled; await RefreshStatusAsync(x); return await _unitOfWork.SaveChangesAsync()>0; }
    public async Task RefreshStatusForTableAsync(int id) { var x=await _repository.GetByIdAsync(id); if(x is null)return; await RefreshStatusAsync(x); await _db.SaveChangesAsync(); }
    private async Task RefreshStatusAsync(RestaurantTable x) { if(!x.IsActive){x.Status=RestaurantTableStatuses.Maintenance;return;} var occupied=await _db.Orders.AnyAsync(o=>o.TableId==x.TableId&&o.Status!="Paid"&&o.Status!="Completed"&&o.Status!="Cancelled"); x.Status=occupied?RestaurantTableStatuses.Occupied:RestaurantTableStatuses.Available; }
    private static RestaurantTableDto Map(RestaurantTable x)=>new(){TableId=x.TableId,TableNumber=x.TableNumber,Capacity=x.Capacity,Status=x.Status,IsActive=x.IsActive};
}
