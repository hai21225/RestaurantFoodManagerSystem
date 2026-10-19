using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly TokenService _tokenService;

    public AuthController(AuthService authService, TokenService tokenService)
    {
        _authService = authService;
        _tokenService = tokenService;
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
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
}