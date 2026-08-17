using Dapper;
using Microsoft.Data.SqlClient;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Services;

public interface IProxyUserService
{
    Task<ProxyUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<ProxyUser?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProxyUser>> ListAsync(CancellationToken ct = default);
    Task<ProxyUser> CreateAsync(string username, string passwordHash, string role, int? subscriberId, CancellationToken ct = default);
    Task UpdateRoleAndStatusAsync(int id, string role, bool isActive, int? subscriberId, CancellationToken ct = default);
    Task UpdatePasswordHashAsync(int id, string passwordHash, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);
}

/// <summary>
/// Dapper-backed CRUD for dbo.ProxyUsers. Unlike AuditLogService, this
/// service does NOT swallow exceptions — these are account-management
/// operations the caller directly depends on the result of (login, user
/// administration), not best-effort logging that must never break the
/// real API response. Let failures propagate to the controller.
/// </summary>
public class ProxyUserService : IProxyUserService
{
    // Cross-database join against the real, authoritative subscriber list
    // (XdsGhanaAdmin.dbo.Subscriber, same SQL Server instance, read-only —
    // see SubscriberService) rather than any table owned by this database.
    private const string SelectColumns = """
        u.Id, u.Username, u.PasswordHash, u.Role, u.IsActive, u.CreatedAtUtc, u.SubscriberId,
        s.SubscriberName AS SubscriberName
        """;

    private const string SubscriberJoin = "LEFT JOIN XdsGhanaAdmin.dbo.Subscriber s ON s.SubscriberID = u.SubscriberId";

    private readonly string _connectionString;

    public ProxyUserService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Verification")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Verification configuration.");
    }

    public async Task<ProxyUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<ProxyUser>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM dbo.ProxyUsers u {SubscriberJoin}
            WHERE u.Username = @username
            """,
            new { username }, cancellationToken: ct));
    }

    public async Task<ProxyUser?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<ProxyUser>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM dbo.ProxyUsers u {SubscriberJoin}
            WHERE u.Id = @id
            """,
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProxyUser>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var users = await connection.QueryAsync<ProxyUser>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM dbo.ProxyUsers u {SubscriberJoin}
            ORDER BY u.Username
            """,
            cancellationToken: ct));
        return users.ToList();
    }

    public async Task<ProxyUser> CreateAsync(string username, string passwordHash, string role, int? subscriberId, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.ProxyUsers (Username, PasswordHash, Role, SubscriberId)
            OUTPUT INSERTED.Id
            VALUES (@username, @passwordHash, @role, @subscriberId);
            """,
            new { username, passwordHash, role, subscriberId }, cancellationToken: ct));

        return await FindByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Failed to read back the just-created ProxyUsers row.");
    }

    public async Task UpdateRoleAndStatusAsync(int id, string role, bool isActive, int? subscriberId, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.ProxyUsers SET Role = @role, IsActive = @isActive, SubscriberId = @subscriberId WHERE Id = @id",
            new { id, role, isActive, subscriberId }, cancellationToken: ct));
    }

    public async Task UpdatePasswordHashAsync(int id, string passwordHash, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.ProxyUsers SET PasswordHash = @passwordHash WHERE Id = @id",
            new { id, passwordHash }, cancellationToken: ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ProxyUsers WHERE Id = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.ProxyUsers", cancellationToken: ct));
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.ProxyUsers WHERE Role = 'Admin' AND IsActive = 1", cancellationToken: ct));
    }
}
