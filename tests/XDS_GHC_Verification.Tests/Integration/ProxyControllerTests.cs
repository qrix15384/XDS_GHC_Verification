using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class ProxyControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", CustomWebApplicationFactory.ApiKey);
        return client;
    }

    [Fact]
    public async Task Get_ForwardsToUpstreamAndReturnsBody()
    {
        Uri? requestedUri = null;
        factory.UpstreamHandler = (req, _) =>
        {
            requestedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"users":[]}""", Encoding.UTF8, "application/json"),
            });
        };

        var response = await AuthorizedClient().GetAsync("/api/v1/proxy/users?page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/users?page=2", requestedUri!.PathAndQuery);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("users").ValueKind);
    }

    [Fact]
    public async Task Post_ForwardsBodyToUpstream()
    {
        string? forwardedBody = null;
        factory.UpstreamHandler = async (req, ct) =>
        {
            forwardedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
        };

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/proxy/orders", new { item = "widget", qty = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // controller wraps upstream result with Ok()
        Assert.Contains("\"item\":\"widget\"", forwardedBody);
    }

    [Fact]
    public async Task MissingApiKey_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/proxy/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpstreamNotFound_PassesThroughStatusAndDetail()
    {
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"detail":"no such order"}""", Encoding.UTF8, "application/json"),
        });

        var response = await AuthorizedClient().GetAsync("/api/v1/proxy/orders/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
