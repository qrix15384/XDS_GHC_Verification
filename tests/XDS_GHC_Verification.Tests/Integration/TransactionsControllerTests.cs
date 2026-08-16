using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Integration;

public class TransactionsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        var login = await TestAuthHelper.LoginAsync(client, CustomWebApplicationFactory.AuthUsername, CustomWebApplicationFactory.AuthPassword);
        client.UseBearer(login.Token);
        return client;
    }

    private async Task<HttpClient> StandardClientAsync()
    {
        var admin = await AdminClientAsync();
        var username = $"standard-tx-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "standard-pass-123", role = "Standard" });
        var login = await TestAuthHelper.LoginAsync(factory.CreateClient(), username, "standard-pass-123");
        var client = factory.CreateClient();
        client.UseBearer(login.Token);
        return client;
    }

    private static TransactionDetail SeedRow(long id, string? pin = "GHA-123456789-0") => new()
    {
        Id = id,
        RequestId = Guid.NewGuid(),
        RequestAtUtc = DateTime.UtcNow,
        EndpointPath = "/api/v1/selfie/verification/kyc/face",
        HttpMethod = "POST",
        Username = "someone",
        HttpStatusCode = 200,
        DetailsFound = "Y",
        PinNumber = pin,
        ResponsePayload = JsonNode.Parse("""{"code":"00"}"""),
    };

    [Fact]
    public async Task List_NoToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsStandardUser_RedactsPinNumber()
    {
        factory.Transactions.Rows.Clear();
        factory.Transactions.Rows.Add(SeedRow(1));
        var client = await StandardClientAsync();

        var response = await client.GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("pinNumber").ValueKind);
    }

    [Fact]
    public async Task List_AsAdmin_IncludesPinNumber()
    {
        factory.Transactions.Rows.Clear();
        factory.Transactions.Rows.Add(SeedRow(2));
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal("GHA-123456789-0", item.GetProperty("pinNumber").GetString());
    }

    [Fact]
    public async Task GetById_AsStandardUser_ReturnsForbidden()
    {
        factory.Transactions.Rows.Clear();
        factory.Transactions.Rows.Add(SeedRow(3));
        var client = await StandardClientAsync();

        var response = await client.GetAsync("/api/v1/transactions/3");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AsAdmin_ReturnsFullDetailIncludingResponsePayload()
    {
        factory.Transactions.Rows.Clear();
        factory.Transactions.Rows.Add(SeedRow(4));
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/api/v1/transactions/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("00", body.RootElement.GetProperty("responsePayload").GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        factory.Transactions.Rows.Clear();
        for (var i = 1; i <= 5; i++)
        {
            factory.Transactions.Rows.Add(SeedRow(i));
        }
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/api/v1/transactions?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(5, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("items").GetArrayLength());
    }
}
