using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;
using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Username/password login that issues the service X-API-Key. Clients
/// exchange their username/password for the shared SERVICE_API_KEY-equivalent
/// via POST /api/v1/auth/login, then use that key as X-API-Key on every
/// other endpoint.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(IOptions<ServiceAuthOptions> authOptions, IAuditLogService auditLog) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var opts = authOptions.Value;

        var isValid = SecureCompare.Equals(payload.Username, opts.AuthUsername)
            & SecureCompare.Equals(payload.Password, opts.AuthPassword);

        var statusCode = isValid ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized;

        await auditLog.LogTransactionAsync(new AuditLogEntry
        {
            EndpointPath = "/api/v1/auth/login",
            HttpMethod = "POST",
            Username = payload.Username,
            HttpStatusCode = statusCode,
            RawResponsePayload = isValid
                ? """{"token_type":"apikey"}"""
                : """{"detail":"Invalid username or password."}""",
            DetailsFound = isValid ? "Y" : "N",
            ErrorMessage = isValid ? null : "Invalid username or password.",
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
        }, ct);

        if (!isValid)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { detail = "Invalid username or password." });
        }

        return Ok(new LoginResponse { ApiKey = opts.ApiKey, TokenType = "apikey" });
    }
}
