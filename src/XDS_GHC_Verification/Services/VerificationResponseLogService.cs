using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Services;

/// <summary>
/// One row per call to NIA: the original, unmasked upstream response for the
/// Selfie Verification flow. Only logged when NIA was actually reached — a
/// request that fails image validation before ever calling NIA has no row here.
/// </summary>
public record NiaResponseLogEntry
{
    public required Guid RequestId { get; init; }
    public required string EndpointPath { get; init; }
    public string? PinNumber { get; init; }
    public required DateTime CallAtUtc { get; init; }
    public DateTime? ResponseAtUtc { get; init; }
    public required int HttpStatusCode { get; init; }
    public JsonNode? RawResponsePayload { get; init; }
    public string? RawResponseText { get; init; }
    public string? Username { get; init; }
    public int? SubscriberId { get; init; }
    public string? SubscriberName { get; init; }
}

/// <summary>
/// One row per response this proxy sends back to the calling client for the
/// Selfie Verification flow — logged for every response, NIA-backed or not
/// (an image-validation failure that never reaches NIA still gets a row here).
/// </summary>
public record ProxyResponseLogEntry
{
    public required Guid RequestId { get; init; }
    public required string EndpointPath { get; init; }
    public string? PinNumber { get; init; }
    public required DateTime CallAtUtc { get; init; }
    public required DateTime ResponseAtUtc { get; init; }
    public required int HttpStatusCode { get; init; }
    public JsonNode? MaskedResponsePayload { get; init; }
    public string? Username { get; init; }
    public int? SubscriberId { get; init; }
    public string? SubscriberName { get; init; }
}

public interface IVerificationResponseLogService
{
    Task LogNiaResponseAsync(NiaResponseLogEntry entry, CancellationToken ct = default);
    Task LogProxyResponseAsync(ProxyResponseLogEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Writes the NIA/proxy response pair for the Selfie Verification flow to
/// dbo.NiaResponseLog / dbo.ProxyResponseLog (see
/// sql/005_add_verification_response_logs.sql). Best-effort, same as
/// <see cref="AuditLogService"/>: a database outage must never break the
/// actual API response.
/// </summary>
public class VerificationResponseLogService : IVerificationResponseLogService
{
    private const string InsertNiaSql = """
        INSERT INTO dbo.NiaResponseLog
            (RequestId, EndpointPath, PinNumber, CallAtUtc, ResponseAtUtc,
             HttpStatusCode, RawResponsePayload, Username, SubscriberId, SubscriberName)
        VALUES
            (@RequestId, @EndpointPath, @PinNumber, @CallAtUtc, @ResponseAtUtc,
             @HttpStatusCode, @RawResponsePayload, @Username, @SubscriberId, @SubscriberName);
        """;

    private const string InsertProxySql = """
        INSERT INTO dbo.ProxyResponseLog
            (RequestId, EndpointPath, PinNumber, CallAtUtc, ResponseAtUtc,
             HttpStatusCode, MaskedResponsePayload, Username, SubscriberId, SubscriberName)
        VALUES
            (@RequestId, @EndpointPath, @PinNumber, @CallAtUtc, @ResponseAtUtc,
             @HttpStatusCode, @MaskedResponsePayload, @Username, @SubscriberId, @SubscriberName);
        """;

    private readonly string _connectionString;
    private readonly ILogger<VerificationResponseLogService> _logger;

    public VerificationResponseLogService(IConfiguration configuration, ILogger<VerificationResponseLogService> logger)
    {
        _connectionString = configuration.GetConnectionString("Verification")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Verification configuration.");
        _logger = logger;
    }

    public async Task LogNiaResponseAsync(NiaResponseLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(InsertNiaSql, new
            {
                entry.RequestId,
                EndpointPath = Truncate(entry.EndpointPath, 200),
                PinNumber = Truncate(entry.PinNumber, 20),
                entry.CallAtUtc,
                entry.ResponseAtUtc,
                entry.HttpStatusCode,
                RawResponsePayload = SerializeRedacted(entry.RawResponsePayload) ?? entry.RawResponseText,
                Username = Truncate(entry.Username, 100),
                entry.SubscriberId,
                SubscriberName = Truncate(entry.SubscriberName, 200),
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write NIA response log for {Endpoint}", entry.EndpointPath);
        }
    }

    public async Task LogProxyResponseAsync(ProxyResponseLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(InsertProxySql, new
            {
                entry.RequestId,
                EndpointPath = Truncate(entry.EndpointPath, 200),
                PinNumber = Truncate(entry.PinNumber, 20),
                entry.CallAtUtc,
                entry.ResponseAtUtc,
                entry.HttpStatusCode,
                MaskedResponsePayload = SerializeRedacted(entry.MaskedResponsePayload),
                Username = Truncate(entry.Username, 100),
                entry.SubscriberId,
                SubscriberName = Truncate(entry.SubscriberName, 200),
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write proxy response log for {Endpoint}", entry.EndpointPath);
        }
    }

    private static string? SerializeRedacted(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        var redacted = JsonRedactor.Redact(node);
        return redacted?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
