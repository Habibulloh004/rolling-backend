using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using System.Text.Json;

namespace Rolling.Infrastructure.Payments;

public sealed class ClickService
{
    private readonly AppDbContext _dbContext;
    private readonly ClickOptions _options;
    private readonly OrderProcessor _orderProcessor;
    private readonly ClickSignatureValidator _signatureValidator;
    private readonly ILogger<ClickService> _logger;

    public ClickService(
        AppDbContext dbContext,
        IOptions<ClickOptions> options,
        OrderProcessor orderProcessor,
        ILogger<ClickService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _orderProcessor = orderProcessor;
        _signatureValidator = new ClickSignatureValidator(_options);
        _logger = logger;
    }

    public async Task<ClickPrepareResponse> PrepareAsync(ClickPrepareRequest request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Transactions.FirstOrDefaultAsync(x => x.Id == request.MerchantTransId, cancellationToken);

        if (order is null)
        {
            return ClickPrepareResponse.Failure(ClickErrorCodes.TransactionNotFound, "Transaction not found");
        }

        var signaturePayload = new ClickSignatureValidator.ClickSignaturePayload(
            request.ClickTransId,
            request.ServiceId,
            order.Id,
            null,
            GetAmountForSignature(request),
            request.Action,
            GetSignTimeForSignature(request));

        if (!_signatureValidator.Validate(signaturePayload, request.SignString))
        {
            return ClickPrepareResponse.Failure(ClickErrorCodes.SignFailed, "Invalid sign");
        }

        if (request.Action != ClickAction.Prepare)
        {
            return ClickPrepareResponse.Failure(ClickErrorCodes.ActionNotFound, "Action not found");
        }

        if (!string.IsNullOrWhiteSpace(order.UserId))
        {
            var alreadyPaid = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserId == order.UserId &&
                    x.Provider == "click" &&
                    x.Status == (int)TransactionState.Paid,
                    cancellationToken);

            if (alreadyPaid is not null)
            {
                return ClickPrepareResponse.Failure(ClickErrorCodes.AlreadyPaid, "Already paid");
            }
        }

        if (request.Amount != order.Amount)
        {
            return ClickPrepareResponse.Failure(ClickErrorCodes.InvalidAmount, "Incorrect parameter amount");
        }

        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == request.ClickTransId, cancellationToken);

        if (transaction is not null && transaction.Status < 0)
        {
            return ClickPrepareResponse.Failure(ClickErrorCodes.TransactionCanceled, "Transaction canceled");
        }

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        order.TransactionId = request.ClickTransId;
        order.Status = (int)TransactionState.Pending;
        order.PerformTime = currentTime;
        order.PrepareId = currentTime.ToString();
        order.Provider = "click";
        order.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ClickPrepareResponse.Success(request.ClickTransId, request.MerchantTransId, currentTime.ToString());
    }

    public async Task<ClickCompleteResponse> CompleteAsync(ClickCompleteRequest request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Transactions.FirstOrDefaultAsync(x => x.Id == request.MerchantTransId, cancellationToken);

        if (order is null)
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.TransactionNotFound, "Transaction not found");
        }

        var signaturePayload = new ClickSignatureValidator.ClickSignaturePayload(
            request.ClickTransId,
            request.ServiceId,
            order.Id,
            request.MerchantPrepareId,
            GetAmountForSignature(request),
            request.Action,
            GetSignTimeForSignature(request));

        if (!_signatureValidator.Validate(signaturePayload, request.SignString))
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.SignFailed, "Invalid sign");
        }

        if (request.Action != ClickAction.Complete)
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.ActionNotFound, "Action not found");
        }

        if (string.IsNullOrWhiteSpace(request.MerchantPrepareId))
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.TransactionNotFound, "Transaction not found");
        }

        var prepared = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrepareId == request.MerchantPrepareId && x.Provider == "click", cancellationToken);

        if (prepared is null)
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.TransactionNotFound, "Transaction not found");
        }

        if (!string.IsNullOrWhiteSpace(order.UserId))
        {
            var alreadyPaid = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserId == order.UserId &&
                    x.Provider == "click" &&
                    x.Status == (int)TransactionState.Paid,
                    cancellationToken);

            if (alreadyPaid is not null)
            {
                return ClickCompleteResponse.Failure(ClickErrorCodes.AlreadyPaid, "Already paid for course");
            }
        }

        if (request.Amount != order.Amount)
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.InvalidAmount, "Incorrect parameter amount");
        }

        if (order.Status < 0)
        {
            return ClickCompleteResponse.Failure(ClickErrorCodes.TransactionCanceled, "Transaction canceled");
        }

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (request.ErrorCode < 0)
        {
            order.Status = (int)TransactionState.PendingCanceled;
            order.CancelTime = currentTime;
            order.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return ClickCompleteResponse.Failure(ClickErrorCodes.TransactionNotFound, "Transaction not found");
        }

        var processedOrderId = await _orderProcessor.ProcessAsync(order, cancellationToken);

        order.Status = (int)TransactionState.Paid;
        order.PerformTime = currentTime;
        order.OrderId = processedOrderId ?? string.Empty;
        order.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ClickCompleteResponse.Success(request.ClickTransId, request.MerchantTransId, currentTime.ToString());
    }

    private static string GetSignTimeForSignature(ClickPrepareRequest request) =>
        !string.IsNullOrWhiteSpace(request.SignTimeRaw)
            ? request.SignTimeRaw
            : request.SignTime.ToString(CultureInfo.InvariantCulture);

    private static string GetSignTimeForSignature(ClickCompleteRequest request) =>
        !string.IsNullOrWhiteSpace(request.SignTimeRaw)
            ? request.SignTimeRaw
            : request.SignTime.ToString(CultureInfo.InvariantCulture);

    private static string GetAmountForSignature(ClickPrepareRequest request) =>
        !string.IsNullOrWhiteSpace(request.AmountRaw)
            ? request.AmountRaw
            : request.Amount.ToString(CultureInfo.InvariantCulture);

    private static string GetAmountForSignature(ClickCompleteRequest request) =>
        !string.IsNullOrWhiteSpace(request.AmountRaw)
            ? request.AmountRaw
            : request.Amount.ToString(CultureInfo.InvariantCulture);

    public async Task<ClickCheckoutResult> CheckoutAsync(ClickCheckoutRequest request, CancellationToken cancellationToken)
    {
        var orderDetailsJson = request.OrderDetails?.ValueKind == JsonValueKind.Object
            ? request.OrderDetails.Value.GetRawText()
            : "{}";
        var now = DateTime.UtcNow;
        var cutoffMillis = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeMilliseconds();

        var pendingQuery = _dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.Provider == "click" &&
                x.Status == (int)TransactionState.Pending &&
                x.CreateTime > cutoffMillis &&
                x.Amount == request.Amount &&
                x.OrderDetailsJson == orderDetailsJson);

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            pendingQuery = pendingQuery.Where(x => x.UserId == request.UserId);
        }
        else
        {
            pendingQuery = pendingQuery.Where(x => x.UserId == null || x.UserId == string.Empty);
        }

        var existing = await pendingQuery
            .OrderByDescending(x => x.CreateTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            var existingCheckoutUrl = BuildCheckoutUrl(existing.Id, request.Amount, request.Url, request.OrderDetails);
            return new ClickCheckoutResult(existingCheckoutUrl, existing.Id);
        }

        var orderDoc = new PaymentTransaction
        {
            OrderDetailsJson = orderDetailsJson,
            Status = (int)TransactionState.Pending,
            Amount = request.Amount,
            Provider = "click",
            UserId = request.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _dbContext.Transactions.AddAsync(orderDoc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PaymentOrderBuilder.EnsureAwaitingPaymentOrderAsync(_dbContext, orderDoc, request.OrderDetails, cancellationToken);

        var checkoutUrl = BuildCheckoutUrl(orderDoc.Id, request.Amount, request.Url, request.OrderDetails);

        return new ClickCheckoutResult(checkoutUrl, orderDoc.Id);
    }

    private string BuildCheckoutUrl(string transactionId, decimal amount, string? returnUrl, JsonElement? orderDetails)
    {
        var baseUrl = returnUrl ?? string.Empty;
        if (TryGetServiceMode(orderDetails, out var serviceMode) && serviceMode != 1 && !string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"{baseUrl.TrimEnd('/')}/{transactionId}";
        }

        return
            $"{_options.CheckoutBaseUrl}?service_id={_options.ServiceId}&merchant_id={_options.MerchantId}&amount={amount:0.##}&transaction_param={transactionId}&merchant_order_id={_options.MerchantUserId}&return_url={Uri.EscapeDataString(baseUrl)}";
    }

    private static bool TryGetServiceMode(JsonElement? doc, out int serviceMode)
    {
        serviceMode = 0;
        if (doc is null || doc.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!doc.Value.TryGetProperty("service_mode", out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
        {
            serviceMode = numeric;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            serviceMode = parsed;
            return true;
        }

        return false;
    }

    public sealed record ClickPrepareRequest(
        string ClickTransId,
        string ServiceId,
        string MerchantTransId,
        decimal Amount,
        int Action,
        long SignTime,
        string SignString,
        string? AmountRaw,
        string? SignTimeRaw);

    public sealed record ClickCompleteRequest(
        string ClickTransId,
        string ServiceId,
        string MerchantTransId,
        string? MerchantPrepareId,
        decimal Amount,
        int Action,
        long SignTime,
        string SignString,
        int ErrorCode,
        string? AmountRaw,
        string? SignTimeRaw);

    public sealed record ClickCheckoutRequest(
        JsonElement? OrderDetails,
        decimal Amount,
        string? UserId,
        string? Url);

    public sealed record ClickPrepareResponse(string ClickTransactionId, string MerchantTransactionId, string MerchantPrepareId, int Error, string ErrorNote)
    {
        public static ClickPrepareResponse Success(string clickId, string merchantTransId, string prepareId) =>
            new(clickId, merchantTransId, prepareId, ClickErrorCodes.Success, "Success");

        public static ClickPrepareResponse Failure(int code, string note) =>
            new(string.Empty, string.Empty, string.Empty, code, note);
    }

    public sealed record ClickCompleteResponse(string ClickTransactionId, string MerchantTransactionId, string MerchantConfirmId, int Error, string ErrorNote)
    {
        public static ClickCompleteResponse Success(string clickId, string merchantTransId, string confirmId) =>
            new(clickId, merchantTransId, confirmId, ClickErrorCodes.Success, "Success");

        public static ClickCompleteResponse Failure(int code, string note) =>
            new(string.Empty, string.Empty, string.Empty, code, note);
    }

    public sealed record ClickCheckoutResult(string Url, string OrderId);
}
