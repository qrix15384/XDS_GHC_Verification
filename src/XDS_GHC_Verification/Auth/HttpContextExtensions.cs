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
}
