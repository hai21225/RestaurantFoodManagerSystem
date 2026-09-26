using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/users")]
public class UserRolesController : ControllerBase
{
    private readonly AuthService _authService;

    public UserRolesController(AuthService authService)
    {
        _authService = authService;
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