using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Payments;
using Rolling.Web.Models.Payments;

namespace Rolling.Web.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PaymeAuthorizeAttribute : TypeFilterAttribute
{
    public PaymeAuthorizeAttribute() : base(typeof(PaymeAuthorizationFilter))
    {
    }

    private sealed class PaymeAuthorizationFilter : IAsyncActionFilter
    {
        private readonly PaymeOptions _options;

        public PaymeAuthorizationFilter(IOptions<PaymeOptions> options)
        {
            _options = options.Value;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.ActionArguments.Values.OfType<PaymeRpcRequest>().FirstOrDefault();
            var id = ConvertRpcId(request?.Id);
            var authorization = context.HttpContext.Request.Headers["Authorization"].ToString();

            if (string.IsNullOrWhiteSpace(authorization))
            {
                context.Result = BuildErrorResult(id);
                return;
            }

            var token = authorization.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                context.Result = BuildErrorResult(id);
                return;
            }

            try
            {
                var data = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                if (string.IsNullOrWhiteSpace(_options.MerchantKey) || !data.Contains(_options.MerchantKey, StringComparison.Ordinal))
                {
                    context.Result = BuildErrorResult(id);
                    return;
                }
            }
            catch (FormatException)
            {
                context.Result = BuildErrorResult(id);
                return;
            }

            await next();
        }

        private IActionResult BuildErrorResult(object? id)
        {
            var descriptor = PaymeErrors.InvalidAuthorization;
            return new JsonResult(new
            {
                error = new
                {
                    code = descriptor.Code,
                    message = descriptor.Message,
                    data = (object?)null
                },
                id
            });
        }

        private static object? ConvertRpcId(JsonElement? element)
        {
            if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            var value = element.Value;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var longValue)
                    ? longValue
                    : value.TryGetUInt64(out var ulongValue)
                        ? ulongValue
                        : value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => JsonSerializer.Deserialize<object>(value.GetRawText())
            };
        }
    }
}
