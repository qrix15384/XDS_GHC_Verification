using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using XDS_GHC_Verification.Auth;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
builder.Services.Configure<AppInfoOptions>(builder.Configuration.GetSection(AppInfoOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));
builder.Services.Configure<UpstreamOptions>(builder.Configuration.GetSection(UpstreamOptions.SectionName));
builder.Services.Configure<SelfieOptions>(builder.Configuration.GetSection(SelfieOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CreditApiOptions>(builder.Configuration.GetSection(CreditApiOptions.SectionName));

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<UpstreamClient>();
builder.Services.AddHttpClient<ICreditApiClient, CreditApiClient>();
builder.Services.AddScoped<SelfieVerificationService>();
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IVerificationResponseLogService, VerificationResponseLogService>();
builder.Services.AddSingleton<IProxyUserService, ProxyUserService>();
builder.Services.AddSingleton<ISubscriberService, SubscriberService>();
builder.Services.AddSingleton<ITransactionQueryService, TransactionQueryService>();
builder.Services.AddSingleton<IPasswordHasher<ProxyUser>, PasswordHasher<ProxyUser>>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<ApiKeyAuthFilter>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOriginsList
            ?? ["*"];
        if (allowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

// ─── JWT authentication (admin web app only — X-API-Key remains the gate on
// the proxy/selfie endpoints for external API clients) ──────────────────────
// TokenValidationParameters are resolved lazily from IOptions<JwtOptions> (not
// read eagerly from builder.Configuration here) so WebApplicationFactory-based
// tests — which override config on the real host built after Program.cs's
// initial pass — see the same signing key the token was actually issued with.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        var opts = jwtOptions.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opts.Issuer,
            ValidateAudience = true,
            ValidAudience = opts.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// ─── Login rate limiting: 5 attempts per minute per client IP, rejected
// outright (no queueing) — the login endpoint had no brute-force protection
// at all before this. ───────────────────────────────────────────────────────
builder.Services.Configure<LoginRateLimitOptions>(builder.Configuration.GetSection(LoginRateLimitOptions.SectionName));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext =>
    {
        var limitOptions = httpContext.RequestServices.GetRequiredService<IOptions<LoginRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limitOptions.PermitLimit,
                Window = TimeSpan.FromSeconds(limitOptions.WindowSeconds),
                QueueLimit = 0,
            });
    });

    options.OnRejected = (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        return new ValueTask(context.HttpContext.Response.WriteAsync(
            """{"detail":"Too many login attempts. Please wait a minute and try again."}""", ct));
    };
});

var app = builder.Build();

// ─── Startup: fail fast on known-insecure Production config ────────────────
// Catches the two most likely "forgot to override the default" mistakes
// before they become a live vulnerability — an admin JWT signed with the
// shipped placeholder key, or a wide-open CORS policy nobody meant to ship.
if (app.Environment.IsProduction())
{
    const string placeholderSigningKey = "change-me-jwt-signing-key-at-least-32-characters-long";
    var jwtOptions = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
    if (jwtOptions.SigningKey == placeholderSigningKey || jwtOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is missing, too short, or still the placeholder value. Set a strong, unique " +
            "signing key (32+ random characters) in the production configuration before starting this service.");
    }

    var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
    if (corsOptions.AllowedOriginsList.Contains("*"))
    {
        app.Services.GetRequiredService<ILogger<Program>>().LogWarning(
            "Cors:AllowedOrigins is \"*\" in Production — any website can make cross-origin requests to " +
            "this API. Set it to the specific origin(s) that need cross-origin access, or leave it empty if none do.");
    }
}

// ─── Startup: best-effort DB connectivity check + ProxyUsers seed ──────────
using (var scope = app.Services.CreateScope())
{
    var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    if (await auditLog.CheckConnectivityAsync())
    {
        logger.LogInformation("Verification database connection OK.");

        try
        {
            var proxyUsers = scope.ServiceProvider.GetRequiredService<IProxyUserService>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ProxyUser>>();
            var serviceAuthOptions = scope.ServiceProvider.GetRequiredService<IOptions<ServiceAuthOptions>>();
            await ProxyUserSeeder.SeedAdminIfEmptyAsync(proxyUsers, passwordHasher, serviceAuthOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not seed the initial ProxyUsers admin account — has sql/002_add_proxy_users_table.sql been run yet?");
        }
    }
    else
    {
        logger.LogWarning(
            "Verification database is unreachable — API transaction logging will fail until connectivity is restored. The service will continue to run.");
    }
}

// ─── Middleware pipeline ─────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serves the built React app — copy frontend/dist into wwwroot before
// `dotnet publish` (see the deploy workflow notes); not built automatically.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Client-side routes (react-router) that don't match a controller or a static
// file fall back to index.html so deep links / page refreshes keep working.
app.MapFallbackToFile("index.html");

app.Run();

// Exposes the generated Program class to WebApplicationFactory<Program> in the test project.
public partial class Program;
