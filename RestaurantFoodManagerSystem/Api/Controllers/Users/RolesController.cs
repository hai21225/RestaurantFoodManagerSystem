using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// <c>GET /api/roles</c> (P4) — chỉ đọc danh sách role cố định.
///
/// Không có CRUD role và không trả ma trận quyền: 5 role được seed sẵn, còn quyền
/// từng endpoint là policy trong code (<see cref="AuthPolicies"/>).
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class RolesController : ControllerBase
{
    private readonly RoleService _roleService;

    public RolesController(RoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Ok(await _roleService.GetAllAsync(ct));
    }
}
