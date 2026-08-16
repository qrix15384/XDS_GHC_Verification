using System.ComponentModel.DataAnnotations;

namespace XDS_GHC_Verification.Models;

/// <summary>Request body for both the KYC and YES/NO face verification endpoints.</summary>
public class SelfieVerificationRequest
{
    private const int MaxImageBytes = 1 * 1024 * 1024; // 1MB, per vendor docs

    /// <summary>Ghana Card PIN, format GHA-xxxxxxxxx-x.</summary>
    [Required]
    public string PinNumber { get; set; } = "";

    /// <summary>
    /// Base64-encoded live selfie image. Must be PNG, at least 640x480, and
    /// under 1MB — larger images may time out or be blocked by the NIA firewall.
    /// </summary>
    [Required]
    public string Image { get; set; } = "";

    /// <summary>Image encoding. Only PNG is supported by the upstream API.</summary>
    public string DataType { get; set; } = "PNG";

    /// <summary>Optional caller identifier. Defaults to this service's configured Selfie:UserId.</summary>
    public string? UserID { get; set; }

    /// <summary>
    /// Strips any data URL prefix and validates the image is well-formed
    /// Base64 under the vendor's size limit. Returns the cleaned Base64
    /// string, or throws <see cref="ValidationException"/> if invalid.
    /// </summary>
    public string ValidateAndCleanImage()
    {
        var cleaned = Image.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? Image[(Image.IndexOf(',') + 1)..]
            : Image;

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(cleaned);
        }
        catch (FormatException)
        {
            throw new ValidationException("image must be a valid Base64-encoded string");
        }

        if (decoded.Length > MaxImageBytes)
        {
            throw new ValidationException(
                $"image is {decoded.Length} bytes; the upstream API requires it to be under {MaxImageBytes} bytes (1MB)");
        }

        return cleaned;
    }
}
