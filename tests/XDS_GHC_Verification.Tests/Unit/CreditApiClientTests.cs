using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Unit;

public class CreditApiClientTests
{
    // Response bodies below are transcribed verbatim from real, live calls made
    // during integration testing — not fabricated.
    private const string RealLoginResponse = """
        {"dataTicket":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test","message":"sucess","statusCode":200}
        """;

    private const string RealNoMatchResponse = """
        [{"response":{"message":"No records found! Thank You!","statusCode":200},"matchingEngineID":null,"enquiryID":null,"consumerID":null,"reference":null,"idNo":null,"passportNo":null,"socialSecurityNo":null,"voterIDNo":null,"driversLicenseNo":null,"firstName":null,"secondName":null,"surname":null,"otherNames":null,"address":null,"birthDate":null,"genderInd":null,"accountNo":null}]
        """;

    private const string RealMatchFoundResponse = """
        [{"response":{"message":"success","statusCode":200},"matchingEngineID":131510624,"enquiryID":78820369,"consumerID":4902753,"reference":"C78820369-4902753","idNo":"GHA7173221669","passportNo":"","socialSecurityNo":"","voterIDNo":"7173221669","driversLicenseNo":"","firstName":"KINGSLEY","secondName":"","surname":"OKYERE","otherNames":"","address":"ATIMATIM","birthDate":"2000/04/25 12:00:00 AM","genderInd":"","accountNo":""}]
        """;

    private const string RealFullReportResponse = """
        {"response":{"message":"Success","statusCode":200},"addressHistory":[{"upDateDate":"19/06/2023","upDateOnDate":"19/06/2023","address1":"ATIMATIM","address2":"","address3":"","address4":"","addressTypeInd":"Residential"}]}
        """;

    private static CreditApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://credit-api.test/") };
        var options = Microsoft.Extensions.Options.Options.Create(new CreditApiOptions
        {
            BaseUrl = "https://credit-api.test/",
            Username = "test-user",
            Password = "test-pass",
        });
        return new CreditApiClient(httpClient, options, NullLogger<CreditApiClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task FindConsumerMatchAsync_NoMatch_ReturnsResultWithIsMatchFalse()
    {
        var client = CreateClient((req, _) =>
        {
            var response = req.RequestUri!.AbsolutePath.Contains("login")
                ? JsonResponse(HttpStatusCode.OK, RealLoginResponse)
                : JsonResponse(HttpStatusCode.OK, RealNoMatchResponse);
            return Task.FromResult(response);
        });

        var result = await client.FindConsumerMatchAsync("GHA-000000000-0", "1984-03-15", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsMatch);
        Assert.Null(result.MatchingEngineId);
    }

    [Fact]
    public async Task FindConsumerMatchAsync_Match_ReturnsResultWithIsMatchTrue()
    {
        var client = CreateClient((req, _) =>
        {
            var response = req.RequestUri!.AbsolutePath.Contains("login")
                ? JsonResponse(HttpStatusCode.OK, RealLoginResponse)
                : JsonResponse(HttpStatusCode.OK, RealMatchFoundResponse);
            return Task.FromResult(response);
        });

        var result = await client.FindConsumerMatchAsync("GHA-717322166-9", "2000-04-25", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsMatch);
        Assert.Equal(131510624, result.MatchingEngineId);
        Assert.Equal(78820369, result.EnquiryId);
        Assert.Equal(4902753, result.ConsumerId);
    }

    [Fact]
    public async Task FindConsumerMatchAsync_LoginFails_ReturnsNull()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.FindConsumerMatchAsync("GHA-000000000-0", "1984-03-15", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAddressHistoryAsync_NoMatch_ReturnsAllNullPlaceholder()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var noMatch = new Models.ConsumerMatchResult();

        var history = await client.GetAddressHistoryAsync(noMatch, CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Null(entry.Address1);
        Assert.Null(entry.AddressTypeInd);
    }

    [Fact]
    public async Task GetAddressHistoryAsync_Match_ReturnsRealAddressHistory()
    {
        var client = CreateClient((req, _) =>
        {
            Assert.Contains("GetConsumerFullCreditReport", req.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, RealFullReportResponse));
        });
        var match = new Models.ConsumerMatchResult
        {
            MatchingEngineId = 131510624,
            EnquiryId = 78820369,
            ConsumerId = 4902753,
            DataTicket = "test-ticket",
        };

        var history = await client.GetAddressHistoryAsync(match, CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Equal("ATIMATIM", entry.Address1);
        Assert.Equal("Residential", entry.AddressTypeInd);
        Assert.Equal("19/06/2023", entry.UpDateDate);
    }

    [Fact]
    public async Task GetAddressHistoryAsync_ReportCallFails_ReturnsPlaceholderNotException()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var match = new Models.ConsumerMatchResult
        {
            MatchingEngineId = 1,
            EnquiryId = 2,
            ConsumerId = 3,
            DataTicket = "test-ticket",
        };

        var history = await client.GetAddressHistoryAsync(match, CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Null(entry.Address1);
    }
}
