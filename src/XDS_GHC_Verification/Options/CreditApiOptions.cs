namespace XDS_GHC_Verification.Options;

/// <summary>
/// Connection details for the internal XDS Data credit/consumer-matching
/// API used to enrich a successful selfie verification with address
/// history. Login credentials for that system — not the same as anything
/// in ServiceAuth, Selfie, or Upstream.
/// </summary>
public class CreditApiOptions
{
    public const string SectionName = "CreditApi";

    public string BaseUrl { get; set; } = "https://www.online.xdsdata.com/XdsDataRestApi";
    public string Username { get; set; } = "change-me-credit-api-username";
    public string Password { get; set; } = "change-me-credit-api-password";
    public double TimeoutSeconds { get; set; } = 30.0;
}
