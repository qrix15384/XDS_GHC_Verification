namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>Routes HttpClient calls through a delegate instead of the network — used to stand in for the upstream API.</summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}
