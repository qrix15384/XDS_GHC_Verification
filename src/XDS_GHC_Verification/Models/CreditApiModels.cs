using System.Text.Json.Serialization;

namespace XDS_GHC_Verification.Models;

/// <summary>
/// Shapes below are transcribed from real, live responses observed from the
/// XDS Data credit API during integration testing — not guessed. Only the
/// fields this app actually uses are modeled; everything else in the real
/// payload is ignored by System.Text.Json's default unknown-member handling.
/// </summary>
public class CreditApiLoginResponse
{
    [JsonPropertyName("dataTicket")]
    public string DataTicket { get; set; } = "";

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}

public class ConsumerMatchResponseWrapper
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}

/// <summary>
/// GetConsumerMatch returns a single-element array. matchingEngineID /
/// enquiryID / consumerID are all null when nothing matched.
/// </summary>
public class ConsumerMatchResult
{
    [JsonPropertyName("response")]
    public ConsumerMatchResponseWrapper Response { get; set; } = new();

    [JsonPropertyName("matchingEngineID")]
    public long? MatchingEngineId { get; set; }

    [JsonPropertyName("enquiryID")]
    public long? EnquiryId { get; set; }

    [JsonPropertyName("consumerID")]
    public long? ConsumerId { get; set; }

    public bool IsMatch => MatchingEngineId is not null && EnquiryId is not null && ConsumerId is not null;

    /// <summary>Not part of the API response — set by CreditApiClient so the same ticket carries into GetConsumerFullCreditReport.</summary>
    [JsonIgnore]
    public string DataTicket { get; set; } = "";
}

public class AddressHistoryEntry
{
    [JsonPropertyName("upDateDate")]
    public string? UpDateDate { get; set; }

    [JsonPropertyName("upDateOnDate")]
    public string? UpDateOnDate { get; set; }

    [JsonPropertyName("address1")]
    public string? Address1 { get; set; }

    [JsonPropertyName("address2")]
    public string? Address2 { get; set; }

    [JsonPropertyName("address3")]
    public string? Address3 { get; set; }

    [JsonPropertyName("address4")]
    public string? Address4 { get; set; }

    [JsonPropertyName("addressTypeInd")]
    public string? AddressTypeInd { get; set; }

    /// <summary>A single all-null placeholder — used when GetConsumerMatch found no record, so the response shape stays consistent either way.</summary>
    public static AddressHistoryEntry Empty => new();
}

public class FullCreditReportResponse
{
    [JsonPropertyName("addressHistory")]
    public List<AddressHistoryEntry> AddressHistory { get; set; } = [];
}
