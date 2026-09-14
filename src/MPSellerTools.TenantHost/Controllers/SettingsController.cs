using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Policy = Roles.TenantAdmin)]
public class SettingsController(TenantDbContext db, AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var settings = await db.CompanySettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            return NotFound();
        }
        return Ok(new CompanySettingsResponse(settings.CompanyName, RowVersionCodec.Encode(settings.RowVersion)));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateCompanySettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName) || request.CompanyName.Length > 200)
        {
            return Problem("Company name is required and must be 200 characters or fewer.", statusCode: StatusCodes.Status400BadRequest);
        }

        var settings = await db.CompanySettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            return NotFound();
        }

        settings.CompanyName = request.CompanyName.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(settings).Property(s => s.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("CompanySettingsUpdated");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("Settings were modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(new CompanySettingsResponse(settings.CompanyName, RowVersionCodec.Encode(settings.RowVersion)));
    }
}
