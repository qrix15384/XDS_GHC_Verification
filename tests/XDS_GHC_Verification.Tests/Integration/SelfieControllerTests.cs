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

    private async Task<HttpClient> SubscriberLinkedClientAsync(string subscriberName)
    {
        var admin = factory.CreateClient();
        var adminLogin = await TestAuthHelper.LoginAsync(admin, CustomWebApplicationFactory.AuthUsername, CustomWebApplicationFactory.AuthPassword);
        admin.UseBearer(adminLogin.Token);

        var subscriberId = factory.Subscribers.Seed(subscriberName).Id;

        var username = $"subscriber-user-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "tester-pass-123", role = "Standard", subscriberId });

        var login = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "tester-pass-123");
        var client = factory.CreateClient();
        client.UseBearer(login.Token);
        client.DefaultRequestHeaders.Add("X-API-Key", CustomWebApplicationFactory.ApiKey);
        return client;
    }

    [Fact]
    public async Task KycFace_UpstreamFindsMatch_ReturnsMaskedBodyAndLogsFound()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"code":"00","success":true,"msg":"Verified","data":{"person":{"nationalId":"GHA-123456789-0"}}}""",
                Encoding.UTF8, "application/json"),
        });

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("00", body.RootElement.GetProperty("N_StatusCode").GetString());
        Assert.Equal("GHA-123456789-0", body.RootElement.GetProperty("data").GetProperty("person").GetProperty("IDNo").GetString());
        Assert.False(body.RootElement.TryGetProperty("code", out _)); // raw NIA field name must not survive masking

        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal("Y", entry.DetailsFound);
        Assert.Equal("GHA-123456789-0", entry.PinNumber);
    }

    [Fact]
    public async Task KycFace_SuccessWithBirthDate_RunsCreditLookupAndEmbedsRealAddressHistory()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"code":"00","success":true,"data":{"person":{"nationalId":"GHA-717322166-9","birthDate":"2000-04-25"}}}""",
                Encoding.UTF8, "application/json"),
        });
        factory.CreditApiHandler = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = path.Contains("login", StringComparison.OrdinalIgnoreCase)
                ? """{"dataTicket":"test-ticket","message":"sucess","statusCode":200}"""
                : path.Contains("getconsumermatch", StringComparison.OrdinalIgnoreCase)
                    ? """[{"response":{"message":"success","statusCode":200},"matchingEngineID":131510624,"enquiryID":78820369,"consumerID":4902753}]"""
                    : """{"addressHistory":[{"upDateDate":"19/06/2023","upDateOnDate":"19/06/2023","address1":"ATIMATIM","address2":"","address3":"","address4":"","addressTypeInd":"Residential"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        };

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-717322166-9",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body2 = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var history = body2.RootElement.GetProperty("data").GetProperty("person").GetProperty("X_addressHistory");
        Assert.Equal("ATIMATIM", history[0].GetProperty("X_address1").GetString());
        Assert.Equal("Residential", history[0].GetProperty("X_addressTypeInd").GetString());
    }

    [Fact]
    public async Task KycFace_SuccessWithBirthDate_CreditNoMatch_EmbedsNullPlaceholder()
    {
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"code":"00","success":true,"data":{"person":{"nationalId":"GHA-000000000-0","birthDate":"1984-03-15"}}}""",
                Encoding.UTF8, "application/json"),
        });
        factory.CreditApiHandler = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = path.Contains("login", StringComparison.OrdinalIgnoreCase)
                ? """{"dataTicket":"test-ticket","message":"sucess","statusCode":200}"""
                : """[{"response":{"message":"No records found! Thank You!","statusCode":200},"matchingEngineID":null,"enquiryID":null,"consumerID":null}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        };

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-000000000-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body3 = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var history = body3.RootElement.GetProperty("data").GetProperty("person").GetProperty("X_addressHistory");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("X_address1").ValueKind);
    }

    [Fact]
    public async Task KycFace_SuccessWithoutBirthDate_SkipsCreditLookupEntirely()
    {
        var creditApiCalled = false;
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"code":"00","success":true,"data":{"person":{"nationalId":"GHA-123456789-0"}}}""",
                Encoding.UTF8, "application/json"),
        });
        factory.CreditApiHandler = (_, _) =>
        {
            creditApiCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        };

        var response = await AuthorizedClient().PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(creditApiCalled, "no birthDate in the NIA response means there's nothing to key the credit search on");
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

    [Fact]
    public async Task KycFace_CallerBelongsToSubscriber_AttributesAuditLogToThatSubscriber()
    {
        var subscriberName = $"Acme Bank {Guid.NewGuid():N}";
        var client = await SubscriberLinkedClientAsync(subscriberName);
        factory.AuditLog.Entries.Clear();
        factory.UpstreamHandler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00"}""", Encoding.UTF8, "application/json"),
        });

        var response = await client.PostAsJsonAsync("/api/v1/selfie/verification/kyc/face", new
        {
            pinNumber = "GHA-123456789-0",
            image = Convert.ToBase64String("fake-png-bytes"u8.ToArray()),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(factory.AuditLog.Entries);
        Assert.Equal(subscriberName, entry.SubscriberName);
        Assert.NotNull(entry.SubscriberId);
    }
}
