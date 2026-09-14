using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize(Policy = Roles.Employee)]
public class TasksController(TenantDbContext db, AuditLogger audit) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsTenantAdmin => User.IsInRole(Roles.TenantAdmin);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var query = db.WorkItems.AsQueryable();
        if (!IsTenantAdmin)
        {
            query = query.Where(w => w.AssignedUserId == CurrentUserId);
        }
        var items = await query.OrderByDescending(w => w.CreatedAtUtc).ToListAsync();
        return Ok(items.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var item = await db.WorkItems.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }
        if (!IsTenantAdmin && item.AssignedUserId != CurrentUserId)
        {
            return NotFound();
        }
        return Ok(ToResponse(item));
    }

    [HttpPost]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateWorkItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return Problem("Title is required and must be 200 characters or fewer.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!await db.Users.AnyAsync(u => u.Id == request.AssignedUserId && !u.IsBlocked))
        {
            return Problem("Assigned user must be an existing, active user.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = WorkItemStatus.Open,
            AssignedUserId = request.AssignedUserId,
            DueAtUtc = request.DueAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.WorkItems.Add(item);
        audit.Log("TaskCreated", $"title={item.Title}");
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = item.Id }, ToResponse(item));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return Problem("Title is required and must be 200 characters or fewer.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!await db.Users.AnyAsync(u => u.Id == request.AssignedUserId && !u.IsBlocked))
        {
            return Problem("Assigned user must be an existing, active user.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.WorkItems.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        item.Title = request.Title.Trim();
        item.Description = request.Description;
        item.AssignedUserId = request.AssignedUserId;
        item.DueAtUtc = request.DueAtUtc;
        item.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(item).Property(w => w.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("TaskUpdated", $"title={item.Title}");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This task was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(ToResponse(item));
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeWorkItemStatusRequest request)
    {
        var item = await db.WorkItems.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        // An Employee may update the status of their own task, but cannot
        // reassign it or change anything else about it (brief §5) — enforced
        // here by only ever touching Status, never the other fields, and by
        // rejecting anyone whose task this is not.
        if (!IsTenantAdmin && item.AssignedUserId != CurrentUserId)
        {
            return NotFound();
        }

        if (!WorkItemStatusTransitions.IsValid(item.Status, request.Status))
        {
            return Problem($"Cannot transition a task from {item.Status} to {request.Status}.", statusCode: StatusCodes.Status400BadRequest);
        }

        item.Status = request.Status;
        item.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(item).Property(w => w.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("TaskStatusChanged", $"title={item.Title}; status={request.Status}");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This task was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(ToResponse(item));
    }

    private static WorkItemResponse ToResponse(WorkItem w) => new(
        w.Id, w.Title, w.Description, w.Status, w.AssignedUserId, w.DueAtUtc,
        RowVersionCodec.Encode(w.RowVersion), w.CreatedAtUtc, w.UpdatedAtUtc);
}
