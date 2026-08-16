using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Read-only view over the audit log for the admin UI. Any authenticated
/// role can view the list, but full detail (PIN, full NIA response payload)
/// is Admin-only — Standard users get a redacted summary.
/// </summary>
[ApiController]
[Route("api/v1/transactions")]
[Authorize]
public class TransactionsController(ITransactionQueryService transactions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TransactionQuery query, CancellationToken ct)
    {
        var result = await transactions.QueryAsync(query, ct);

        if (!User.IsInRole("Admin"))
        {
            foreach (var item in result.Items)
            {
                item.PinNumber = null;
            }
        }

        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var detail = await transactions.GetByIdAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}
