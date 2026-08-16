using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Manages ProxyUsers accounts. Gated by JWT + Admin role (not X-API-Key) —
/// only people who are already logged in as an Admin can reach this.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = "Admin")]
public class ProxyUsersController(IProxyUserService users, IPasswordHasher<ProxyUser> passwordHasher) : ControllerBase
{
    private static readonly string[] ValidRoles = ["Admin", "Standard"];

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await users.ListAsync(ct);
        return Ok(all.Select(ProxyUserResponse.FromEntity));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProxyUserRequest payload, CancellationToken ct)
    {
        if (!ValidRoles.Contains(payload.Role))
        {
            return BadRequest(new { detail = $"Role must be one of: {string.Join(", ", ValidRoles)}." });
        }

        if (await users.FindByUsernameAsync(payload.Username, ct) is not null)
        {
            return Conflict(new { detail = "A user with that username already exists." });
        }

        var newUser = new ProxyUser { Username = payload.Username, Role = payload.Role };
        var passwordHash = passwordHasher.HashPassword(newUser, payload.Password);
        var created = await users.CreateAsync(payload.Username, passwordHash, payload.Role, ct);

        return Ok(ProxyUserResponse.FromEntity(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProxyUserRequest payload, CancellationToken ct)
    {
        if (!ValidRoles.Contains(payload.Role))
        {
            return BadRequest(new { detail = $"Role must be one of: {string.Join(", ", ValidRoles)}." });
        }

        var target = await users.FindByIdAsync(id, ct);
        if (target is null)
        {
            return NotFound();
        }

        var demotingOrDeactivatingSelf = IsSelf(id) && (payload.Role != "Admin" || !payload.IsActive);
        if (demotingOrDeactivatingSelf)
        {
            return BadRequest(new { detail = "You cannot demote or deactivate your own account." });
        }

        var wouldRemoveLastAdmin = target.Role == "Admin" && target.IsActive
            && (payload.Role != "Admin" || !payload.IsActive)
            && await users.CountActiveAdminsAsync(ct) <= 1;
        if (wouldRemoveLastAdmin)
        {
            return BadRequest(new { detail = "Cannot remove the last active Admin account." });
        }

        await users.UpdateRoleAndStatusAsync(id, payload.Role, payload.IsActive, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest payload, CancellationToken ct)
    {
        var target = await users.FindByIdAsync(id, ct);
        if (target is null)
        {
            return NotFound();
        }

        var passwordHash = passwordHasher.HashPassword(target, payload.NewPassword);
        await users.UpdatePasswordHashAsync(id, passwordHash, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (IsSelf(id))
        {
            return BadRequest(new { detail = "You cannot delete your own account." });
        }

        var target = await users.FindByIdAsync(id, ct);
        if (target is null)
        {
            return NotFound();
        }

        if (target.Role == "Admin" && target.IsActive && await users.CountActiveAdminsAsync(ct) <= 1)
        {
            return BadRequest(new { detail = "Cannot delete the last active Admin account." });
        }

        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    private bool IsSelf(int id) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId) && callerId == id;
}
