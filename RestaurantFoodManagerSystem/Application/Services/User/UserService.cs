using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDto?> FindUserByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return null;
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == normalizedUsername, ct);

        if (user == null)
        {
            return null;
        }
        var userDto = new UserDto
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.UserEmail,
            Password = user.UserPassword,
            FullName = user.UserFullName,
            Phone = user.UserPhone,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
        return userDto;
    }

    /// <summary>
    /// Danh sách nhân viên kèm tên role (GĐ6/A2) — phục vụ <c>GET /api/users</c>.
    /// Đọc 3 bảng rồi ghép trong bộ nhớ để tránh N+1.
    /// </summary>
    public async Task<List<UserListItemResponse>> GetAllWithRolesAsync(CancellationToken ct = default)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        var roleLinks = await (from ur in _db.UserRoles.AsNoTracking()
                               join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId
                               select new { ur.UserId, r.RoleName })
            .ToListAsync(ct);

        var rolesByUser = roleLinks
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName).OrderBy(name => name).ToList());

        return users.Select(u => new UserListItemResponse
        {
            UserId = u.Id,
            UserName = u.UserName ?? string.Empty,
            FullName = u.UserFullName ?? string.Empty,
            Email = u.UserEmail ?? string.Empty,
            Phone = u.UserPhone ?? string.Empty,
            IsActive = u.IsActive,
            Roles = rolesByUser.TryGetValue(u.Id, out var roles) ? roles : []
        }).ToList();
    }

    /// <summary>
    /// Hồ sơ của một user kèm role (P4) — dùng cho <c>GET /api/users/me</c>.
    /// Trả <c>null</c> khi không tìm thấy user (controller đổi thành 404).
    /// </summary>
    public async Task<UserProfileResponse?> GetProfileAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return null;
        }

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                _db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.RoleId,
                (ur, r) => r.RoleName)
            .OrderBy(name => name)
            .ToListAsync(ct);

        return new UserProfileResponse
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            FullName = user.UserFullName ?? string.Empty,
            Email = user.UserEmail ?? string.Empty,
            Phone = user.UserPhone ?? string.Empty,
            IsActive = user.IsActive,
            Roles = roles,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    /// <summary>
    /// Cập nhật hồ sơ tự phục vụ (P4) — chỉ sửa fullName/email/phone, không đụng role hay
    /// trạng thái tài khoản. Email vẫn là định danh nên phải duy nhất (không phân biệt hoa/thường).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Không tìm thấy user.</exception>
    /// <exception cref="ArgumentException">Dữ liệu không hợp lệ.</exception>
    /// <exception cref="InvalidOperationException">Email đã thuộc user khác.</exception>
    public async Task<UserProfileResponse> UpdateProfileAsync(
        int userId,
        UpdateProfileRequest request,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found");

        await ApplyContactUpdateAsync(user, request.FullName, request.Email, request.Phone, ct);

        return await GetProfileAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found after update");
    }

    /// <summary>
    /// Admin sửa hồ sơ nhân viên (Batch A) — cùng phạm vi field với hồ sơ tự phục vụ,
    /// không đổi được username/role/trạng thái qua endpoint này.
    /// </summary>
    public async Task<UserProfileResponse> UpdateUserAsync(
        int userId,
        UpdateUserRequest request,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found");

        await ApplyContactUpdateAsync(user, request.FullName, request.Email, request.Phone, ct);

        return await GetProfileAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found after update");
    }

    /// <summary>
    /// Admin khoá/mở tài khoản nhân viên (Batch A). Chặn tự khoá chính mình và chặn khoá
    /// Admin đang hoạt động cuối cùng để không tự biến hệ thống thành không còn người quản trị.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Không tìm thấy user.</exception>
    /// <exception cref="InvalidOperationException">Vi phạm quy tắc an toàn.</exception>
    public async Task<UserProfileResponse> SetActiveAsync(
        int userId,
        bool isActive,
        int? actingUserId,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found");

        // Idempotent: gửi lại đúng trạng thái hiện tại không phải lỗi.
        if (user.IsActive != isActive)
        {
            if (!isActive)
            {
                if (actingUserId == userId)
                {
                    throw new InvalidOperationException("You cannot lock your own account");
                }

                if (await HasRoleAsync(userId, AuthPolicies.Roles.Admin, ct))
                {
                    var anotherActiveAdmin = await (
                        from ur in _db.UserRoles
                        join r in _db.Roles on ur.RoleId equals r.RoleId
                        join u in _db.Users on ur.UserId equals u.Id
                        where r.RoleName == AuthPolicies.Roles.Admin
                           && ur.UserId != userId
                           && u.IsActive
                        select ur.UserId)
                        .AnyAsync(ct);

                    if (!anotherActiveAdmin)
                    {
                        throw new InvalidOperationException("Cannot lock the last active Admin account");
                    }
                }
            }

            user.IsActive = isActive;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return await GetProfileAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found after update");
    }

    /// <summary>
    /// Phần validate + ghi dùng chung cho tự sửa hồ sơ và Admin sửa hồ sơ nhân viên.
    /// </summary>
    private async Task ApplyContactUpdateAsync(
        Users user,
        string? rawFullName,
        string? rawEmail,
        string? rawPhone,
        CancellationToken ct)
    {
        var fullName = rawFullName?.Trim() ?? string.Empty;
        var email = rawEmail?.Trim() ?? string.Empty;
        var phone = rawPhone?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required");
        }

        if (fullName.Length > 150)
        {
            throw new ArgumentException("Full name must be at most 150 characters");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required");
        }

        if (email.Length > 150)
        {
            throw new ArgumentException("Email must be at most 150 characters");
        }

        if (!IsPlausibleEmail(email))
        {
            throw new ArgumentException("Email format is invalid");
        }

        if (phone.Length > 20)
        {
            throw new ArgumentException("Phone must be at most 20 characters");
        }

        // LOWER() dịch được trên cả SQL Server lẫn SQLite ⇒ bắt trùng email không phân biệt hoa/thường
        // đúng như unique index của DB thật (collation mặc định không phân biệt hoa/thường).
        var normalizedEmail = email.ToLowerInvariant();
        var emailTaken = await _db.Users.AnyAsync(
            x => x.Id != user.Id && x.UserEmail.ToLower() == normalizedEmail,
            ct);

        if (emailTaken)
        {
            throw new InvalidOperationException("Email already exists");
        }

        user.UserFullName = fullName;
        user.UserEmail = email;
        user.UserPhone = phone;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private Task<bool> HasRoleAsync(int userId, string roleName, CancellationToken ct) =>
        (from ur in _db.UserRoles
         join r in _db.Roles on ur.RoleId equals r.RoleId
         where ur.UserId == userId && r.RoleName == roleName
         select ur.UserId)
        .AnyAsync(ct);

    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0
            && at < email.Length - 1
            && !email.Any(char.IsWhiteSpace);
    }

    public async Task<bool> AddUserAsync(UserDto userdto, CancellationToken ct = default)
    {
        var username = userdto.UserName?.Trim();
        var email = userdto.Email?.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Username and email are required");
        }

        if (await _db.Users.AnyAsync(x => x.UserName == username, ct))
        {
            throw new InvalidOperationException("Username already exists");
        }

        if (await _db.Users.AnyAsync(x => x.UserEmail == email, ct))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var user = new Users
        {
            UserName = username,
            UserPassword = userdto.Password??"",
            UserFullName = userdto.FullName??"",
            UserEmail = email,
            UserPhone = userdto.Phone??"",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _db.Users.AddAsync(user, ct);
        return await _db.SaveChangesAsync(ct)>0;

    }
}
