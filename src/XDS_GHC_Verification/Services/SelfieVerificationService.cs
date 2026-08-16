using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Services;

/// <summary>
/// Client for the upstream Selfie Verification (NIA face-match) API.
/// Injects the service's configured merchant key, center, and userID into
/// every request so callers of this proxy never need to know the merchant key.
/// </summary>
public class SelfieVerificationService(UpstreamClient upstream, IOptions<SelfieOptions> selfieOptions)
{
    private const string KycPath = "api/v1/third-party/verification/base_64/verification/kyc/face";
    private const string YesNoPath = "api/v1/third-party/verification/yes_no/face";

    private readonly SelfieOptions _selfie = selfieOptions.Value;

    public Task<(int StatusCode, JsonNode? Body)> VerifyKycFaceAsync(
        string pinNumber, string image, string dataType, string? userId, CancellationToken ct) =>
        PostFaceVerificationAsync(KycPath, pinNumber, image, dataType, userId, ct);

    public Task<(int StatusCode, JsonNode? Body)> VerifyYesNoFaceAsync(
        string pinNumber, string image, string dataType, string? userId, CancellationToken ct) =>
        PostFaceVerificationAsync(YesNoPath, pinNumber, image, dataType, userId, ct);

    private Task<(int StatusCode, JsonNode? Body)> PostFaceVerificationAsync(
        string path, string pinNumber, string image, string dataType, string? userId, CancellationToken ct)
    {
        var payload = new
        {
            pinNumber,
            image,
            dataType,
            center = _selfie.Center,
            userID = string.IsNullOrWhiteSpace(userId) ? _selfie.UserId : userId,
            merchantKey = _selfie.MerchantKey,
        };

        return upstream.PostJsonAsync(path, payload, ct);
    }
}
