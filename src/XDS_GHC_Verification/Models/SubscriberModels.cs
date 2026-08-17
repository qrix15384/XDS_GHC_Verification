using System.ComponentModel.DataAnnotations;

namespace XDS_GHC_Verification.Models;

/// <summary>A client organization (e.g. a bank or telco) subscribed to this verification service.</summary>
public class Subscriber
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public class SubscriberResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static SubscriberResponse FromEntity(Subscriber subscriber) => new()
    {
        Id = subscriber.Id,
        Name = subscriber.Name,
        IsActive = subscriber.IsActive,
        CreatedAtUtc = subscriber.CreatedAtUtc,
    };
}

public class CreateSubscriberRequest
{
    [Required]
    public string Name { get; set; } = "";
}

public class UpdateSubscriberRequest
{
    [Required]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
