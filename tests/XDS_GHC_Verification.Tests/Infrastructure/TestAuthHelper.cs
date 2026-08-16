using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace XDS_GHC_Verification.Tests.Infrastructure;

public static class TestAuthHelper
{
    public record LoginResult(string ApiKey, string Token, string Role);

    public static async Task<LoginResult> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new LoginResult(
            body.GetProperty("apiKey").GetString()!,
            body.GetProperty("token").GetString()!,
            body.GetProperty("role").GetString()!);
    }

    public static void UseBearer(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
