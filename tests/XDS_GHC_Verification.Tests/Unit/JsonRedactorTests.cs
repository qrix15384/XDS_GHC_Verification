using System.Text.Json.Nodes;
using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Tests.Unit;

public class JsonRedactorTests
{
    [Fact]
    public void Redact_TopLevelImageField_IsRedacted()
    {
        var node = JsonNode.Parse("""{"pinNumber":"GHA-123","image":"aGVsbG8="}""");

        var result = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", result!["image"]!.GetValue<string>());
        Assert.Equal("GHA-123", result["pinNumber"]!.GetValue<string>());
    }

    [Fact]
    public void Redact_NestedImageField_IsRedacted()
    {
        var node = JsonNode.Parse("""{"data":{"person":{"image":"aGVsbG8=","name":"Kwame"}}}""");

        var result = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", result!["data"]!["person"]!["image"]!.GetValue<string>());
        Assert.Equal("Kwame", result["data"]!["person"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Redact_ImageFieldInArray_IsRedacted()
    {
        var node = JsonNode.Parse("""[{"image":"one"},{"image":"two"}]""");

        var result = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", result![0]!["image"]!.GetValue<string>());
        Assert.Equal("<redacted>", result[1]!["image"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("image")]
    [InlineData("Image")]
    [InlineData("IMAGE")]
    public void Redact_IsCaseInsensitiveOnKeyName(string key)
    {
        var node = new JsonObject { [key] = "sensitive-bytes" };

        var result = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", result![key]!.GetValue<string>());
    }

    [Fact]
    public void Redact_NonObjectOrArray_ReturnsUnchanged()
    {
        JsonNode? node = JsonValue.Create("plain-string");

        var result = JsonRedactor.Redact(node);

        Assert.Equal("plain-string", result!.GetValue<string>());
    }

    [Fact]
    public void Redact_Null_ReturnsNull()
    {
        Assert.Null(JsonRedactor.Redact(null));
    }

    [Fact]
    public void Redact_RawNiaPtotoData_IsRedacted()
    {
        // "ptotoData" is the raw NIA biometric blob key, before VerificationResponseMasker
        // renames it to "N_PtotoData" — the raw form must be caught too, since
        // NiaResponseLog now persists the unmasked response.
        var node = JsonNode.Parse("""{"binaries":[{"type":"FACE","ptotoData":"base64-blob-data"}]}""")!;

        var redacted = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", redacted?["binaries"]?[0]?["ptotoData"]?.GetValue<string>());
    }

    [Fact]
    public void Redact_MaskedPtotoData_IsStillRedacted()
    {
        var node = JsonNode.Parse("""{"person":{"biometricFeed":{"face":{"N_PtotoData":"base64-blob-data"}}}}""")!;

        var redacted = JsonRedactor.Redact(node);

        Assert.Equal("<redacted>", redacted?["person"]?["biometricFeed"]?["face"]?["N_PtotoData"]?.GetValue<string>());
    }
}
