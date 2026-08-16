using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Services;

public record AuditLogEntry
{
    public required string EndpointPath { get; init; }
    public required string HttpMethod { get; init; }
    public string? Username { get; init; }
    public required int HttpStatusCode { get; init; }
    public JsonNode? ResponsePayload { get; init; }
    public string? RawResponsePayload { get; init; }
    public string? DetailsFound { get; init; } // "Y" | "N"
    public string? ErrorMessage { get; init; }
    public int? DurationMs { get; init; }
    public string? PinNumber { get; init; }
}

public interface IAuditLogService
{
    Task LogTransactionAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<bool> CheckConnectivityAsync(CancellationToken ct = default);
}

/// <summary>
/// Writes one row per API transaction to the ApiTransactionLog table.
/// Best-effort: a database outage must never break the actual API
/// response, so every failure here is caught and logged, never thrown.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private const string InsertSql = """
        INSERT INTO dbo.ApiTransactionLog
            (EndpointPath, HttpMethod, Username, HttpStatusCode, ResponsePayload,
             DetailsFound, ErrorMessage, DurationMs, PinNumber)
        VALUES
            (@EndpointPath, @HttpMethod, @Username, @HttpStatusCode, @ResponsePayload,
             @DetailsFound, @ErrorMessage, @DurationMs, @PinNumber);
        """;

    private readonly string _connectionString;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IConfiguration configuration, ILogger<AuditLogService> logger)
    {
        _connectionString = configuration.GetConnectionString("Verification")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Verification configuration.");
        _logger = logger;
    }

    public async Task LogTransactionAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(InsertSql, new
            {
                EndpointPath = Truncate(entry.EndpointPath, 200),
                HttpMethod = Truncate(entry.HttpMethod, 10),
                Username = Truncate(entry.Username, 100),
                entry.HttpStatusCode,
                ResponsePayload = SerializeResponse(entry),
                entry.DetailsFound,
                ErrorMessage = Truncate(entry.ErrorMessage, 500),
                entry.DurationMs,
                PinNumber = Truncate(entry.PinNumber, 20),
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write API transaction log for {Method} {Endpoint}",
                entry.HttpMethod, entry.EndpointPath);
        }
    }

    public async Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verification database is not reachable at startup.");
            return false;
        }
    }

    private static string? SerializeResponse(AuditLogEntry entry)
    {
        if (entry.RawResponsePayload is not null)
        {
            return entry.RawResponsePayload;
        }
        if (entry.ResponsePayload is null)
        {
            return null;
        }
        var redacted = JsonRedactor.Redact(entry.ResponsePayload);
        return redacted?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
