using System.Text.Json.Nodes;

namespace XDS_GHC_Verification.Utils;

/// <summary>Strips image/biometric-blob fields from a JSON tree before it gets logged anywhere.</summary>
public static class JsonRedactor
{
    // "image" — the raw selfie photo callers submit. "N_PtotoData" — the
    // biometric face/signature blob's key name after masking (see
    // VerificationResponseMasker). The raw NIA blob itself is keyed plainly
    // as "data", alongside a sibling "dataType" (e.g.
    // {"dataType":"PNG","data":"<base64>"} for both person.biometricFeed.face
    // and each person.binaries[] entry) — "data" is too generic a name to
    // blanket-redact (it's also the response envelope's own top-level key),
    // so that one is redacted contextually below instead.
    private static readonly HashSet<string> SensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase) { "image", "N_PtotoData" };

    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var isRawBiometricBlobContainer = obj.ContainsKey("dataType") && obj.ContainsKey("data");
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = SensitiveKeys.Contains(key) || (isRawBiometricBlobContainer && key == "data")
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
