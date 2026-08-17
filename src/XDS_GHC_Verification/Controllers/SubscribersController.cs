using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Manages client-organization ("subscriber") records that ProxyUsers
/// accounts can be assigned to. Gated by JWT + Admin role, same as
/// ProxyUsersController — not X-API-Key.
/// </summary>
[ApiController]
[Route("api/v1/subscribers")]
[Authorize(Roles = "Admin")]
public class SubscribersController(ISubscriberService subscribers, IProxyUserService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await subscribers.ListAsync(ct);
        return Ok(all.Select(SubscriberResponse.FromEntity));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubscriberRequest payload, CancellationToken ct)
    {
        if (await subscribers.FindByNameAsync(payload.Name, ct) is not null)
        {
            return Conflict(new { detail = "A subscriber with that name already exists." });
        }

        var created = await subscribers.CreateAsync(payload.Name, ct);
        return Ok(SubscriberResponse.FromEntity(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSubscriberRequest payload, CancellationToken ct)
    {
        if (await subscribers.FindByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        var duplicate = await subscribers.FindByNameAsync(payload.Name, ct);
        if (duplicate is not null && duplicate.Id != id)
        {
            return Conflict(new { detail = "A subscriber with that name already exists." });
        }

        await subscribers.UpdateAsync(id, payload.Name, payload.IsActive, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await subscribers.FindByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        if (await users.CountBySubscriberIdAsync(id, ct) > 0)
        {
            return BadRequest(new { detail = "Reassign or remove the users on this subscriber before deleting it." });
        }

        await subscribers.DeleteAsync(id, ct);
        return NoContent();
    }
}
