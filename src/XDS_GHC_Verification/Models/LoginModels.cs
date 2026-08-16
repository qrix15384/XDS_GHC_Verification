using System.ComponentModel.DataAnnotations;

namespace XDS_GHC_Verification.Models;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string ApiKey { get; set; } = "";
    public string TokenType { get; set; } = "apikey";

    /// <summary>JWT for the admin web app — authorizes user management and transaction viewing.</summary>
    public string Token { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
