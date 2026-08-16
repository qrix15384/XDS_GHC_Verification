using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Utils;

namespace XDS_GHC_Verification.Auth;

/// <summary>
/// Validates the incoming X-API-Key header against the configured
/// ServiceAuth:ApiKey. Applied via [ServiceFilter(typeof(ApiKeyAuthFilter))]
/// on controllers that require it — everything except /health and login.
/// </summary>
public class ApiKeyAuthFilter(IOptions<ServiceAuthOptions> options) : IAsyncActionFilter
{
    private const string HeaderName = "X-API-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrEmpty(provided))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new { detail = "Missing API key. Provide it via the 'X-API-Key' header." })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        if (!SecureCompare.Equals(provided.ToString(), options.Value.ApiKey))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new { detail = "Invalid API key." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
