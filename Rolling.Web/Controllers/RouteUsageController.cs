using Microsoft.AspNetCore.Mvc;
using Rolling.Web.Auth;
using Rolling.Web.Diagnostics;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/route-usage")]
[AdminAuthorize]
public sealed class RouteUsageController : ControllerBase
{
    private readonly RouteUsageStore _store;

    public RouteUsageController(RouteUsageStore store)
    {
        _store = store;
    }

    [HttpGet]
    public ActionResult<IEnumerable<RouteUsageSnapshot>> GetUsage()
    {
        var snapshot = _store.GetSnapshot();
        return Ok(snapshot);
    }
}
