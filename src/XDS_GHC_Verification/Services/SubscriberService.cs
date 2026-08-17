using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Services;

public interface ISubscriberService
{
    Task<Subscriber?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default);
    Task<Subscriber> CreateAsync(string name, CancellationToken ct = default);
    Task UpdateAsync(int id, string name, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Dapper-backed CRUD for dbo.Subscribers. Like ProxyUserService (and unlike
/// AuditLogService), exceptions propagate — this is account-management data
/// the caller directly depends on, not best-effort logging.
/// </summary>
public class SubscriberService : ISubscriberService
{
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
            "SELECT Id, Name, IsActive, CreatedAtUtc FROM dbo.Subscribers WHERE Id = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<Subscriber>(new CommandDefinition(
            "SELECT Id, Name, IsActive, CreatedAtUtc FROM dbo.Subscribers WHERE Name = @name",
            new { name }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var subscribers = await connection.QueryAsync<Subscriber>(new CommandDefinition(
            "SELECT Id, Name, IsActive, CreatedAtUtc FROM dbo.Subscribers ORDER BY Name",
            cancellationToken: ct));
        return subscribers.ToList();
    }

    public async Task<Subscriber> CreateAsync(string name, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Subscribers (Name)
            OUTPUT INSERTED.Id
            VALUES (@name);
            """,
            new { name }, cancellationToken: ct));

        return new Subscriber { Id = id, Name = name, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
    }

    public async Task UpdateAsync(int id, string name, bool isActive, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Subscribers SET Name = @name, IsActive = @isActive WHERE Id = @id",
            new { id, name, isActive }, cancellationToken: ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Subscribers WHERE Id = @id",
            new { id }, cancellationToken: ct));
    }
}
