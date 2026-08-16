using System.ComponentModel.DataAnnotations;

namespace XDS_GHC_Verification.Models;

/// <summary>DB-shape record for a managed proxy user account.</summary>
public class ProxyUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Standard"; // "Admin" | "Standard"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Public-facing shape of a ProxyUser — never includes the password hash.</summary>
public class ProxyUserResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static ProxyUserResponse FromEntity(ProxyUser user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Role = user.Role,
        IsActive = user.IsActive,
        CreatedAtUtc = user.CreatedAtUtc,
    };
}

public class CreateProxyUserRequest
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = "";

    [Required]
    public string Role { get; set; } = "Standard";
}

public class UpdateProxyUserRequest
{
    [Required]
    public string Role { get; set; } = "Standard";

    public bool IsActive { get; set; } = true;
}

public class ResetPasswordRequest
{
    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = "";
}
