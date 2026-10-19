using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly UserService _userService;
    private readonly UserRoleService _userRoleService;
    private readonly RoleService _roleService;
    public AuthService(AppDbContext db, UserService userService, UserRoleService userRoleService,RoleService roleService)
    {
        _db = db;
        _userService = userService;
        _userRoleService = userRoleService;
        _roleService = roleService;
    }

    public async Task<UserDto?> Login(string username, string password, CancellationToken ct = default)
    {
        var users = await _userService.FindUserByUsernameAsync(username, ct);

        if (users == null || users.IsActive != true || string.IsNullOrWhiteSpace(users.Password))
        {
            return null;
        }

        var result = BCrypt.Net.BCrypt.Verify(password, users.Password);
        
        if(!result)
        {
            return null;
        }

        return users;
    }

    public async Task<bool> Register(UserDto userDto, CancellationToken ct = default)
    {
        var user = await _userService.FindUserByUsernameAsync(userDto.UserName ?? "", ct);
        if (user != null)
        {
            throw new InvalidOperationException("Username already exists");
        }

        if (string.IsNullOrWhiteSpace(userDto.UserName) ||
            string.IsNullOrWhiteSpace(userDto.Email) ||
            string.IsNullOrWhiteSpace(userDto.Password))
        {
            throw new ArgumentException("Username, email and password are required");
        }

        if(userDto.Password != userDto.ConfirmPassword)
        {
            return false;
        }
        var userdto = new UserDto
        {
            UserName = userDto.UserName,
            Email = userDto.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(userDto.Password),
            FullName = userDto.FullName,
            Phone = userDto.Phone,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return await _userService.AddUserAsync(userdto, ct);
    }

    /// <summary>
    /// Tên các role của user, dùng để nạp claim vào JWT khi login (GĐ6).
    /// Ở lát 6a chỉ đọc thêm, không đổi hành vi đăng nhập hiện tại.
    /// </summary>
    public Task<List<string>> GetRoleNamesByUserIdAsync(int userId, CancellationToken ct = default)
    {
        return _userRoleService.GetRoleNamesByUserIdAsync(userId, ct);
    }

    public async Task<bool> AssignRoleToUser(int userid, string rolename, CancellationToken ct = default)
    {

        var role = await _roleService.GetRoleByRoleNameAsync(rolename, ct);


        if (role == null)
        {
            throw new Exception("Role not found");
        }
        var result=await _userRoleService.AddUserRoleAsync(new UserRoleDto
        {
            UserId = userid,
            RoleId = role.RoleId,
        }, ct);

        return result;
    }

    /// <summary>
    /// Tự đổi mật khẩu (P4) — bắt buộc xác nhận mật khẩu hiện tại.
    /// Lưu hash BCrypt mới và cập nhật <c>UpdatedAt</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Không tìm thấy user.</exception>
    /// <exception cref="ArgumentException">Dữ liệu không hợp lệ.</exception>
    /// <exception cref="InvalidOperationException">Mật khẩu hiện tại sai.</exception>
    public async Task ChangePasswordAsync(
        int userId,
        ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var currentPassword = request.CurrentPassword ?? string.Empty;
        var newPassword = request.NewPassword ?? string.Empty;
        var confirmPassword = request.ConfirmNewPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            throw new ArgumentException("Current password is required");
        }

        // BCrypt chỉ dùng tối đa 72 byte đầu; chặn sớm để không âm thầm cắt mật khẩu.
        if (newPassword.Length < 6 || newPassword.Length > 72)
        {
            throw new ArgumentException("New password must be between 6 and 72 characters");
        }

        if (newPassword != confirmPassword)
        {
            throw new ArgumentException("New password and confirm password do not match");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found");

        if (!VerifyPassword(currentPassword, user.UserPassword))
        {
            throw new InvalidOperationException("Current password is incorrect");
        }

        if (VerifyPassword(newPassword, user.UserPassword))
        {
            throw new ArgumentException("New password must be different from the current password");
        }

        user.UserPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Admin đặt lại mật khẩu hộ nhân viên (Batch A). Không cần mật khẩu hiện tại;
    /// vẫn áp dụng cùng chính sách độ dài 6–72 ký tự và xác nhận mật khẩu.
    /// Token JWT cũ không bị thu hồi (hệ thống không lưu token) — giống đổi mật khẩu tự phục vụ.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Không tìm thấy user.</exception>
    /// <exception cref="ArgumentException">Dữ liệu không hợp lệ.</exception>
    public async Task ResetPasswordAsync(
        int userId,
        ResetUserPasswordRequest request,
        CancellationToken ct = default)
    {
        var newPassword = request.NewPassword ?? string.Empty;
        var confirmPassword = request.ConfirmPassword ?? string.Empty;

        if (newPassword.Length < 6 || newPassword.Length > 72)
        {
            throw new ArgumentException("New password must be between 6 and 72 characters");
        }

        if (newPassword != confirmPassword)
        {
            throw new ArgumentException("New password and confirm password do not match");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found");

        user.UserPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private static bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash lưu trong DB không đúng định dạng BCrypt ⇒ coi như không khớp,
            // không để lỗi 500 lộ chi tiết ra ngoài.
            return false;
        }
    }

    /// <summary>
    /// Thu hồi một role khỏi user (P4). Chặn xoá role <c>Admin</c> cuối cùng để không
    /// tự khoá toàn bộ hệ thống.
    /// </summary>
    /// <exception cref="KeyNotFoundException">User/role/assignment không tồn tại.</exception>
    /// <exception cref="InvalidOperationException">Đây là Admin cuối cùng.</exception>
    public async Task RemoveRoleFromUserAsync(
        int userId,
        string roleName,
        CancellationToken ct = default)
    {
        var normalizedName = roleName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Role name is required");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            throw new KeyNotFoundException("User not found");
        }

        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleName == normalizedName, ct)
            ?? throw new KeyNotFoundException("Role not found");

        var link = await _db.UserRoles
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == role.RoleId, ct)
            ?? throw new KeyNotFoundException("Role is not assigned to this user");

        if (role.RoleName == AuthPolicies.Roles.Admin)
        {
            var anotherAdminExists = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.RoleId
                where r.RoleName == AuthPolicies.Roles.Admin && ur.UserId != userId
                select ur.UserId)
                .AnyAsync(ct);

            if (!anotherAdminExists)
            {
                throw new InvalidOperationException("Cannot remove the last Admin role");
            }
        }

        _db.UserRoles.Remove(link);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Use-case tạo nhân viên kèm role (GĐ6/A2) cho <c>POST /api/users</c>.
    /// Bọc trong transaction để "user + role" là nguyên tử: gán role lỗi thì user cũng không được tạo.
    /// </summary>
    public async Task<bool> CreateUserWithRoleAsync(UserDto userDto, string? roleName, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var created = await Register(userDto, ct);
        if (!created)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var user = await _userService.FindUserByUsernameAsync(userDto.UserName ?? "", ct);
            if (user is null)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            await AssignRoleToUser(user.UserId, roleName.Trim(), ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

}
