using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class ProxyUsersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        var login = await TestAuthHelper.LoginAsync(client, CustomWebApplicationFactory.AuthUsername, CustomWebApplicationFactory.AuthPassword);
        client.UseBearer(login.Token);
        return client;
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsSeededAdmin()
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(body.RootElement.EnumerateArray(),
            u => u.GetProperty("username").GetString() == CustomWebApplicationFactory.AuthUsername);
    }

    [Fact]
    public async Task List_NoToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsStandardUser_ReturnsForbidden()
    {
        var admin = await AdminClientAsync();
        var username = $"standard-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "standard-pass-123", role = "Standard" });
        var standardLogin = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "standard-pass-123");
        var standardClient = factory.CreateClient();
        standardClient.UseBearer(standardLogin.Token);

        var response = await standardClient.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateUsername_ReturnsConflict()
    {
        var admin = await AdminClientAsync();
        var username = $"dup-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "password-123", role = "Standard" });

        var response = await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "password-456", role = "Standard" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidRole_ReturnsBadRequest()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new { username = $"bad-role-{Guid.NewGuid():N}", password = "password-123", role = "SuperUser" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_SelfDemote_ReturnsBadRequest()
    {
        var admin = await AdminClientAsync();
        var listResponse = await admin.GetAsync("/api/v1/users");
        var users = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var selfId = users.RootElement.EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == CustomWebApplicationFactory.AuthUsername)
            .GetProperty("id").GetInt32();

        var response = await admin.PutAsJsonAsync($"/api/v1/users/{selfId}", new { role = "Standard", isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Self_ReturnsBadRequest()
    {
        var admin = await AdminClientAsync();
        var listResponse = await admin.GetAsync("/api/v1/users");
        var users = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var selfId = users.RootElement.EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == CustomWebApplicationFactory.AuthUsername)
            .GetProperty("id").GetInt32();

        var response = await admin.DeleteAsync($"/api/v1/users/{selfId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_AnotherAdmin_Succeeds()
    {
        var admin = await AdminClientAsync();
        var username = $"second-admin-{Guid.NewGuid():N}";
        var createResponse = await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "password-123", role = "Admin" });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var newAdminId = created.RootElement.GetProperty("id").GetInt32();

        var response = await admin.PutAsJsonAsync($"/api/v1/users/{newAdminId}", new { role = "Standard", isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_AllowsLoginWithNewPassword()
    {
        var admin = await AdminClientAsync();
        var username = $"reset-me-{Guid.NewGuid():N}";
        var createResponse = await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "old-password-123", role = "Standard" });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var userId = created.RootElement.GetProperty("id").GetInt32();

        var resetResponse = await admin.PostAsJsonAsync($"/api/v1/users/{userId}/reset-password", new { newPassword = "new-password-456" });
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var login = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "new-password-456");
        Assert.Equal("Standard", login.Role);
    }
}
