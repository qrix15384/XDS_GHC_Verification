namespace XDS_GHC_Verification.Options;

/// <summary>
/// Credentials clients use to call this service: a single shared API key,
/// obtainable either directly or by exchanging a username/password via
/// POST /api/v1/auth/login.
/// </summary>
public class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public string ApiKey { get; set; } = "change-me-service-key";
    public string AuthUsername { get; set; } = "change-me-username";
    public string AuthPassword { get; set; } = "change-me-password";
}
