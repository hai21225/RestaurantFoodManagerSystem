using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class RoleService
{
    private readonly AppDbContext _db;

    public RoleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Roles> GetRoleByRoleNameAsync(string name)
    {
        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoleName == name);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        return role;
    }
}
