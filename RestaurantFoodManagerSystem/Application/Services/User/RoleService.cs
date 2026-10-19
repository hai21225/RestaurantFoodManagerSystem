using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class RoleService
{
    private readonly AppDbContext _db;

    public RoleService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Danh sách role cố định (P4) cho <c>GET /api/roles</c>. Chỉ đọc — không CRUD role
    /// và không trả ma trận quyền (ma trận là policy trong code).
    /// </summary>
    public async Task<List<RoleResponse>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.RoleId)
            .Select(r => new RoleResponse
            {
                RoleId = r.RoleId,
                RoleName = r.RoleName,
                RoleDescription = r.RoleDescription
            })
            .ToListAsync(ct);
    }

    public async Task<Roles> GetRoleByRoleNameAsync(string name, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoleName == name, ct);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        return role;
    }
}
