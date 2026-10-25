using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Khởi tạo hệ thống lần đầu (GĐ6/A1). Hai endpoint đều <c>[AllowAnonymous]</c> vì phải
/// dùng được TRƯỚC khi có tài khoản nào; an toàn được bảo đảm bằng quy tắc
/// "chỉ khi bảng Users rỗng" trong <see cref="SetupService"/>.
/// </summary>
[ApiController]
[Route("api/setup")]
public sealed class SetupController : ControllerBase
{
    private readonly SetupService _setupService;

    public SetupController(SetupService setupService)
    {
        _setupService = setupService;
    }

    // GET /api/setup/status → { requiresSetup }
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var requiresSetup = await _setupService.IsSetupRequiredAsync(ct);
        return Ok(new SetupStatusResponse { RequiresSetup = requiresSetup });
    }

    // POST /api/setup/first-admin
    [AllowAnonymous]
    [HttpPost("first-admin")]
    public async Task<IActionResult> FirstAdmin(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        try
        {
            var created = await _setupService.CreateFirstAdminAsync(request, ct);

            if (!created)
            {
                return Conflict(new
                {
                    message = "System is already initialized"
                });
            }

            return Ok(new
            {
                message = "First admin created successfully"
            });
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
}
