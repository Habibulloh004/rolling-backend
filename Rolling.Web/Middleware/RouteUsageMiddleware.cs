using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Rolling.Web.Diagnostics;
using System.Diagnostics;

namespace Rolling.Web.Middleware;

public sealed class RouteUsageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RouteUsageStore _store;
    private readonly ILogger<RouteUsageMiddleware> _logger;

    public RouteUsageMiddleware(RequestDelegate next, RouteUsageStore store, ILogger<RouteUsageMiddleware> logger)
    {
        _next = next;
        _store = store;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            return; // skip non-matched requests (e.g., static files)
        }

        var routePattern = endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : endpoint.DisplayName ?? "unknown";

        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        var statusCode = context.Response?.StatusCode;

        _store.Increment(routePattern, path);
        _logger.LogInformation(
            "Route hit: {RoutePattern} Method: {Method} Status: {StatusCode} DurationMs: {Duration}",
            routePattern,
            method,
            statusCode,
            stopwatch.ElapsedMilliseconds);
    }
}
