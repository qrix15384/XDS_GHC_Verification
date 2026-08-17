using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Read-only view of client-organization ("subscriber") records, sourced
/// live from the real, authoritative XdsGhanaAdmin.dbo.Subscriber table.
/// This app never creates, edits, or deletes subscribers — they're managed
/// entirely outside this codebase; this controller only lists them so an
/// Admin can assign a ProxyUsers account to one.
/// </summary>
[ApiController]
[Route("api/v1/subscribers")]
[Authorize(Roles = "Admin")]
public class SubscribersController(ISubscriberService subscribers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await subscribers.ListAsync(ct);
        return Ok(all.Select(SubscriberResponse.FromEntity));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var subscriber = await subscribers.FindByIdAsync(id, ct);
        return subscriber is null ? NotFound() : Ok(SubscriberResponse.FromEntity(subscriber));
    }
}
