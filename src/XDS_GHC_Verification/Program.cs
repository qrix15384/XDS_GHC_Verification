using XDS_GHC_Verification.Auth;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
builder.Services.Configure<AppInfoOptions>(builder.Configuration.GetSection(AppInfoOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));
builder.Services.Configure<UpstreamOptions>(builder.Configuration.GetSection(UpstreamOptions.SectionName));
builder.Services.Configure<SelfieOptions>(builder.Configuration.GetSection(SelfieOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<UpstreamClient>();
builder.Services.AddScoped<SelfieVerificationService>();
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
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

var app = builder.Build();

// ─── Startup: best-effort DB connectivity check ─────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    if (await auditLog.CheckConnectivityAsync())
    {
        logger.LogInformation("Verification database connection OK.");
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
app.MapControllers();

app.Run();
