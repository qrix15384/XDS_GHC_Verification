using System.ComponentModel.DataAnnotations;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Tests.Unit;

public class SelfieVerificationRequestTests
{
    private static string SmallValidBase64 => Convert.ToBase64String("not-really-a-png"u8.ToArray());

    [Fact]
    public void ValidateAndCleanImage_PlainBase64_ReturnsUnchanged()
    {
        var request = new SelfieVerificationRequest { PinNumber = "GHA-123456789-0", Image = SmallValidBase64 };

        var cleaned = request.ValidateAndCleanImage();

        Assert.Equal(SmallValidBase64, cleaned);
    }

    [Fact]
    public void ValidateAndCleanImage_DataUrlPrefix_IsStripped()
    {
        var request = new SelfieVerificationRequest
        {
            PinNumber = "GHA-123456789-0",
            Image = $"data:image/png;base64,{SmallValidBase64}",
        };

        var cleaned = request.ValidateAndCleanImage();

        Assert.Equal(SmallValidBase64, cleaned);
    }

    [Fact]
    public void ValidateAndCleanImage_InvalidBase64_ThrowsValidationException()
    {
        var request = new SelfieVerificationRequest { PinNumber = "GHA-123456789-0", Image = "not-valid-base64!!!" };

        Assert.Throws<ValidationException>(() => request.ValidateAndCleanImage());
    }

    [Fact]
    public void ValidateAndCleanImage_OverOneMegabyte_ThrowsValidationException()
    {
        var oversized = new byte[1024 * 1024 + 1];
        var request = new SelfieVerificationRequest
        {
            PinNumber = "GHA-123456789-0",
            Image = Convert.ToBase64String(oversized),
        };

        var ex = Assert.Throws<ValidationException>(() => request.ValidateAndCleanImage());
        Assert.Contains("1MB", ex.Message);
    }
}
