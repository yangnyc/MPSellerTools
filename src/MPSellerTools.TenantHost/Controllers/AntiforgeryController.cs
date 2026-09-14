using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/antiforgery")]
[AllowAnonymous]
public class AntiforgeryController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("token")]
    public IActionResult GetToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }
}
