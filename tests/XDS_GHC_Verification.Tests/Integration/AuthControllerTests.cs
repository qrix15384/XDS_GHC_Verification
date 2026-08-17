using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class AuthControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Login_ValidCredentials_ReturnsServiceApiKey()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = CustomWebApplicationFactory.AuthUsername,
            password = CustomWebApplicationFactory.AuthPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CustomWebApplicationFactory.ApiKey, body.RootElement.GetProperty("apiKey").GetString());
        Assert.Equal("apikey", body.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal("Admin", body.RootElement.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("token").GetString()));
        Assert.True(body.RootElement.GetProperty("expiresAtUtc").GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = CustomWebApplicationFactory.AuthUsername,
            password = "not-the-right-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUsername_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "someone-else",
            password = CustomWebApplicationFactory.AuthPassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_MissingUsername_ReturnsBadRequestFromModelValidation()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = "whatever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_WritesFailedAuditLogEntry()
    {
        factory.AuditLog.Entries.Clear();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = CustomWebApplicationFactory.AuthUsername,
            password = "wrong",
        });

        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal("N", entry.DetailsFound);
        Assert.Equal(401, entry.HttpStatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsUnauthorized()
    {
        var created = await factory.ProxyUsers.CreateAsync(
            $"inactive-{Guid.NewGuid():N}", new Microsoft.AspNetCore.Identity.PasswordHasher<XDS_GHC_Verification.Models.ProxyUser>()
                .HashPassword(new XDS_GHC_Verification.Models.ProxyUser(), "some-password-123"),
            "Standard", subscriberId: null);
        await factory.ProxyUsers.UpdateRoleAndStatusAsync(created.Id, "Standard", isActive: false, subscriberId: null);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = created.Username,
            password = "some-password-123",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
