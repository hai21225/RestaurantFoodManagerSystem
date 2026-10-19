using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserService _userService;
    private readonly ICurrentUser _currentUser;

    public UsersController(AuthService authService, UserService userService, ICurrentUser currentUser)
    {
        _authService = authService;
        _userService = userService;
        _currentUser = currentUser;
    }

    // GET: /api/users/me  (P4) — hồ sơ của chính mình; mọi role đã đăng nhập.
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (_currentUser.UserId is not int userId)
        {
            return Unauthenticated();
        }

        var profile = await _userService.GetProfileAsync(userId, ct);

        if (profile is null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(profile);
    }

    // PUT: /api/users/me  (P4) — tự sửa fullName/email/phone; không đổi được quyền/trạng thái.
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateProfileRequest request,
        CancellationToken ct)
    {
        if (_currentUser.UserId is not int userId)
        {
            return Unauthenticated();
        }

        try
        {
            var profile = await _userService.UpdateProfileAsync(userId, request, ct);
            return Ok(profile);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET: /api/users/{id}  (Batch A) — chi tiết một nhân viên cho màn hình quản lý.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(id, ct);

        if (profile is null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(profile);
    }

    // PUT: /api/users/{id}  (Batch A) — Admin sửa fullName/email/phone của nhân viên.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _userService.UpdateUserAsync(id, request, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PATCH: /api/users/{id}/active?active=false  (Batch A) — khoá/mở tài khoản.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool? active, CancellationToken ct)
    {
        if (active is null)
        {
            return BadRequest(new { message = "active is required" });
        }

        try
        {
            return Ok(await _userService.SetActiveAsync(id, active.Value, _currentUser.UserId, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST: /api/users/{id}/reset-password  (Batch A) — Admin đặt lại mật khẩu hộ nhân viên.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetUserPasswordRequest request, CancellationToken ct)
    {
        try
        {
            await _authService.ResetPasswordAsync(id, request, ct);
            return Ok(new { message = "Password reset successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET: /api/users  (GĐ6/A2) — danh sách nhân viên kèm role.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var employees = await _userService.GetAllWithRolesAsync(ct);
        return Ok(employees);
    }

    // POST: /api/users  (GĐ6/A2) — tạo nhân viên, gán role ngay nếu có roleName.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken ct)
    {
        if (request.Password != request.ConfirmPassword)
        {
            return BadRequest(new
            {
                message = "Password and confirm password do not match"
            });
        }

        try
        {
            var created = await _authService.CreateUserWithRoleAsync(
                new UserDto
                {
                    UserName = request.UserName,
                    Email = request.Email,
                    Password = request.Password,
                    ConfirmPassword = request.ConfirmPassword,
                    FullName = request.FullName,
                    Phone = request.Phone
                },
                request.RoleName,
                ct);

            if (!created)
            {
                return BadRequest(new
                {
                    message = "Create employee failed"
                });
            }

            return Ok(new
            {
                message = "Employee created successfully"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST: /api/users/{userId}/roles
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("{userId:int}/roles")]
    public async Task<IActionResult> AssignRole(
        int userId,
        [FromBody] AssignRoleRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _authService.AssignRoleToUser(
                userId,
                request.RoleName,
                ct
            );

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Failed to assign role"
                });
            }

            return Ok(new
            {
                message = "Role assigned successfully"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    // DELETE: /api/users/{userId}/roles/{roleName}  (P4) — thu hồi role.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("{userId:int}/roles/{roleName}")]
    public async Task<IActionResult> RemoveRole(
        int userId,
        string roleName,
        CancellationToken ct)
    {
        try
        {
            await _authService.RemoveRoleFromUserAsync(userId, roleName, ct);
            return Ok(new { message = "Role removed successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Hồ sơ tự phục vụ cần biết "tôi là ai"; khi <c>Auth:Enforce=false</c> policy cho qua
    /// nhưng request không có token ⇒ trả 401 thay vì đoán user.
    /// </summary>
    private UnauthorizedObjectResult Unauthenticated() =>
        Unauthorized(new { message = "Not authenticated" });
}
