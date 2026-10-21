namespace XDS_GHC_Verification.Options;

public class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Comma-separated list of allowed origins. Use "*" to allow all — but
    /// don't: the admin console is served same-origin in production, so
    /// same-origin calls never need CORS at all, and leaving this wide open
    /// only gives an arbitrary website the ability to call the authenticated
    /// API cross-origin. Defaults to empty (deny all cross-origin) — set this
    /// explicitly only if a specific external origin legitimately needs it.
    /// </summary>
    public string AllowedOrigins { get; set; } = "";

    public string[] AllowedOriginsList =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
