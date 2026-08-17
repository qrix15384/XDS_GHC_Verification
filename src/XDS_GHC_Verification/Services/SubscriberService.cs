using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Services;

public interface ISubscriberService
{
    Task<Subscriber?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// Read-only Dapper access to the real, authoritative subscriber list —
/// XdsGhanaAdmin.dbo.Subscriber — via a cross-database query (same SQL
/// Server instance, three-part name; no linked server). This service never
/// writes to that table: subscribers are managed entirely outside this app.
/// The connecting login only has SELECT on dbo.Subscriber in that database —
/// nothing else — granted separately, once, outside this codebase.
/// </summary>
public class SubscriberService : ISubscriberService
{
    private const string SelectColumns = "SubscriberID AS Id, SubscriberName AS Name, CASE WHEN StatusInd = 'A' THEN 1 ELSE 0 END AS IsActive";

    private readonly string _connectionString;

    public SubscriberService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Verification")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Verification configuration.");
    }

    public async Task<Subscriber?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<Subscriber>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM XdsGhanaAdmin.dbo.Subscriber WHERE SubscriberID = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<Subscriber>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM XdsGhanaAdmin.dbo.Subscriber WHERE SubscriberName = @name",
            new { name }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var subscribers = await connection.QueryAsync<Subscriber>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM XdsGhanaAdmin.dbo.Subscriber ORDER BY SubscriberName",
            cancellationToken: ct));
        return subscribers.ToList();
    }
}
