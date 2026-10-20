using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly TokenService _tokenService;
    private readonly ICurrentUser _currentUser;

    public AuthController(AuthService authService, TokenService tokenService, ICurrentUser currentUser)
    {
        _authService = authService;
        _tokenService = tokenService;
        _currentUser = currentUser;
    }

    // POST: /api/auth/login  (public)
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct = default)
    {
        var user = await _authService.Login(
            request.UserName,
            request.Password,
            ct
        );

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid username or password"
            });
        }

        var roles = await _authService.GetRoleNamesByUserIdAsync(user.UserId, ct);

        var response = new LoginResponse
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            FullName = user.FullName,
            Roles = roles,
            Token = _tokenService.CreateToken(user, roles)
        };

        return Ok(new
        {
            message = "Login successful",
            data = response
        });
    }


    // POST: /api/auth/change-password  (P4) — tự đổi mật khẩu, mọi role đã đăng nhập.
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        if (_currentUser.UserId is not int userId)
        {
            return Unauthorized(new { message = "Not authenticated" });
        }

        try
        {
            await _authService.ChangePasswordAsync(userId, request, ct);
            return Ok(new { message = "Password changed successfully" });
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


    // POST: /api/auth/register  (AdminOnly)
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct = default)
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
            var userDto = new UserDto
            {
                UserName = request.UserName,
                Email = request.Email,
                Password = request.Password,
                ConfirmPassword = request.ConfirmPassword,
                FullName = request.FullName,
                Phone = request.Phone
            };

            var result = await _authService.Register(userDto, ct);

            if (!result)
            {
                return BadRequest(new
                {
                    message = "Register failed"
                });
            }

            return Ok(new
            {
                message = "Register successful"
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
        catch (DbUpdateException)
        {
            // Race tạo trùng username/email: unique index của DB là chốt chặn cuối.
            // Không trả message SQL cho client.
            return Conflict(new
            {
                message = "Username or email already exists"
            });
        }
    }
}