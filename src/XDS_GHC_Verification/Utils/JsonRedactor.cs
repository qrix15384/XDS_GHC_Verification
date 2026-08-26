using System.Text.Json.Nodes;

namespace XDS_GHC_Verification.Utils;

/// <summary>Strips image/biometric-blob fields from a JSON tree before it gets logged anywhere.</summary>
public static class JsonRedactor
{
    // "image" — the raw selfie photo callers submit. "N_PtotoData" — the masked
    // KYC response's biometric face/signature blobs (see VerificationResponseMasker).
    private static readonly HashSet<string> SensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase) { "image", "N_PtotoData" };

    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = SensitiveKeys.Contains(key)
                        ? "<redacted>"
                        : Redact(value?.DeepClone());
                }
                return result;

            case JsonArray arr:
                var arrResult = new JsonArray();
                foreach (var item in arr)
                {
                    arrResult.Add(Redact(item?.DeepClone()));
                }
                return arrResult;

            default:
                return node;
        }
    }
}
