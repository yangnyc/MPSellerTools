using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

/// <summary>
/// Issues the antiforgery token pair (cookie + body value) the SPA must echo
/// back in the X-CSRF-TOKEN header on every state-changing request (brief §7).
/// Works unauthenticated so the login request itself can be protected too.
/// </summary>
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
