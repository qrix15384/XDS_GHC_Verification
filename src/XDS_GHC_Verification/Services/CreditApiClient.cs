using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Services;

public interface ICreditApiClient
{
    /// <summary>
    /// Runs login + GetConsumerMatch. Returns null if login fails or the
    /// call errors — enrichment is best-effort and must never break the
    /// underlying selfie verification response. A non-null result with
    /// IsMatch == false means the call succeeded but found no record.
    /// </summary>
    Task<ConsumerMatchResult?> FindConsumerMatchAsync(string identification, string dateOfBirthIso, CancellationToken ct);

    /// <summary>Fetches the full credit report for a match and returns only its address history.</summary>
    Task<List<AddressHistoryEntry>> GetAddressHistoryAsync(ConsumerMatchResult match, CancellationToken ct);
}

/// <summary>
/// Client for the internal XDS Data credit/consumer-matching API
/// (login -> GetConsumerMatch -> GetConsumerFullCreditReport). Response
/// shapes are transcribed from real, live-tested responses. Best-effort:
/// every failure is caught and logged, never thrown — a credit-lookup
/// outage must not break the selfie verification response it enriches.
/// </summary>
public class CreditApiClient : ICreditApiClient
{
    private readonly HttpClient _client;
    private readonly CreditApiOptions _options;
    private readonly ILogger<CreditApiClient> _logger;

    public CreditApiClient(HttpClient client, IOptions<CreditApiOptions> options, ILogger<CreditApiClient> logger)
    {
        _options = options.Value;
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _client = client;
        _logger = logger;
    }

    private async Task<string?> LoginAsync(CancellationToken ct)
    {
        try
        {
            var url = QueryHelpers.AddQueryString("Api/login", new Dictionary<string, string?>
            {
                ["username"] = _options.Username,
                ["password"] = _options.Password,
            });
            using var response = await _client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Credit API login failed with status {Status}", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<CreditApiLoginResponse>(cancellationToken: ct);
            if (string.IsNullOrEmpty(body?.DataTicket))
            {
                _logger.LogError("Credit API login returned no dataTicket");
                return null;
            }
            return body.DataTicket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credit API login threw an exception");
            return null;
        }
    }

    public async Task<ConsumerMatchResult?> FindConsumerMatchAsync(string identification, string dateOfBirthIso, CancellationToken ct)
    {
        var ticket = await LoginAsync(ct);
        if (ticket is null)
        {
            return null;
        }

        try
        {
            var url = QueryHelpers.AddQueryString("Api/getconsumermatch", new Dictionary<string, string?>
            {
                ["dataticket"] = ticket,
                ["enquiryReason"] = "TEST",
                ["ConsumerName"] = "",
                ["DateOfBirth"] = dateOfBirthIso,
                ["identification"] = identification,
                ["AccountNumber"] = "",
            });
            using var response = await _client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Credit API consumer match failed with status {Status}", (int)response.StatusCode);
                return null;
            }

            var results = await response.Content.ReadFromJsonAsync<List<ConsumerMatchResult>>(cancellationToken: ct);
            var result = results?.FirstOrDefault();
            if (result is not null)
            {
                result.DataTicket = ticket;
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credit API consumer match threw an exception");
            return null;
        }
    }

    public async Task<List<AddressHistoryEntry>> GetAddressHistoryAsync(ConsumerMatchResult match, CancellationToken ct)
    {
        if (!match.IsMatch)
        {
            return [AddressHistoryEntry.Empty];
        }

        try
        {
            var url = QueryHelpers.AddQueryString("Api/GetConsumerFullCreditReport", new Dictionary<string, string?>
            {
                ["EnquiryID"] = match.EnquiryId!.Value.ToString(),
                ["ConsumerID"] = match.ConsumerId!.Value.ToString(),
                ["DataTicket"] = match.DataTicket,
                ["MatchingEngineID"] = match.MatchingEngineId!.Value.ToString(),
            });
            using var response = await _client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Credit API full report failed with status {Status}", (int)response.StatusCode);
                return [AddressHistoryEntry.Empty];
            }

            var report = await response.Content.ReadFromJsonAsync<FullCreditReportResponse>(cancellationToken: ct);
            return report?.AddressHistory is { Count: > 0 } history ? history : [AddressHistoryEntry.Empty];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credit API full report threw an exception");
            return [AddressHistoryEntry.Empty];
        }
    }
}
