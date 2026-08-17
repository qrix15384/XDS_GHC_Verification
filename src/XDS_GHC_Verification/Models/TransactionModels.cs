using System.Text.Json.Nodes;

namespace XDS_GHC_Verification.Models;

public class TransactionQuery
{
    private int _page = 1;
    private int _pageSize = 25;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch { < 1 => 1, > 100 => 100, _ => value };
    }

    public string? Username { get; set; }
    public string? EndpointPath { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? DetailsFound { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int? SubscriberId { get; set; }
}

/// <summary>Summary row for the transactions list — excludes the (potentially large) ResponsePayload.</summary>
public class TransactionListItem
{
    public long Id { get; set; }
    public Guid RequestId { get; set; }
    public DateTime RequestAtUtc { get; set; }
    public string EndpointPath { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string? Username { get; set; }
    public int HttpStatusCode { get; set; }
    public string? DetailsFound { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }

    /// <summary>Redacted (set to null) for non-Admin callers by TransactionsController.</summary>
    public string? PinNumber { get; set; }

    public int? SubscriberId { get; set; }
    public string? SubscriberName { get; set; }
}

/// <summary>Full detail for a single transaction — Admin-only, includes the response payload.</summary>
public class TransactionDetail : TransactionListItem
{
    public JsonNode? ResponsePayload { get; set; }
    public string? RawResponsePayload { get; set; }
}

public class TransactionPageResult
{
    public required IReadOnlyList<TransactionListItem> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
