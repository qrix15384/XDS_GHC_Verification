using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Username/password login. Validates against the ProxyUsers table (a real,
/// individually managed account per person) and, on success, issues both the
/// one shared X-API-Key (unchanged behavior for external API clients) and a
/// personal JWT (for the admin web app's user-management/transaction views).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    IProxyUserService users,
    IPasswordHasher<ProxyUser> passwordHasher,
    JwtTokenService jwtTokenService,
    IOptions<ServiceAuthOptions> authOptions,
    IAuditLogService auditLog,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var user = await users.FindByUsernameAsync(payload.Username, ct);
        var isValid = user is { IsActive: true }
            && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, payload.Password) != PasswordVerificationResult.Failed;

        var statusCode = isValid ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized;

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (isValid)
        {
            logger.LogInformation("Successful login for {Username} from {RemoteIp}", payload.Username, remoteIp);
        }
        else
        {
            logger.LogWarning("Failed login attempt for {Username} from {RemoteIp} ({UserAgent})",
                payload.Username, remoteIp, Request.Headers.UserAgent.ToString());
        }

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
            SubscriberId = user?.SubscriberId,
            SubscriberName = user?.SubscriberName,
        }, ct);

        if (!isValid)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { detail = "Invalid username or password." });
        }

        var issued = jwtTokenService.GenerateToken(user!);

        return Ok(new LoginResponse
        {
            ApiKey = authOptions.Value.ApiKey,
            TokenType = "apikey",
            Token = issued.Token,
            Role = user!.Role,
            ExpiresAtUtc = issued.ExpiresAtUtc,
        });
    }
}
