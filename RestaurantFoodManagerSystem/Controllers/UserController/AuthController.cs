using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    // POST: /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _authService.Login(
            request.UserName,
            request.Password
        );

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid username or password"
            });
        }

        var response = new LoginResponse
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            FullName = user.FullName
        };

        return Ok(new
        {
            message = "Login successful",
            data = response
        });
    }


    // POST: /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request)
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

            var result = await _authService.Register(userDto);

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