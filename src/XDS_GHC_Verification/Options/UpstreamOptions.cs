namespace XDS_GHC_Verification.Options;

/// <summary>Connection details for the private upstream API this service proxies to.</summary>
public class UpstreamOptions
{
    public const string SectionName = "Upstream";

    public string BaseUrl { get; set; } = "https://selfie.imsgh.org:2035/skyface";

    /// <summary>"ApiKey" | "Bearer" | "Basic" | "None".</summary>
    public string AuthType { get; set; } = "None";

    public string ApiKey { get; set; } = "change-me-upstream-key";
    public string ApiKeyHeader { get; set; } = "X-API-Key";
    public string BasicUsername { get; set; } = "";
    public string BasicPassword { get; set; } = "";
    public double TimeoutSeconds { get; set; } = 30.0;
}
