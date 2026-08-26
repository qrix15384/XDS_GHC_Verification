using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
/// </summary>
[ApiController]
[Route("api/v1/selfie")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class SelfieController(
    SelfieVerificationService selfieService,
    ICreditApiClient creditApi,
    IAuditLogService auditLog,
    IOptions<ServiceAuthOptions> authOptions) : ControllerBase
{
    private const string KycEndpoint = "/api/v1/selfie/verification/kyc/face";
    private const string YesNoEndpoint = "/api/v1/selfie/verification/yes_no/face";

    [HttpPost("verification/kyc/face")]
    public async Task<IActionResult> KycFace([FromBody] SelfieVerificationRequest payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername);
        var (subscriberId, subscriberName) = HttpContext.ResolveAuditSubscriber();

        string cleanedImage;
        try
        {
            cleanedImage = payload.ValidateAndCleanImage();
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(new { detail = ex.Message });
        }

        try
        {
            var (statusCode, body) = await selfieService.VerifyKycFaceAsync(
                payload.PinNumber, cleanedImage, payload.DataType, payload.UserID, ct);
            var detailsFound = IsKycDetailsFound(body);

            var addressHistory = detailsFound == "Y"
                ? await LookUpAddressHistoryAsync(payload.PinNumber, body, ct)
                : [AddressHistoryEntry.Empty];

            var maskedResponse = VerificationResponseMasker.MaskKycResponse(body, addressHistory);

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
            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = KycEndpoint,
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
        var username = HttpContext.ResolveAuditUsername(authOptions.Value.AuthUsername);
        var (subscriberId, subscriberName) = HttpContext.ResolveAuditSubscriber();

        string cleanedImage;
        try
        {
            cleanedImage = payload.ValidateAndCleanImage();
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(new { detail = ex.Message });
        }

        try
        {
            var (statusCode, body) = await selfieService.VerifyYesNoFaceAsync(
                payload.PinNumber, cleanedImage, payload.DataType, payload.UserID, ct);

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
