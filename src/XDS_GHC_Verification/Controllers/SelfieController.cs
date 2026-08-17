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
/// </summary>
[ApiController]
[Route("api/v1/selfie")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class SelfieController(
    SelfieVerificationService selfieService,
    IAuditLogService auditLog,
    IOptions<ServiceAuthOptions> authOptions) : ControllerBase
{
    private const string KycEndpoint = "/api/v1/selfie/verification/kyc/face";
    private const string YesNoEndpoint = "/api/v1/selfie/verification/yes_no/face";

    [HttpPost("verification/kyc/face")]
    public Task<IActionResult> KycFace([FromBody] SelfieVerificationRequest payload, CancellationToken ct) =>
        VerifyAndLogAsync(KycEndpoint, payload, selfieService.VerifyKycFaceAsync, IsKycDetailsFound, ct);

    [HttpPost("verification/yes_no/face")]
    public Task<IActionResult> YesNoFace([FromBody] SelfieVerificationRequest payload, CancellationToken ct) =>
        VerifyAndLogAsync(YesNoEndpoint, payload, selfieService.VerifyYesNoFaceAsync, IsYesNoDetailsFound, ct);

    private async Task<IActionResult> VerifyAndLogAsync(
        string endpoint,
        SelfieVerificationRequest payload,
        Func<string, string, string, string?, CancellationToken, Task<(int StatusCode, JsonNode? Body)>> verify,
        Func<JsonNode?, string> detailsFoundFn,
        CancellationToken ct)
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
            var (statusCode, body) = await verify(payload.PinNumber, cleanedImage, payload.DataType, payload.UserID, ct);

            await auditLog.LogTransactionAsync(new AuditLogEntry
            {
                EndpointPath = endpoint,
                HttpMethod = "POST",
                Username = username,
                HttpStatusCode = statusCode,
                ResponsePayload = body,
                DetailsFound = detailsFoundFn(body),
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
                EndpointPath = endpoint,
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
