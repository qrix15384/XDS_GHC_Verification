using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class SubscribersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        var login = await TestAuthHelper.LoginAsync(client, CustomWebApplicationFactory.AuthUsername, CustomWebApplicationFactory.AuthPassword);
        client.UseBearer(login.Token);
        return client;
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsSeededSubscribers()
    {
        var seeded = factory.Subscribers.Seed($"Acme Bank {Guid.NewGuid():N}");
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/subscribers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(body.RootElement.EnumerateArray(), s => s.GetProperty("name").GetString() == seeded.Name);
    }

    [Fact]
    public async Task List_NoToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/subscribers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsStandardUser_ReturnsForbidden()
    {
        var admin = await AdminClientAsync();
        var username = $"standard-sub-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "standard-pass-123", role = "Standard" });
        var login = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "standard-pass-123");
        var client = factory.CreateClient();
        client.UseBearer(login.Token);

        var response = await client.GetAsync("/api/v1/subscribers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_KnownSubscriber_ReturnsIt()
    {
        var seeded = factory.Subscribers.Seed($"Known Sub {Guid.NewGuid():N}");
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/subscribers/{seeded.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(seeded.Name, body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetById_UnknownSubscriber_ReturnsNotFound()
    {
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/subscribers/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithUnknownSubscriberId_ReturnsBadRequest()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            username = $"bad-subscriber-{Guid.NewGuid():N}",
            password = "password-123",
            role = "Standard",
            subscriberId = 999_999,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithValidSubscriberId_ReturnsUserWithSubscriberName()
    {
        var seeded = factory.Subscribers.Seed($"Valid Sub {Guid.NewGuid():N}");
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            username = $"with-subscriber-{Guid.NewGuid():N}",
            password = "password-123",
            role = "Standard",
            subscriberId = seeded.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(seeded.Id, body.RootElement.GetProperty("subscriberId").GetInt32());
        Assert.Equal(seeded.Name, body.RootElement.GetProperty("subscriberName").GetString());
    }
}
