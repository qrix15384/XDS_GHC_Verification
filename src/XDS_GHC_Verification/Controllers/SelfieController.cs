using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Auth;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Controllers;

/// <summary>
/// Selfie Verification (NIA face-match) endpoints. Both endpoints inject
/// the service's configured merchantKey, center, and userID server-side —
/// callers only supply the Ghana Card pinNumber and image.
///
/// KYC gets its own flow, separate from YES/NO: on a successful match it
/// enriches with an address history lookup from the credit API and returns
/// a masked, restructured response (see VerificationResponseMasker) instead
/// of a raw passthrough. YES/NO stays a plain passthrough — its NIA response
/// carries no birth date, so there's nothing to key the credit lookup on.
///
/// Every call is logged twice, correlated by a per-request RequestId: once to
/// dbo.NiaResponseLog (the original, unmasked NIA response — only when NIA was
/// actually reached) and once to dbo.ProxyResponseLog (whatever this proxy
/// actually sent back to the client, for every response including validation
/// failures that never reach NIA). See sql/005_add_verification_response_logs.sql.
/// </summary>
[ApiController]
[Route("api/v1/selfie")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class SelfieController(
    SelfieVerificationService selfieService,
    ICreditApiClient creditApi,
    IAuditLogService auditLog,
    IVerificationResponseLogService responseLog,
    IOptions<ServiceAuthOptions> authOptions) : ControllerBase
{
    private const string KycEndpoint = "/api/v1/selfie/verification/kyc/face";
    private const string YesNoEndpoint = "/api/v1/selfie/verification/yes_no/face";

    [HttpPost("verification/kyc/face")]
    public async Task<IActionResult> KycFace([FromBody] SelfieVerificationRequest payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid();
        var proxyCallAtUtc = DateTime.UtcNow;
        var username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername);
        var (subscriberId, subscriberName) = HttpContext.ResolveAuditSubscriber();

        string cleanedImage;
        try
        {
            cleanedImage = payload.ValidateAndCleanImage();
        }
        catch (ValidationException ex)
        {
            await LogProxyAsync(requestId, KycEndpoint, payload.PinNumber, proxyCallAtUtc, DateTime.UtcNow,
                StatusCodes.Status422UnprocessableEntity, JsonSerializer.SerializeToNode(new { detail = ex.Message }),
                username, subscriberId, subscriberName, ct);
            return UnprocessableEntity(new { detail = ex.Message });
        }

        var niaCallAtUtc = DateTime.UtcNow;
        try
        {
            var (statusCode, body) = await selfieService.VerifyKycFaceAsync(
                payload.PinNumber, cleanedImage, payload.DataType, payload.UserID, ct);
            var niaResponseAtUtc = DateTime.UtcNow;
            var detailsFound = IsKycDetailsFound(body);

            var addressHistory = detailsFound == "Y"
                ? await LookUpAddressHistoryAsync(payload.PinNumber, body, ct)
                : [AddressHistoryEntry.Empty];

            var maskedResponse = VerificationResponseMasker.MaskKycResponse(body, addressHistory);

            await LogNiaAsync(requestId, KycEndpoint, payload.PinNumber, niaCallAtUtc, niaResponseAtUtc,
                statusCode, body, null, username, subscriberId, subscriberName, ct);
            await LogProxyAsync(requestId, KycEndpoint, payload.PinNumber, proxyCallAtUtc, DateTime.UtcNow,
                statusCode, maskedResponse, username, subscriberId, subscriberName, ct);

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = KycEndpoint,
                HttpMethod = "POST",
                Username = username,
                HttpStatusCode = statusCode,
                ResponsePayload = maskedResponse,
                DetailsFound = detailsFound,
                PinNumber = payload.PinNumber,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                SubscriberId = subscriberId,
                SubscriberName = subscriberName,
            }, ct);

            return Ok(maskedResponse);
        }
        catch (UpstreamServiceException ex)
        {
            var niaResponseAtUtc = DateTime.UtcNow;

            // Failures have no N_/X_ split to make (no credit lookup ever runs
            // here) — every key is uniformly X_-prefixed instead of left as raw
            // NIA field names. The client and the audit log see the same masked shape.
            var maskedDetail = ex.Detail is JsonNode detailNode
                ? VerificationResponseMasker.MaskKycFailureResponse(detailNode)
                : ex.Detail;

            await LogNiaAsync(requestId, KycEndpoint, payload.PinNumber, niaCallAtUtc, niaResponseAtUtc,
                ex.StatusCode, ex.Detail as JsonNode, ex.Detail is JsonNode ? null : ex.Detail?.ToString(),
                username, subscriberId, subscriberName, ct);
            await LogProxyAsync(requestId, KycEndpoint, payload.PinNumber, proxyCallAtUtc, niaResponseAtUtc,
                ex.StatusCode, JsonSerializer.SerializeToNode(new { detail = maskedDetail }),
                username, subscriberId, subscriberName, ct);

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = KycEndpoint,
                HttpMethod = "POST",
                Username = username,
                HttpStatusCode = ex.StatusCode,
                ResponsePayload = maskedDetail as JsonNode,
                RawResponsePayload = maskedDetail is JsonNode ? null : maskedDetail?.ToString(),
                DetailsFound = "N",
                ErrorMessage = ex.Message,
                PinNumber = payload.PinNumber,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                SubscriberId = subscriberId,
                SubscriberName = subscriberName,
            }, ct);

            return StatusCode(ex.StatusCode, new { detail = maskedDetail });
        }
    }

    /// <summary>
    /// Runs the credit API's login -> match -> full-report chain, keyed on
    /// the NIA-confirmed PIN and the birth date NIA returned. Best-effort:
    /// any failure (including "no match") falls back to the all-null
    /// placeholder so the response shape stays consistent and a credit-API
    /// outage never breaks the underlying verification result.
    /// </summary>
    private async Task<List<AddressHistoryEntry>> LookUpAddressHistoryAsync(string pinNumber, JsonNode? niaBody, CancellationToken ct)
    {
        var birthDate = niaBody?["data"]?["person"]?["birthDate"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(birthDate))
        {
            return [AddressHistoryEntry.Empty];
        }

        var match = await creditApi.FindConsumerMatchAsync(pinNumber, birthDate, ct);
        return match is not null
            ? await creditApi.GetAddressHistoryAsync(match, ct)
            : [AddressHistoryEntry.Empty];
    }

    [HttpPost("verification/yes_no/face")]
    public async Task<IActionResult> YesNoFace([FromBody] SelfieVerificationRequest payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid();
        var proxyCallAtUtc = DateTime.UtcNow;
        var username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername);
        var (subscriberId, subscriberName) = HttpContext.ResolveAuditSubscriber();

        string cleanedImage;
        try
        {
            cleanedImage = payload.ValidateAndCleanImage();
        }
        catch (ValidationException ex)
        {
            await LogProxyAsync(requestId, YesNoEndpoint, payload.PinNumber, proxyCallAtUtc, DateTime.UtcNow,
                StatusCodes.Status422UnprocessableEntity, JsonSerializer.SerializeToNode(new { detail = ex.Message }),
                username, subscriberId, subscriberName, ct);
            return UnprocessableEntity(new { detail = ex.Message });
        }

        var niaCallAtUtc = DateTime.UtcNow;
        try
        {
            var (statusCode, body) = await selfieService.VerifyYesNoFaceAsync(
                payload.PinNumber, cleanedImage, payload.DataType, payload.UserID, ct);
            var niaResponseAtUtc = DateTime.UtcNow;

            await LogNiaAsync(requestId, YesNoEndpoint, payload.PinNumber, niaCallAtUtc, niaResponseAtUtc,
                statusCode, body, null, username, subscriberId, subscriberName, ct);
            await LogProxyAsync(requestId, YesNoEndpoint, payload.PinNumber, proxyCallAtUtc, DateTime.UtcNow,
                statusCode, body, username, subscriberId, subscriberName, ct);

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = YesNoEndpoint,
                HttpMethod = "POST",
                Username = username,
                HttpStatusCode = statusCode,
                ResponsePayload = body,
                DetailsFound = IsYesNoDetailsFound(body),
                PinNumber = payload.PinNumber,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                SubscriberId = subscriberId,
                SubscriberName = subscriberName,
            }, ct);

            return Ok(body);
        }
        catch (UpstreamServiceException ex)
        {
            var niaResponseAtUtc = DateTime.UtcNow;

            await LogNiaAsync(requestId, YesNoEndpoint, payload.PinNumber, niaCallAtUtc, niaResponseAtUtc,
                ex.StatusCode, ex.Detail as JsonNode, ex.Detail is JsonNode ? null : ex.Detail?.ToString(),
                username, subscriberId, subscriberName, ct);
            await LogProxyAsync(requestId, YesNoEndpoint, payload.PinNumber, proxyCallAtUtc, niaResponseAtUtc,
                ex.StatusCode, JsonSerializer.SerializeToNode(new { detail = ex.Detail }),
                username, subscriberId, subscriberName, ct);

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = YesNoEndpoint,
                HttpMethod = "POST",
                Username = username,
                HttpStatusCode = ex.StatusCode,
                ResponsePayload = ex.Detail as JsonNode,
                RawResponsePayload = ex.Detail is JsonNode ? null : ex.Detail?.ToString(),
                DetailsFound = "N",
                ErrorMessage = ex.Message,
                PinNumber = payload.PinNumber,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                SubscriberId = subscriberId,
                SubscriberName = subscriberName,
            }, ct);

            return StatusCode(ex.StatusCode, new { detail = ex.Detail });
        }
    }

    /// <summary>
    /// Logs the NIA half of the pair. CallAtUtc/ResponseAtUtc prefer NIA's own
    /// echoed requestTimestamp/responseTimestamp (its own clock, exact
    /// processing moment) and fall back to the times measured locally around
    /// the call when NIA didn't echo one back (e.g. a timeout or a malformed body).
    /// </summary>
    private Task LogNiaAsync(
        Guid requestId, string endpoint, string? pinNumber,
        DateTime measuredCallAtUtc, DateTime measuredResponseAtUtc, int statusCode,
        JsonNode? rawPayload, string? rawText,
        string username, int? subscriberId, string? subscriberName, CancellationToken ct) =>
        responseLog.LogNiaResponseAsync(new NiaResponseLogEntry
        {
            RequestId = requestId,
            EndpointPath = endpoint,
            PinNumber = pinNumber,
            CallAtUtc = ParseNiaTimestamp(rawPayload, "requestTimestamp") ?? measuredCallAtUtc,
            ResponseAtUtc = ParseNiaTimestamp(rawPayload, "responseTimestamp") ?? measuredResponseAtUtc,
            HttpStatusCode = statusCode,
            RawResponsePayload = rawPayload,
            RawResponseText = rawText,
            Username = username,
            SubscriberId = subscriberId,
            SubscriberName = subscriberName,
        }, ct);

    /// <summary>
    /// Logs the proxy half of the pair — CallAtUtc/ResponseAtUtc are always
    /// measured locally, since this is about our own boundary with the client,
    /// not NIA's.
    /// </summary>
    private Task LogProxyAsync(
        Guid requestId, string endpoint, string? pinNumber,
        DateTime callAtUtc, DateTime responseAtUtc, int statusCode, JsonNode? maskedPayload,
        string username, int? subscriberId, string? subscriberName, CancellationToken ct) =>
        responseLog.LogProxyResponseAsync(new ProxyResponseLogEntry
        {
            RequestId = requestId,
            EndpointPath = endpoint,
            PinNumber = pinNumber,
            CallAtUtc = callAtUtc,
            ResponseAtUtc = responseAtUtc,
            HttpStatusCode = statusCode,
            MaskedResponsePayload = maskedPayload,
            Username = username,
            SubscriberId = subscriberId,
            SubscriberName = subscriberName,
        }, ct);

    private static DateTime? ParseNiaTimestamp(JsonNode? body, string key) =>
        body?["data"]?[key] is JsonValue value
        && value.TryGetValue<string>(out var raw)
        && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string IsKycDetailsFound(JsonNode? body)
    {
        var code = body?["code"]?.GetValue<string>();
        var hasPerson = body?["data"]?["person"] is not null;
        return code == "00" && hasPerson ? "Y" : "N";
    }

    private static string IsYesNoDetailsFound(JsonNode? body)
    {
        var verified = body?["data"]?["verified"]?.ToString();
        return verified is not null && new[] { "YES", "Y", "TRUE" }.Contains(verified.Trim().ToUpperInvariant())
            ? "Y"
            : "N";
    }
}
