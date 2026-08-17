namespace XDS_GHC_Verification.Models;

/// <summary>
/// A client organization (e.g. a bank) subscribed to this verification
/// service. Read-only, sourced live from XdsGhanaAdmin.dbo.Subscriber — the
/// real, authoritative subscriber list — never written to from here.
/// </summary>
public class Subscriber
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class SubscriberResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }

    public static SubscriberResponse FromEntity(Subscriber subscriber) => new()
    {
        Id = subscriber.Id,
        Name = subscriber.Name,
        IsActive = subscriber.IsActive,
    };
}
