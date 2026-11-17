using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/menu")]
[AllowAnonymous]
public sealed class MenuController : ControllerBase
{
    [HttpGet]
    public Task<object> Get([FromServices] MenuService service, CancellationToken ct) => service.GetMenuAsync(ct);
}
