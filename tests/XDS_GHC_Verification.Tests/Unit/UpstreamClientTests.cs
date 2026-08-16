using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Unit;

public class UpstreamClientTests
{
    private static UpstreamClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        UpstreamOptions? options = null)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler));
        return new UpstreamClient(httpClient, Microsoft.Extensions.Options.Options.Create(options ?? new UpstreamOptions()), NullLogger<UpstreamClient>.Instance);
    }

    [Fact]
    public async Task PostJsonAsync_SuccessfulJsonResponse_ReturnsParsedBody()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"00"}""", Encoding.UTF8, "application/json"),
        }));

        var (statusCode, body) = await client.PostJsonAsync("some/path", new { }, CancellationToken.None);

        Assert.Equal(200, statusCode);
        Assert.Equal("00", body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task PostJsonAsync_NonJsonSuccessResponse_IsWrapped()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("plain text ok", Encoding.UTF8, "text/plain"),
        }));

        var (statusCode, body) = await client.PostJsonAsync("some/path", new { }, CancellationToken.None);

        Assert.Equal(200, statusCode);
        Assert.Equal("plain text ok", body!["raw_response"]!.GetValue<string>());
    }

    [Fact]
    public async Task PostJsonAsync_ServerError_ThrowsUpstreamServiceException502()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<UpstreamServiceException>(
            () => client.PostJsonAsync("some/path", new { }, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task PostJsonAsync_ClientErrorWithJsonBody_ThrowsWithOriginalStatusAndBody()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"detail":"not found"}""", Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<UpstreamServiceException>(
            () => client.PostJsonAsync("some/path", new { }, CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task PostJsonAsync_NonJsonErrorResponse_ThrowsUpstreamServiceException502()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not json", Encoding.UTF8, "text/plain"),
        }));

        var ex = await Assert.ThrowsAsync<UpstreamServiceException>(
            () => client.PostJsonAsync("some/path", new { }, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task PostJsonAsync_RequestTimesOut_ThrowsUpstreamServiceException504()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException("simulated timeout"));

        var ex = await Assert.ThrowsAsync<UpstreamServiceException>(
            () => client.PostJsonAsync("some/path", new { }, CancellationToken.None));

        Assert.Equal(504, ex.StatusCode);
    }

    [Fact]
    public async Task PostJsonAsync_ConnectionFailure_ThrowsUpstreamServiceException502()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("simulated connection failure"));

        var ex = await Assert.ThrowsAsync<UpstreamServiceException>(
            () => client.PostJsonAsync("some/path", new { }, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Constructor_ApiKeyAuthType_AddsConfiguredHeader()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(
            (req, _) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            },
            new UpstreamOptions { AuthType = "apikey", ApiKey = "upstream-secret", ApiKeyHeader = "X-API-Key" });

        await client.PostJsonAsync("some/path", new { }, CancellationToken.None);

        Assert.Equal("upstream-secret", captured!.Headers.GetValues("X-API-Key").Single());
    }

    [Fact]
    public async Task Constructor_BearerAuthType_AddsAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(
            (req, _) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            },
            new UpstreamOptions { AuthType = "bearer", ApiKey = "bearer-token" });

        await client.PostJsonAsync("some/path", new { }, CancellationToken.None);

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("bearer-token", captured.Headers.Authorization.Parameter);
    }
}
