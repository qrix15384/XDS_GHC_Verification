using System.Net;
using System.Net.Http.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

/// <summary>
/// Exercises ApiKeyAuthFilter through a real protected endpoint (selfie
/// verification) rather than unit-testing the filter in isolation, since its
/// behavior only matters as wired into the request pipeline.
/// </summary>
public class ApiKeyAuthFilterTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static object ValidPayload => new
    {
        pinNumber = "GHA-123456789-0",
        image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
    };

    [Fact]
    public async Task ProtectedEndpoint_MissingApiKeyHeader_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", ValidPayload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WrongApiKey_ReturnsForbidden()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "not-the-real-key");

        var response = await client.PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", ValidPayload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_CorrectApiKey_PassesThroughToController()
    {
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"01"}""", System.Text.Encoding.UTF8, "application/json"),
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", CustomWebApplicationFactory.ApiKey);

        var response = await client.PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", ValidPayload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
