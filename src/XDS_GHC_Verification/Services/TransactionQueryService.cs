using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Services;

public interface ITransactionQueryService
{
    Task<TransactionPageResult> QueryAsync(TransactionQuery filter, CancellationToken ct = default);
    Task<TransactionDetail?> GetByIdAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// Dapper-backed reads over dbo.ApiTransactionLog for the admin UI. Kept
/// separate from IAuditLogService: writes there are deliberately
/// best-effort/never-throw, but a read the UI is waiting on should propagate
/// errors normally so the UI can surface them.
/// </summary>
public class TransactionQueryService : ITransactionQueryService
{
    private readonly string _connectionString;

    public TransactionQueryService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Verification")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Verification configuration.");
    }

    public async Task<TransactionPageResult> QueryAsync(TransactionQuery filter, CancellationToken ct = default)
    {
        var (whereClause, parameters) = BuildWhereClause(filter);

        await using var connection = new SqlConnection(_connectionString);

        var totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM dbo.ApiTransactionLog {whereClause}", parameters, cancellationToken: ct));

        parameters.Add("@offset", (filter.Page - 1) * filter.PageSize);
        parameters.Add("@pageSize", filter.PageSize);

        var items = await connection.QueryAsync<TransactionListItem>(new CommandDefinition(
            $"""
            SELECT Id, RequestId, RequestAtUtc, EndpointPath, HttpMethod, Username,
                   HttpStatusCode, DetailsFound, ErrorMessage, DurationMs, PinNumber,
                   SubscriberId, SubscriberName
            FROM dbo.ApiTransactionLog
            {whereClause}
            ORDER BY RequestAtUtc DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """,
            parameters, cancellationToken: ct));

        return new TransactionPageResult
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task<TransactionDetail?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var detail = await connection.QuerySingleOrDefaultAsync<TransactionDetail>(new CommandDefinition(
            """
            SELECT Id, RequestId, RequestAtUtc, EndpointPath, HttpMethod, Username,
                   HttpStatusCode, DetailsFound, ErrorMessage, DurationMs, PinNumber,
                   SubscriberId, SubscriberName,
                   ResponsePayload AS RawResponsePayload
            FROM dbo.ApiTransactionLog
            WHERE Id = @id;
            """,
            new { id }, cancellationToken: ct));

        // The column is stored as text; parse it into structured JSON for the
        // response when possible so the client doesn't receive a double-encoded string.
        if (detail is { RawResponsePayload: not null })
        {
            try
            {
                detail.ResponsePayload = JsonNode.Parse(detail.RawResponsePayload);
                detail.RawResponsePayload = null;
            }
            catch (JsonException)
            {
                // Not JSON — leave it in RawResponsePayload as plain text.
            }
        }

        return detail;
    }

    private static (string WhereClause, DynamicParameters Parameters) BuildWhereClause(TransactionQuery filter)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            conditions.Add("Username = @username");
            parameters.Add("@username", filter.Username);
        }
        if (!string.IsNullOrWhiteSpace(filter.EndpointPath))
        {
            conditions.Add("EndpointPath = @endpointPath");
            parameters.Add("@endpointPath", filter.EndpointPath);
        }
        if (filter.HttpStatusCode is not null)
        {
            conditions.Add("HttpStatusCode = @httpStatusCode");
            parameters.Add("@httpStatusCode", filter.HttpStatusCode);
        }
        if (!string.IsNullOrWhiteSpace(filter.DetailsFound))
        {
            conditions.Add("DetailsFound = @detailsFound");
            parameters.Add("@detailsFound", filter.DetailsFound);
        }
        if (filter.FromUtc is not null)
        {
            conditions.Add("RequestAtUtc >= @fromUtc");
            parameters.Add("@fromUtc", filter.FromUtc);
        }
        if (filter.ToUtc is not null)
        {
            conditions.Add("RequestAtUtc <= @toUtc");
            parameters.Add("@toUtc", filter.ToUtc);
        }
        if (filter.SubscriberId is not null)
        {
            conditions.Add("SubscriberId = @subscriberId");
            parameters.Add("@subscriberId", filter.SubscriberId);
        }

        if (conditions.Count == 0)
        {
            return ("", parameters);
        }

        var sb = new StringBuilder("WHERE ");
        sb.Append(string.Join(" AND ", conditions));
        return (sb.ToString(), parameters);
    }
}
