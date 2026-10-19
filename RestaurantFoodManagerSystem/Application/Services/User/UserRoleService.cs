using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class UserRoleService
{
    private readonly AppDbContext _db;

    public UserRoleService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Tên các role gán cho một user (dùng để nạp claim khi đăng nhập — GĐ6).</summary>
    public async Task<List<string>> GetRoleNamesByUserIdAsync(int userId, CancellationToken ct = default)
    {
        return await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                _db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.RoleId,
                (ur, r) => r.RoleName)
            .OrderBy(name => name)
            .ToListAsync(ct);
    }

    public async Task<bool> AddUserRoleAsync(UserRoleDto userRoleDto, CancellationToken ct = default)
    {
        if (userRoleDto == null)
        {
            throw new ArgumentNullException(nameof(userRoleDto));
        }
        var userRole = new UserRoles
        {
            RoleId = userRoleDto.RoleId,
            UserId = userRoleDto.UserId,
        };
        await _db.UserRoles.AddAsync(userRole, ct);
        return await _db.SaveChangesAsync(ct)>0;
    }
}
