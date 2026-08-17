using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Auth;

public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the authenticated caller's username (from a JWT bearer token,
    /// if one was presented alongside X-API-Key) for audit-log attribution,
    /// or the given fallback for callers that only sent X-API-Key.
    /// </summary>
    public static string ResolveAuditUsername(this HttpContext context, string fallback) =>
        context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? fallback
            : fallback;

    /// <summary>
    /// Returns the authenticated caller's subscriber (organization), if their
    /// JWT carries one, so audit-log rows can be attributed to it. Null for
    /// callers with no bearer token, or whose account has no subscriber assigned.
    /// </summary>
    public static (int? SubscriberId, string? SubscriberName) ResolveAuditSubscriber(this HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return (null, null);
        }

        var idClaim = context.User.FindFirst(JwtTokenService.SubscriberIdClaimType)?.Value;
        var nameClaim = context.User.FindFirst(JwtTokenService.SubscriberNameClaimType)?.Value;
        return int.TryParse(idClaim, out var subscriberId) ? (subscriberId, nameClaim) : (null, null);
    }
}
