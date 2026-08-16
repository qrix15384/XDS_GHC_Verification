using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Tests.Unit;

public class SecureCompareTests
{
    [Fact]
    public void Equals_SameString_ReturnsTrue()
    {
        Assert.True(SecureCompare.Equals("my-secret-key", "my-secret-key"));
    }

    [Fact]
    public void Equals_BothEmpty_ReturnsTrue()
    {
        Assert.True(SecureCompare.Equals("", ""));
    }

    [Fact]
    public void Equals_DifferentContentSameLength_ReturnsFalse()
    {
        Assert.False(SecureCompare.Equals("abcdefgh", "abcdefgx"));
    }

    [Fact]
    public void Equals_DifferentLength_ReturnsFalse()
    {
        Assert.False(SecureCompare.Equals("short", "a-much-longer-value"));
    }

    [Fact]
    public void Equals_IsCaseSensitive()
    {
        Assert.False(SecureCompare.Equals("Secret", "secret"));
    }
}
