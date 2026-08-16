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
}
