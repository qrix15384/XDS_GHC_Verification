using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>
/// Boots the real app with two swaps: the SQL-backed audit log becomes an
/// in-memory fake, and the upstream HttpClient's handler is replaced with a
/// delegate each test controls — so tests never touch a real database or network.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeAuditLogService AuditLog { get; } = new();
    public FakeSubscriberService Subscribers { get; } = new();
    public FakeProxyUserService ProxyUsers { get; }
    public FakeTransactionQueryService Transactions { get; } = new();

    public CustomWebApplicationFactory()
    {
        ProxyUsers = new FakeProxyUserService(Subscribers);
    }

    /// <summary>Set per-test to control what the "upstream API" (NIA) returns.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> UpstreamHandler { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        });

    /// <summary>Set per-test to control what the credit API (login/getconsumermatch/GetConsumerFullCreditReport) returns.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> CreditApiHandler { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        });

    public const string ApiKey = "test-service-api-key";
    public const string AuthUsername = "test-user";
    public const string AuthPassword = "test-password";
    public const string SelfieMerchantKey = "test-merchant-key";
    public const string SelfieUserId = "test-selfie-user-id";
    public const string JwtSigningKey = "test-only-signing-key-at-least-32-characters-long";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceAuth:ApiKey"] = ApiKey,
                ["ServiceAuth:AuthUsername"] = AuthUsername,
                ["ServiceAuth:AuthPassword"] = AuthPassword,
                ["Upstream:BaseUrl"] = "https://upstream.test/",
                ["Upstream:AuthType"] = "None",
                ["Selfie:MerchantKey"] = SelfieMerchantKey,
                ["Selfie:Center"] = "BRANCHLESS",
                ["Selfie:UserId"] = SelfieUserId,
                ["ConnectionStrings:Verification"] = "Server=unused;Database=unused;",
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Jwt:Issuer"] = "XDS_GHC_Verification.Tests",
                ["Jwt:Audience"] = "XDS_GHC_Verification.Tests",
                ["Jwt:ExpiryMinutes"] = "480",
                ["CreditApi:BaseUrl"] = "https://credit-api.test/",
                ["CreditApi:Username"] = "test-credit-user",
                ["CreditApi:Password"] = "test-credit-password",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(AuditLog);

            services.RemoveAll<IProxyUserService>();
            services.AddSingleton<IProxyUserService>(ProxyUsers);

            services.RemoveAll<ISubscriberService>();
            services.AddSingleton<ISubscriberService>(Subscribers);

            services.RemoveAll<ITransactionQueryService>();
            services.AddSingleton<ITransactionQueryService>(Transactions);

            services.AddHttpClient<UpstreamClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler((req, ct) => UpstreamHandler(req, ct)));

            services.AddHttpClient<ICreditApiClient, CreditApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler((req, ct) => CreditApiHandler(req, ct)));
        });
    }
}
