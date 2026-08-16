using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class SelfieControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    /// <summary>
    /// Creates and logs in as a distinct Standard user (deliberately NOT the
    /// seeded admin, whose username happens to equal the ServiceAuth fallback
    /// — using it here would make the attribution test pass even if the
    /// fallback were used by mistake).
    /// </summary>
    private async Task<(HttpClient Client, string Username)> DistinctUserClientAsync()
    {
        var admin = factory.CreateClient();
        var adminLogin = await TestAuthHelper.LoginAsync(admin, CustomWebApplicationFactory.AuthUsername, CustomWebApplicationFactory.AuthPassword);
        admin.UseBearer(adminLogin.Token);
        var username = $"attributed-tester-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "tester-pass-123", role = "Standard" });

        var login = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "tester-pass-123");
        var client = factory.CreateClient();
        client.UseBearer(login.Token);
        client.DefaultRequestHeaders.Add("X-API-Key", CustomWebApplicationFactory.ApiKey);
        return (client, username);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", CustomWebApplicationFactory.ApiKey);
        return client;
    }

    [Fact]
    public async Task KycFace_UpstreamFindsMatch_ReturnsUpstreamBodyAndLogsFound()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00","data":{"person":{"name":"Kwame"}}}""", Encoding.UTF8, "application/json"),
        });

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("00", body.RootElement.GetProperty("code").GetString());

        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal("Y", entry.DetailsFound);
        Assert.Equal("GHA-123456789-0", entry.PinNumber);
    }

    [Fact]
    public async Task KycFace_UpstreamRequest_InjectsMerchantKeyAndCenterServerSide()
    {
        JsonDocument? sentBody = null;
        factory.UpstreamHandler = async (req, ct) =>
        {
            var text = await req.Content!.ReadAsStringAsync(ct);
            sentBody = JsonDocument.Parse(text);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":"00"}""", Encoding.UTF8, "application/json"),
            };
        };

        await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(CustomWebApplicationFactory.SelfieMerchantKey, sentBody!.RootElement.GetProperty("merchantKey").GetString());
        Assert.Equal(CustomWebApplicationFactory.SelfieUserId, sentBody.RootElement.GetProperty("userID").GetString());
        Assert.Equal("BRANCHLESS", sentBody.RootElement.GetProperty("center").GetString());
    }

    [Fact]
    public async Task KycFace_InvalidBase64Image_ReturnsUnprocessableEntity()
    {
        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = "not-valid-base64!!!",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task YesNoFace_UpstreamSaysVerified_ReturnsOkAndLogsFound()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00","data":{"verified":"YES"}}""", Encoding.UTF8, "application/json"),
        });

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/yes_no/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Y", Assert.Single(factory.AuditLog.Entries).DetailsFound);
    }

    [Fact]
    public async Task KycFace_UpstreamServerError_ReturnsBadGatewayAndLogsError()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal("N", entry.DetailsFound);
        Assert.NotNull(entry.ErrorMessage);
    }

    [Fact]
    public async Task KycFace_WithBearerToken_AttributesAuditLogToRealUser()
    {
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00"}""", Encoding.UTF8, "application/json"),
        });
        var (client, username) = await DistinctUserClientAsync();
        factory.AuditLog.Entries.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal(username, entry.Username);
        Assert.NotEqual(CustomWebApplicationFactory.AuthUsername, entry.Username);
    }

    [Fact]
    public async Task KycFace_ApiKeyOnlyNoBearerToken_AttributesAuditLogToFallbackUsername()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00"}""", Encoding.UTF8, "application/json"),
        });

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal(CustomWebApplicationFactory.AuthUsername, entry.Username);
    }
}
