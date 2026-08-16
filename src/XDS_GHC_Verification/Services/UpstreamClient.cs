using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Services;

/// <summary>
/// Carries the exact status code and body that should be returned to the
/// caller when an upstream call fails — the C# equivalent of FastAPI's
/// HTTPException, but as a normal exception caught explicitly by each
/// controller action (so it can log the audit entry before responding).
/// </summary>
public class UpstreamServiceException(int statusCode, object? detail) : Exception(detail?.ToString() ?? $"Upstream error {statusCode}")
{
    public int StatusCode { get; } = statusCode;
    public object? Detail { get; } = detail;
}

/// <summary>Async HTTP client for communicating with the upstream API.</summary>
public class UpstreamClient
{
    private readonly HttpClient _client;
    private readonly ILogger<UpstreamClient> _logger;

    public UpstreamClient(HttpClient client, IOptions<UpstreamOptions> options, ILogger<UpstreamClient> logger)
    {
        var opts = options.Value;
        client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        switch (opts.AuthType.ToLowerInvariant())
        {
            case "apikey":
                client.DefaultRequestHeaders.Add(opts.ApiKeyHeader, opts.ApiKey);
                break;
            case "bearer":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
                break;
            case "basic":
                var basicValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.BasicUsername}:{opts.BasicPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
                break;
                // "none" — upstream authenticates via a field in the JSON body instead.
        }

        _client = client;
        _logger = logger;
    }

    /// <summary>Forwards an arbitrary request to the upstream API (used by the generic proxy).</summary>
    public async Task<(int StatusCode, JsonNode? Body)> ForwardAsync(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        byte[]? body,
        IDictionary<string, string>? headers,
        CancellationToken ct)
    {
        var cleanPath = path.TrimStart('/');
        var url = query is { Count: > 0 } ? QueryHelpers.AddQueryString(cleanPath, query) : cleanPath;

        using var request = new HttpRequestMessage(method, url);
        if (body is { Length: > 0 })
        {
            request.Content = new ByteArrayContent(body);
        }
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    request.Content ??= new ByteArrayContent(body ?? []);
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }

        var response = await SendAsync(request, method, cleanPath, ct);
        return await HandleResponseAsync(response, method, cleanPath, ct);
    }

    /// <summary>POSTs a JSON payload to the upstream API (used by the selfie verification endpoints).</summary>
    public async Task<(int StatusCode, JsonNode? Body)> PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        var cleanPath = path.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, cleanPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        var response = await SendAsync(request, HttpMethod.Post, cleanPath, ct);
        return await HandleResponseAsync(response, HttpMethod.Post, cleanPath, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpMethod method, string path, CancellationToken ct)
    {
        try
        {
            return await _client.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Upstream request timed out: {Method} /{Path}", method, path);
            throw new UpstreamServiceException(504, "The upstream service did not respond in time. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not connect to upstream API: {Method} /{Path}", method, path);
            throw new UpstreamServiceException(502, $"Unable to connect to the upstream service: {ex.Message}");
        }
    }

    private async Task<(int StatusCode, JsonNode? Body)> HandleResponseAsync(
        HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;

        if (statusCode >= 500)
        {
            _logger.LogError("Upstream server error {Status} for {Method} /{Path}", statusCode, method, path);
            throw new UpstreamServiceException(502, $"Upstream API returned a server error ({statusCode}).");
        }

        var text = await response.Content.ReadAsStringAsync(ct);

        JsonNode? node = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                if (response.IsSuccessStatusCode)
                {
                    // Non-JSON success response (e.g. plain text) — wrap it, don't fail.
                    return (statusCode, JsonNode.Parse(JsonSerializer.Serialize(new { raw_response = text })));
                }

                _logger.LogError("Upstream returned a non-JSON response: {Text}", text.Length > 200 ? text[..200] : text);
                throw new UpstreamServiceException(502, "Upstream API returned an invalid response.");
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Upstream non-success {Status} for {Method} /{Path}: {Body}", statusCode, method, path, text);
            throw new UpstreamServiceException(statusCode, (object?)node ?? text);
        }

        return (statusCode, node);
    }
}
