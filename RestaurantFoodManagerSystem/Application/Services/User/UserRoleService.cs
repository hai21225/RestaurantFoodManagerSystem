using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class UserRoleService
{
    private readonly AppDbContext _db;

    public UserRoleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserRoleDto> GetUserRoleByIdAsync(int id)
    {
        var userRole = await _db.UserRoles.FindAsync(id);
        if (userRole == null)
        {
            throw new Exception("User role not found");
        }
        var userRoleDto = new UserRoleDto
        {
            RoleId = userRole.RoleId,
            UserId = userRole.UserId,
        };
        return userRoleDto;
    }

    public async Task<List<UserRoleDto>> GetAllUserRolesAsync()
    {
        var userRoles = await _db.UserRoles
            .AsNoTracking()
            .ToListAsync();
        var userRoleDtos = userRoles.Select(userRole => new UserRoleDto
        {
            RoleId = userRole.RoleId,
            UserId = userRole.UserId,
        }).ToList();
        return userRoleDtos;
    }

    public async Task<bool> AddUserRoleAsync(UserRoleDto userRoleDto)
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
        await _db.UserRoles.AddAsync(userRole);
        return await _db.SaveChangesAsync()>0;
    }
}
