using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>In-memory stand-in for TransactionQueryService — no real SQL Server needed for tests.</summary>
public class FakeTransactionQueryService : ITransactionQueryService
{
    public List<TransactionDetail> Rows { get; } = [];

    public Task<TransactionPageResult> QueryAsync(TransactionQuery filter, CancellationToken ct = default)
    {
        IEnumerable<TransactionDetail> query = Rows;

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            query = query.Where(r => r.Username == filter.Username);
        }
        if (!string.IsNullOrWhiteSpace(filter.EndpointPath))
        {
            query = query.Where(r => r.EndpointPath == filter.EndpointPath);
        }

        var ordered = query.OrderByDescending(r => r.RequestAtUtc).ToList();
        var page = ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new TransactionListItem
            {
                Id = r.Id,
                RequestId = r.RequestId,
                RequestAtUtc = r.RequestAtUtc,
                EndpointPath = r.EndpointPath,
                HttpMethod = r.HttpMethod,
                Username = r.Username,
                HttpStatusCode = r.HttpStatusCode,
                DetailsFound = r.DetailsFound,
                ErrorMessage = r.ErrorMessage,
                DurationMs = r.DurationMs,
                PinNumber = r.PinNumber,
            })
            .ToList();

        return Task.FromResult(new TransactionPageResult
        {
            Items = page,
            TotalCount = ordered.Count,
            Page = filter.Page,
            PageSize = filter.PageSize,
        });
    }

    public Task<TransactionDetail?> GetByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
}
