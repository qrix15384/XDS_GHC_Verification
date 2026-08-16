using System.Security.Cryptography;
using System.Text;

namespace XDS_GHC_Verification.Utils;

/// <summary>Constant-time string comparison, for credential/API-key checks.</summary>
public static class SecureCompare
{
    public static bool Equals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        var maxLen = Math.Max(aBytes.Length, bBytes.Length);
        Array.Resize(ref aBytes, maxLen);
        Array.Resize(ref bBytes, maxLen);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes) && a.Length == b.Length;
    }
}
