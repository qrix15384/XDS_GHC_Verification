namespace XDS_GHC_Verification.Options;

/// <summary>
/// Signing configuration for the per-user JWT issued at login. Kept separate
/// from ServiceAuthOptions since it's an independent secret with its own
/// rotation — it authorizes the new admin endpoints (user management,
/// transactions), not the shared X-API-Key used by external API clients.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Symmetric signing key, HMAC-SHA256. Must be at least 32 characters.</summary>
    public string SigningKey { get; set; } = "change-me-jwt-signing-key-at-least-32-characters-long";

    public string Issuer { get; set; } = "XDS_GHC_Verification";
    public string Audience { get; set; } = "XDS_GHC_Verification";
    public int ExpiryMinutes { get; set; } = 480;
}
