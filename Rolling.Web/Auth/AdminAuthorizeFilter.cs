using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Rolling.Web.Auth;

public sealed class AdminAuthorizeFilter : IAsyncAuthorizationFilter
{
    private readonly AdminAuthService _authService;

    public AdminAuthorizeFilter(AdminAuthService authService)
    {
        _authService = authService;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = BuildUnauthorizedResult();
            return Task.CompletedTask;
        }

        var token = authorization[7..].Trim();
        var session = _authService.ValidateAccessToken(token);

        if (session is null)
        {
            context.Result = BuildUnauthorizedResult();
            return Task.CompletedTask;
        }

        context.HttpContext.SetAdminSession(session);
        return Task.CompletedTask;
    }

    private static JsonResult BuildUnauthorizedResult()
    {
        return new JsonResult(new { error = "Unauthorized" })
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };
    }
}
