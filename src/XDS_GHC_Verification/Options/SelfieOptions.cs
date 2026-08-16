namespace XDS_GHC_Verification.Options;

/// <summary>
/// Injected server-side into every selfie verification request so callers
/// of this proxy never need to know the merchant key.
/// </summary>
public class SelfieOptions
{
    public const string SectionName = "Selfie";

    public string MerchantKey { get; set; } = "change-me-merchant-key";
    public string Center { get; set; } = "BRANCHLESS";
    public string UserId { get; set; } = "change-me-user-id";
}
