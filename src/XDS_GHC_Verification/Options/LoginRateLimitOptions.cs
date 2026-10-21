namespace XDS_GHC_Verification.Options;

/// <summary>
/// Login brute-force protection — how many /api/v1/auth/login attempts a
/// single client IP may make before being rejected with 429 until the window
/// rolls over. No queueing: once the limit is hit, requests fail immediately
/// rather than piling up.
/// </summary>
public class LoginRateLimitOptions
{
    public const string SectionName = "LoginRateLimit";

    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}
