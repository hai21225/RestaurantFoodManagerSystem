using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserService _userService;

    public UsersController(AuthService authService, UserService userService)
    {
        _authService = authService;
        _userService = userService;
    }

    // GET: /api/users  (GĐ6/A2) — danh sách nhân viên kèm role.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var employees = await _userService.GetAllWithRolesAsync(ct);
        return Ok(employees);
    }

    // POST: /api/users  (GĐ6/A2) — tạo nhân viên, gán role ngay nếu có roleName.
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
    [HttpPost("{userId:int}/roles")]
    public async Task<IActionResult> AssignRole(
        int userId,
        [FromBody] AssignRoleRequest request)
    {
        try
        {
            var result = await _authService.AssignRoleToUser(
                userId,
                request.RoleName
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
}
