using Microsoft.AspNetCore.Mvc;

namespace Rolling.Web.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminAuthorizeAttribute : TypeFilterAttribute
{
    public AdminAuthorizeAttribute() : base(typeof(AdminAuthorizeFilter))
    {
    }
}
