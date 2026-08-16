namespace XDS_GHC_Verification.Options;

public class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Comma-separated list of allowed origins. Use "*" to allow all.</summary>
    public string AllowedOrigins { get; set; } = "*";

    public string[] AllowedOriginsList =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
