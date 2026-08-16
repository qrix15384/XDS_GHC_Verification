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
}
