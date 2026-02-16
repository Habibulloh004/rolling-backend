using Microsoft.AspNetCore.Http;

namespace Rolling.Web.Auth;

public static class AdminAuthHttpContextExtensions
{
    private const string SessionItemKey = "AdminAuth.Session";

    public static void SetAdminSession(this HttpContext httpContext, AdminAuthSession session)
    {
        httpContext.Items[SessionItemKey] = session;
    }

    public static AdminAuthSession? GetAdminSession(this HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue(SessionItemKey, out var session) ? session as AdminAuthSession : null;
    }
}
