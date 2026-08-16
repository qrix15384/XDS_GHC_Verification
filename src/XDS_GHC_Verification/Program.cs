using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<UpstreamClient>();
builder.Services.AddScoped<SelfieVerificationService>();
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IProxyUserService, ProxyUserService>();
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

var app = builder.Build();

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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the generated Program class to WebApplicationFactory<Program> in the test project.
public partial class Program;
