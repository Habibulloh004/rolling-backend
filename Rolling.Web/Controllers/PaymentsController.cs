using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Payments;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Filters;
using Rolling.Web.Models.Payments;
using Microsoft.Extensions.Primitives;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class PaymentsController : ControllerBase
{
    private static readonly JsonSerializerOptions PaymeSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ClickService _clickService;
    private readonly PaymeService _paymeService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(ClickService clickService, PaymeService paymeService, AppDbContext dbContext, ILogger<PaymentsController> logger)
    {
        _clickService = clickService;
        _paymeService = paymeService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost("click/prepare")]
    public async Task<IActionResult> ClickPrepareAsync(CancellationToken cancellationToken)
    {
        var (request, rawPayload) = await BindClickPrepareRequestAsync(cancellationToken);

        _logger.LogInformation(
            "[CLICK PREPARE] Received request: ClickTransId={ClickTransId}, ServiceId={ServiceId}, MerchantTransId={MerchantTransId}, Amount={Amount}, Action={Action}, SignTime={SignTime}",
            request.ClickTransactionId,
            request.ServiceId,
            request.MerchantTransactionId,
            request.Amount,
            request.Action,
            request.SignTime);
        _logger.LogInformation("[CLICK PREPARE] Raw request payload: {Payload}", rawPayload ?? "<empty>");

        var result = await _clickService.PrepareAsync(
            new ClickService.ClickPrepareRequest(
                request.ClickTransactionId,
                request.ServiceId,
                request.MerchantTransactionId,
                request.Amount,
                request.Action,
                request.SignTime,
                request.SignString,
                request.AmountRaw,
                request.SignTimeRaw),
            cancellationToken);

        _logger.LogInformation(
            "[CLICK PREPARE] Response: ClickTransId={ClickTransId}, MerchantTransId={MerchantTransId}, PrepareId={PrepareId}, Error={Error}, ErrorNote={ErrorNote}",
            result.ClickTransactionId,
            result.MerchantTransactionId,
            result.MerchantPrepareId,
            result.Error,
            result.ErrorNote);

        var responsePayload = new
        {
            click_trans_id = result.ClickTransactionId,
            merchant_trans_id = result.MerchantTransactionId,
            merchant_prepare_id = result.MerchantPrepareId,
            error = result.Error,
            error_note = result.ErrorNote
        };

        _logger.LogInformation("[CLICK PREPARE] Response payload: {Payload}", JsonSerializer.Serialize(responsePayload));

        return Ok(responsePayload);
    }

    [HttpPost("click/complete")]
    public async Task<IActionResult> ClickCompleteAsync(CancellationToken cancellationToken)
    {
        var (request, rawPayload) = await BindClickCompleteRequestAsync(cancellationToken);

        _logger.LogInformation("[CLICK COMPLETE] Raw request payload: {Payload}", rawPayload ?? "<empty>");
        _logger.LogInformation(
            "[CLICK COMPLETE] Received request: ClickTransId={ClickTransId}, ServiceId={ServiceId}, MerchantTransId={MerchantTransId}, MerchantPrepareId={MerchantPrepareId}, Amount={Amount}, Action={Action}, SignTime={SignTime}, ErrorCode={ErrorCode}",
            request.ClickTransactionId,
            request.ServiceId,
            request.MerchantTransactionId,
            request.MerchantPrepareId,
            request.Amount,
            request.Action,
            request.SignTime,
            request.ErrorCode);

        var result = await _clickService.CompleteAsync(
            new ClickService.ClickCompleteRequest(
                request.ClickTransactionId,
                request.ServiceId,
                request.MerchantTransactionId,
                request.MerchantPrepareId,
                request.Amount,
                request.Action,
                request.SignTime,
                request.SignString,
                request.ErrorCode,
                request.AmountRaw,
                request.SignTimeRaw),
            cancellationToken);

        _logger.LogInformation(
            "[CLICK COMPLETE] Response: ClickTransId={ClickTransId}, MerchantTransId={MerchantTransId}, ConfirmId={ConfirmId}, Error={Error}, ErrorNote={ErrorNote}",
            result.ClickTransactionId,
            result.MerchantTransactionId,
            result.MerchantConfirmId,
            result.Error,
            result.ErrorNote);

        var responsePayload = new
        {
            click_trans_id = result.ClickTransactionId,
            merchant_trans_id = result.MerchantTransactionId,
            merchant_confirm_id = result.MerchantConfirmId,
            error = result.Error,
            error_note = result.ErrorNote
        };

        _logger.LogInformation("[CLICK COMPLETE] Response payload: {Payload}", JsonSerializer.Serialize(responsePayload));

        return Ok(responsePayload);
    }

    [HttpPost("click/checkout")]
    public async Task<IActionResult> ClickCheckoutAsync([FromBody] ClickCheckoutRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _clickService.CheckoutAsync(
            new ClickService.ClickCheckoutRequest(
                request.OrderDetails,
                request.Amount,
                request.UserId,
                request.Url),
            cancellationToken);

        return Ok(new { url = result.Url, order_id = result.OrderId });
    }

    /// <summary>
    /// Helper endpoint for testing: creates a fake Click transaction.
    /// </summary>
    [HttpPost("click/fake-transaction")]
    public async Task<IActionResult> ClickFakeTransactionAsync([FromBody] ClickFakeTransactionRequestDto? request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var payload = request ?? new ClickFakeTransactionRequestDto();

        var status = payload.Status ?? 0;
        var createTime = payload.CreateTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var performTime = payload.PerformTime ?? 0;
        var cancelTime = payload.CancelTime ?? 0;

        var transaction = new PaymentTransaction
        {
            TransactionId = payload.TransactionId,
            UserId = payload.UserId,
            OrderDetailsJson = SerializeOrderDetails(payload.OrderDetails),
            Status = status,
            Amount = payload.Amount ?? 0,
            OrderId = payload.OrderId,
            CreateTime = createTime,
            PerformTime = performTime,
            CancelTime = cancelTime,
            Reason = payload.Reason,
            Provider = "click",
            PrepareId = payload.PrepareId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _dbContext.Transactions.AddAsync(transaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        object? orderDetails = null;
        if (!string.IsNullOrWhiteSpace(transaction.OrderDetailsJson))
        {
            try
            {
                orderDetails = JsonSerializer.Deserialize<object>(transaction.OrderDetailsJson);
            }
            catch (JsonException)
            {
                orderDetails = transaction.OrderDetailsJson;
            }
        }

        return Ok(new
        {
            transaction.Id,
            transaction.TransactionId,
            transaction.UserId,
            orderDetails,
            transaction.Status,
            transaction.Amount,
            transaction.OrderId,
            transaction.CreateTime,
            transaction.PerformTime,
            transaction.CancelTime,
            transaction.Reason,
            transaction.Provider,
            transaction.PrepareId,
            transaction.CreatedAt,
            transaction.UpdatedAt
        });
    }

    private async Task<(ClickPrepareRequestDto Request, string? RawPayload)> BindClickPrepareRequestAsync(CancellationToken cancellationToken)
    {
        var rawBody = await ReadBodyAsStringAsync(Request, cancellationToken);
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var dtoFromForm = MapClickPrepareFromForm(form);
            return (dtoFromForm, rawBody ?? SerializeForm(form));
        }

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var parsed = MapClickPrepareFromJson(rawBody);
                return (parsed, rawBody);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[CLICK PREPARE] Failed to parse request body as JSON.");
            }
        }

        return (new ClickPrepareRequestDto(), rawBody);
    }

    private async Task<(ClickCompleteRequestDto Request, string? RawPayload)> BindClickCompleteRequestAsync(CancellationToken cancellationToken)
    {
        var rawBody = await ReadBodyAsStringAsync(Request, cancellationToken);
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var dtoFromForm = MapClickCompleteFromForm(form);
            return (dtoFromForm, rawBody ?? SerializeForm(form));
        }

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var parsed = MapClickCompleteFromJson(rawBody);
                return (parsed, rawBody);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[CLICK COMPLETE] Failed to parse request body as JSON.");
            }
        }

        return (new ClickCompleteRequestDto(), rawBody);
    }

    private static ClickPrepareRequestDto MapClickPrepareFromForm(IFormCollection form)
    {
        var amountRaw = GetString(form, "amount");
        var signTimeRaw = GetString(form, "sign_time");

        return new ClickPrepareRequestDto
        {
            ClickTransactionId = GetString(form, "click_trans_id"),
            ServiceId = GetString(form, "service_id"),
            MerchantTransactionId = GetString(form, "merchant_trans_id"),
            AmountRaw = amountRaw,
            Amount = ParseDecimal(amountRaw),
            Action = ParseInt(GetString(form, "action")),
            SignTimeRaw = signTimeRaw,
            SignTime = ParseLong(signTimeRaw),
            SignString = GetString(form, "sign_string")
        };
    }

    private static ClickCompleteRequestDto MapClickCompleteFromForm(IFormCollection form)
    {
        var amountRaw = GetString(form, "amount");
        var signTimeRaw = GetString(form, "sign_time");

        return new ClickCompleteRequestDto
        {
            ClickTransactionId = GetString(form, "click_trans_id"),
            ServiceId = GetString(form, "service_id"),
            MerchantTransactionId = GetString(form, "merchant_trans_id"),
            MerchantPrepareId = GetNullableString(form, "merchant_prepare_id"),
            AmountRaw = amountRaw,
            Amount = ParseDecimal(amountRaw),
            Action = ParseInt(GetString(form, "action")),
            SignTimeRaw = signTimeRaw,
            SignTime = ParseLong(signTimeRaw),
            SignString = GetString(form, "sign_string"),
            ErrorCode = ParseInt(GetString(form, "error"))
        };
    }

    private static ClickPrepareRequestDto MapClickPrepareFromJson(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var amountRaw = GetRaw(root, "amount");
        var signTimeRaw = GetRaw(root, "sign_time");

        return new ClickPrepareRequestDto
        {
            ClickTransactionId = GetString(root, "click_trans_id"),
            ServiceId = GetString(root, "service_id"),
            MerchantTransactionId = GetString(root, "merchant_trans_id"),
            AmountRaw = amountRaw,
            Amount = ParseDecimal(amountRaw),
            Action = ParseInt(GetRaw(root, "action")),
            SignTimeRaw = signTimeRaw,
            SignTime = ParseLong(signTimeRaw),
            SignString = GetString(root, "sign_string")
        };
    }

    private static ClickCompleteRequestDto MapClickCompleteFromJson(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var amountRaw = GetRaw(root, "amount");
        var signTimeRaw = GetRaw(root, "sign_time");

        return new ClickCompleteRequestDto
        {
            ClickTransactionId = GetString(root, "click_trans_id"),
            ServiceId = GetString(root, "service_id"),
            MerchantTransactionId = GetString(root, "merchant_trans_id"),
            MerchantPrepareId = GetNullableString(root, "merchant_prepare_id"),
            AmountRaw = amountRaw,
            Amount = ParseDecimal(amountRaw),
            Action = ParseInt(GetRaw(root, "action")),
            SignTimeRaw = signTimeRaw,
            SignTime = ParseLong(signTimeRaw),
            SignString = GetString(root, "sign_string"),
            ErrorCode = ParseInt(GetRaw(root, "error"))
        };
    }

    private static string SerializeForm(IFormCollection form)
    {
        var asDict = form.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        return JsonSerializer.Serialize(asDict);
    }

    private static async Task<string?> ReadBodyAsStringAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        return body;
    }

    private static decimal ParseDecimal(StringValues value) =>
        ParseDecimal(value.ToString());

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static int ParseInt(StringValues value) =>
        ParseInt(value.ToString());

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static long ParseLong(string? value)
    {
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTime))
        {
            return dateTime.ToUnixTimeMilliseconds();
        }

        return 0;
    }

    private static string GetString(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) ? value.ToString() ?? string.Empty : string.Empty;

    private static string? GetNullableString(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? GetRaw(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    private static string GetString(JsonElement root, string property) =>
        GetRaw(root, property) ?? string.Empty;

    private static string? GetNullableString(JsonElement root, string property) =>
        GetRaw(root, property);

    [HttpPost("payme/pay")]
    [PaymeAuthorize]
    public async Task<IActionResult> PaymeAsync([FromBody] PaymeRpcRequest request, CancellationToken cancellationToken)
    {
        object? rpcId = null;
        string? idAsString = null;

        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Method))
            {
                // Handle completely missing or invalid JSON-RPC request
                return Ok(new
                {
                    error = new
                    {
                        code = -32600,
                        message = "Invalid request",
                        data = (object?)null
                    },
                    id = rpcId
                });
            }

            rpcId = ToRpcId(request.Id);
            idAsString = ToIdString(request.Id);

            switch (request.Method)
            {
                case PaymeMethod.CheckPerformTransaction:
                    await _paymeService.CheckPerformTransactionAsync(Deserialize<PaymeService.PaymeCheckPerformParams>(request.Params), idAsString, cancellationToken);
                    return Ok(new { result = new { allow = true }, id = rpcId });

                case PaymeMethod.CheckTransaction:
                {
                    var info = await _paymeService.CheckTransactionAsync(Deserialize<PaymeService.PaymeTransactionIdParams>(request.Params), idAsString, cancellationToken);
                    return Ok(new { result = ToResult(info), id = rpcId });
                }
                case PaymeMethod.CreateTransaction:
                {
                    var info = await _paymeService.CreateTransactionAsync(Deserialize<PaymeService.PaymeCreateTransactionParams>(request.Params), idAsString, cancellationToken);
                    return Ok(new { result = ToResult(info), id = rpcId });
                }
                case PaymeMethod.PerformTransaction:
                {
                    var info = await _paymeService.PerformTransactionAsync(Deserialize<PaymeService.PaymeTransactionIdParams>(request.Params), idAsString, cancellationToken);
                    return Ok(new { result = ToResult(info), id = rpcId });
                }
                case PaymeMethod.CancelTransaction:
                {
                    var info = await _paymeService.CancelTransactionAsync(Deserialize<PaymeService.PaymeCancelTransactionParams>(request.Params), idAsString, cancellationToken);
                    return Ok(new { result = ToResult(info), id = rpcId });
                }
                case PaymeMethod.GetStatement:
                {
                    var items = await _paymeService.GetStatementAsync(Deserialize<PaymeService.PaymeStatementParams>(request.Params), cancellationToken);
                    return Ok(new
                    {
                        result = new
                        {
                            transactions = items.Select(ToStatementItem)
                        }
                    });
                }
                default:
                    // For Payme compatibility, always return HTTP 200 with a JSON-RPC style error
                    return Ok(new
                    {
                        error = new
                        {
                            code = -32601,
                            message = "Unsupported method",
                            data = (object?)null
                        },
                        id = rpcId
                    });
            }
        }
        catch (PaymentTransactionException ex)
        {
            return Ok(new
            {
                error = new
                {
                    code = ex.Descriptor.Code,
                    message = ex.Descriptor.Message,
                    data = ex.DataPayload
                },
                id = rpcId ?? ex.TransactionId
            });
        }
        catch (InvalidOperationException ex)
        {
            // Handle invalid params / missing payload with a JSON-RPC style error but HTTP 200
            return Ok(new
            {
                error = new
                {
                    code = -32602,
                    message = ex.Message,
                    data = (object?)null
                },
                id = rpcId
            });
        }
        catch (JsonException ex)
        {
            // Handle malformed JSON with a JSON-RPC style error but HTTP 200
            return Ok(new
            {
                error = new
                {
                    code = -32700,
                    message = ex.Message,
                    data = (object?)null
                },
                id = rpcId
            });
        }
    }

    [HttpPost("payme/checkout")]
    public async Task<IActionResult> PaymeCheckoutAsync([FromBody] PaymeCheckoutRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _paymeService.CreateCheckoutAsync(
            new PaymeService.PaymeCheckoutRequest(request.OrderDetails, request.Amount, request.UserId, request.Url),
            cancellationToken);

        return Ok(new { url = result.Url, order_id = result.OrderId });
    }

    /// <summary>
    /// Helper endpoint for testing: creates a fake paid Payme transaction.
    /// </summary>
    [HttpPost("payme/fake-transaction")]
    public async Task<IActionResult> PaymeFakeTransactionAsync([FromBody] PaymeFakeTransactionRequestDto? request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var payload = request ?? new PaymeFakeTransactionRequestDto();

        var status = payload.Status ?? 0;
        var createTime = payload.CreateTime ?? 0;
        var performTime = payload.PerformTime ?? 0;
        var cancelTime = payload.CancelTime ?? 0;

        var transaction = new PaymentTransaction
        {
            TransactionId = payload.TransactionId,
            UserId = payload.UserId,
            OrderDetailsJson = SerializeOrderDetails(payload.OrderDetails),
            Status = status,
            Amount = payload.Amount ?? 0,
            OrderId = payload.OrderId,
            CreateTime = createTime,
            PerformTime = performTime,
            CancelTime = cancelTime,
            Reason = payload.Reason,
            Provider = string.IsNullOrWhiteSpace(payload.Provider) ? "payme" : payload.Provider,
            PrepareId = payload.PrepareId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _dbContext.Transactions.AddAsync(transaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        object? orderDetails = null;
        if (!string.IsNullOrWhiteSpace(transaction.OrderDetailsJson))
        {
            try
            {
                orderDetails = JsonSerializer.Deserialize<object>(transaction.OrderDetailsJson);
            }
            catch (JsonException)
            {
                orderDetails = transaction.OrderDetailsJson;
            }
        }

        return Ok(new
        {
            transaction.Id,
            transaction.TransactionId,
            transaction.UserId,
            orderDetails,
            transaction.Status,
            transaction.Amount,
            transaction.OrderId,
            transaction.CreateTime,
            transaction.PerformTime,
            transaction.CancelTime,
            transaction.Reason,
            transaction.Provider,
            transaction.PrepareId,
            transaction.CreatedAt,
            transaction.UpdatedAt
        });
    }

    private static T Deserialize<T>(JsonElement? element)
    {
        if (element is null)
        {
            throw new InvalidOperationException("Params payload is required.");
        }

        return element.Value.Deserialize<T>(PaymeSerializerOptions) ?? throw new InvalidOperationException("Unable to parse params payload.");
    }

    private static object ToResult(PaymeService.PaymeTransactionInfo info) => new
    {
        create_time = info.CreateTime,
        perform_time = info.PerformTime,
        cancel_time = info.CancelTime,
        transaction = info.TransactionId,
        state = info.State,
        reason = NormalizeReason(info.Reason)
    };

    private static object ToStatementItem(PaymeService.PaymeStatementItem item) => new
    {
        transaction_id = item.TransactionId,
        time = item.Time,
        amount = item.Amount,
        account = new { order_id = item.AccountOrderId },
        create_time = item.CreateTime,
        perform_time = item.PerformTime,
        cancel_time = item.CancelTime,
        transaction = item.Transaction,
        state = item.State,
        reason = NormalizeReason(item.Reason)
    };

    private static int? NormalizeReason(int? reason)
    {
        if (reason is null || reason == 0)
        {
            return null;
        }

        return reason;
    }

    private static string? SerializeOrderDetails(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return element.Value.GetRawText();
    }

    private static object? ToRpcId(JsonElement? id)
    {
        if (id is null || id.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var value = id.Value;
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

    private static string? ToIdString(JsonElement? id)
    {
        if (id is null || id.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var value = id.Value;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };
    }
}
