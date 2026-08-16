using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Auth;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Catch-all proxy router. Accepts any HTTP method and any sub-path under
/// /api/v1/proxy and forwards it transparently to the upstream API.
/// </summary>
[ApiController]
[Route("api/v1/proxy")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class ProxyController(UpstreamClient upstream, IAuditLogService auditLog, IOptions<ServiceAuthOptions> authOptions)
    : ControllerBase
{
    private static readonly HashSet<string> SafeForwardedHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "content-type", "accept", "x-request-id", "x-correlation-id" };

    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpPatch("{**path}")]
    [HttpDelete("{**path}")]
    public Task<IActionResult> Proxy(string path, CancellationToken ct) => ForwardAsync(path, ct);

    private async Task<IActionResult> ForwardAsync(string path, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = $"/api/v1/proxy/{path}";
        var method = new HttpMethod(Request.Method);

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var body = ms.Length > 0 ? ms.ToArray() : null;

        var query = Request.Query.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());

        // Only forward safe, non-sensitive headers from the client.
        var safeHeaders = Request.Headers
            .Where(h => SafeForwardedHeaders.Contains(h.Key))
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
        if (!string.IsNullOrEmpty(Request.ContentType))
        {
            safeHeaders["content-type"] = Request.ContentType;
        }

        try
        {
            var (statusCode, responseBody) = await upstream.ForwardAsync(
                method, path, query.Count > 0 ? query : null, body, safeHeaders, ct);

            var isEmpty = responseBody is null;

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = endpoint,
                HttpMethod = Request.Method,
                Username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername),
                HttpStatusCode = statusCode,
                ResponsePayload = responseBody,
                DetailsFound = isEmpty ? "N" : "Y",
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
            }, ct);

            return Ok(responseBody);
        }
        catch (UpstreamServiceException ex)
        {
            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = endpoint,
                HttpMethod = Request.Method,
                Username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername),
                HttpStatusCode = ex.StatusCode,
                RawResponsePayload = ex.Detail?.ToString(),
                DetailsFound = "N",
                ErrorMessage = ex.Message,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
            }, ct);

            return StatusCode(ex.StatusCode, new { detail = ex.Detail });
        }
    }
}
