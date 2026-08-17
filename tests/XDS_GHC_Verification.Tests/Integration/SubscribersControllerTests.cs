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
    public async Task Create_ThenList_ReturnsTheNewSubscriber()
    {
        var admin = await AdminClientAsync();
        var name = $"Acme Bank {Guid.NewGuid():N}";

        var createResponse = await admin.PostAsJsonAsync("/api/v1/subscribers", new { name });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var listResponse = await admin.GetAsync("/api/v1/subscribers");
        var body = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Contains(body.RootElement.EnumerateArray(), s => s.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var admin = await AdminClientAsync();
        var name = $"Dup Subscriber {Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/subscribers", new { name });

        var response = await admin.PostAsJsonAsync("/api/v1/subscribers", new { name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
    public async Task Delete_SubscriberWithAssignedUsers_ReturnsBadRequest()
    {
        var admin = await AdminClientAsync();
        var subscriberName = $"Has Users {Guid.NewGuid():N}";
        var createResponse = await admin.PostAsJsonAsync("/api/v1/subscribers", new { name = subscriberName });
        var subscriber = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var subscriberId = subscriber.RootElement.GetProperty("id").GetInt32();

        await admin.PostAsJsonAsync("/api/v1/users", new
        {
            username = $"assigned-user-{Guid.NewGuid():N}",
            password = "password-123",
            role = "Standard",
            subscriberId,
        });

        var response = await admin.DeleteAsync($"/api/v1/subscribers/{subscriberId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SubscriberWithNoUsers_Succeeds()
    {
        var admin = await AdminClientAsync();
        var createResponse = await admin.PostAsJsonAsync("/api/v1/subscribers", new { name = $"No Users {Guid.NewGuid():N}" });
        var subscriber = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var subscriberId = subscriber.RootElement.GetProperty("id").GetInt32();

        var response = await admin.DeleteAsync($"/api/v1/subscribers/{subscriberId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
        var admin = await AdminClientAsync();
        var subscriberName = $"Valid Sub {Guid.NewGuid():N}";
        var createSubResponse = await admin.PostAsJsonAsync("/api/v1/subscribers", new { name = subscriberName });
        var subscriber = JsonDocument.Parse(await createSubResponse.Content.ReadAsStringAsync());
        var subscriberId = subscriber.RootElement.GetProperty("id").GetInt32();

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            username = $"with-subscriber-{Guid.NewGuid():N}",
            password = "password-123",
            role = "Standard",
            subscriberId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(subscriberId, body.RootElement.GetProperty("subscriberId").GetInt32());
        Assert.Equal(subscriberName, body.RootElement.GetProperty("subscriberName").GetString());
    }
}
