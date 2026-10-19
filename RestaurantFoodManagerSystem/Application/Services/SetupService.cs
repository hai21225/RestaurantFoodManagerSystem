using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Nghiệp vụ khởi tạo hệ thống lần đầu (GĐ6/A1) — đáp ứng
/// <c>GET /api/setup/status</c> và <c>POST /api/setup/first-admin</c> của frontend.
///
/// Quy tắc an toàn (đã chốt): chỉ cho tạo Admin đầu tiên khi bảng <c>Users</c> còn rỗng.
/// Sau khi đã có bất kỳ user nào, endpoint trả lỗi ⇒ không thể tự phong Admin qua đường này.
/// </summary>
public sealed class SetupService
{
    private const string FirstAdminRole = "Admin";

    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly UserService _userService;

    public SetupService(AppDbContext db, AuthService authService, UserService userService)
    {
        _db = db;
        _authService = authService;
        _userService = userService;
    }

    /// <summary>Hệ thống cần khởi tạo khi chưa có user nào.</summary>
    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default)
        => !await _db.Users.AnyAsync(ct);

    /// <summary>
    /// Tạo tài khoản Admin đầu tiên. Chỉ thành công khi chưa có user nào và role "Admin" đã được seed.
    /// Trả về <c>false</c> nếu hệ thống đã được khởi tạo trước đó.
    /// </summary>
    public async Task<bool> CreateFirstAdminAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Password != request.ConfirmPassword)
        {
            throw new ArgumentException("Password and confirm password do not match");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Kiểm tra bên trong transaction để hai request đồng thời không cùng tạo được Admin.
        if (await _db.Users.AnyAsync(ct))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var created = await _authService.Register(new UserDto
        {
            UserName = request.UserName,
            Email = request.Email,
            Password = request.Password,
            ConfirmPassword = request.ConfirmPassword,
            FullName = request.FullName,
            Phone = request.Phone
        });

        if (!created)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("Cannot create the first admin account");
        }

        var user = await _userService.FindUserByUsernameAsync(request.UserName ?? "");
        if (user is null)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("Cannot create the first admin account");
        }

        await _authService.AssignRoleToUser(user.UserId, FirstAdminRole);
        await tx.CommitAsync(ct);
        return true;
    }
}
