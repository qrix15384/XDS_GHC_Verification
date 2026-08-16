using System.Text.Json.Nodes;

namespace XDS_GHC_Verification.Utils;

/// <summary>Strips "image" fields from a JSON tree before it gets logged anywhere.</summary>
public static class JsonRedactor
{
    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = string.Equals(key, "image", StringComparison.OrdinalIgnoreCase)
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
