using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Roles.Employee)]
public class DashboardController(TenantDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var isTenantAdmin = User.IsInRole(Roles.TenantAdmin);
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // brief §8: TenantAdmin sees company-wide metrics; Employee sees only
        // their own personal tasks and assigned orders.
        var orders = db.Orders.AsQueryable();
        var tasks = db.WorkItems.AsQueryable();
        if (!isTenantAdmin)
        {
            orders = orders.Where(o => o.AssignedUserId == currentUserId);
            tasks = tasks.Where(t => t.AssignedUserId == currentUserId);
        }

        var response = new TenantDashboardResponse(
            ProductCount: await db.Products.CountAsync(p => !p.IsArchived),
            OpenOrderCount: await orders.CountAsync(o => o.Status == OrderStatus.New || o.Status == OrderStatus.InProgress),
            OpenTaskCount: await tasks.CountAsync(t => t.Status == WorkItemStatus.Open || t.Status == WorkItemStatus.InProgress),
            TotalOrderCount: await orders.CountAsync(),
            TotalTaskCount: await tasks.CountAsync());

        return Ok(response);
    }
}
