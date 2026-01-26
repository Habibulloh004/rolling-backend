using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Payments;

public sealed class PaymeService
{
    private const int ExpirationMinutes = 720; // 12 hours = 720 minutes

    private readonly AppDbContext _dbContext;
    private readonly PaymeOptions _options;
    private readonly OrderProcessor _orderProcessor;
    private readonly ILogger<PaymeService> _logger;

    public PaymeService(
        AppDbContext dbContext,
        IOptions<PaymeOptions> options,
        OrderProcessor orderProcessor,
        ILogger<PaymeService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _orderProcessor = orderProcessor;
        _logger = logger;
    }

    public async Task CheckPerformTransactionAsync(PaymeCheckPerformParams parameters, string? id, CancellationToken cancellationToken)
    {
        var orderId = parameters.Account.OrderId ?? string.Empty;
        var amount = NormalizeAmount(parameters.Amount);
        var order = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == orderId &&
                x.Provider == "payme",
                cancellationToken);

        if (order is null)
        {
            var orderIdLower = orderId.ToLower();
            var caseInsensitiveOrder = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Provider == "payme" &&
                    x.Id.ToLower() == orderIdLower,
                    cancellationToken);

            _logger.LogWarning(
                "[Payme] CheckPerformTransaction failed: order not found. OrderId={OrderId} CaseInsensitiveMatchId={CaseInsensitiveMatchId}",
                orderId,
                caseInsensitiveOrder?.Id);
            throw new PaymentTransactionException(PaymeErrors.OrderNotFound, id, "order_id");
        }

        if (order.Amount != amount)
        {
            _logger.LogWarning(
                "[Payme] CheckPerformTransaction failed: amount mismatch. OrderId={OrderId} ExpectedAmount={ExpectedAmount} ReceivedAmount={ReceivedAmount} RawAmount={RawAmount}",
                order.Id,
                order.Amount,
                amount,
                parameters.Amount);
            throw new PaymentTransactionException(PaymeErrors.InvalidAmount, id);
        }
    }

    public async Task<PaymeTransactionInfo> CheckTransactionAsync(PaymeTransactionIdParams parameters, string? id, CancellationToken cancellationToken)
    {
        var transactionId = parameters.Id ?? string.Empty;
        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId, cancellationToken);

        if (transaction is null)
        {
            var transactionIdLower = transactionId.ToLower();
            var caseInsensitiveTransaction = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TransactionId != null &&
                    x.TransactionId.ToLower() == transactionIdLower,
                    cancellationToken);

            _logger.LogWarning(
                "[Payme] CheckTransaction failed: transaction not found. TransactionId={TransactionId} CaseInsensitiveMatchId={CaseInsensitiveMatchId}",
                transactionId,
                caseInsensitiveTransaction?.TransactionId);
            throw new PaymentTransactionException(PaymeErrors.TransactionNotFound, id);
        }

        return MapTransactionInfo(transaction);
    }

    public async Task<PaymeTransactionInfo> CreateTransactionAsync(PaymeCreateTransactionParams parameters, string? id, CancellationToken cancellationToken)
    {
        var orderId = parameters.Account.OrderId ?? string.Empty;
        var transactionId = parameters.Id ?? string.Empty;
        await CheckPerformTransactionAsync(new PaymeCheckPerformParams(parameters.Amount, parameters.Account), id, cancellationToken);

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var existingTransaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId, cancellationToken);

        if (existingTransaction is not null)
        {
            if (existingTransaction.Status != (int)TransactionState.Pending)
            {
                _logger.LogWarning(
                    "[Payme] CreateTransaction rejected: existing transaction not pending. TransactionId={TransactionId} Status={Status}",
                    transactionId,
                    existingTransaction.Status);
                throw new PaymentTransactionException(PaymeErrors.CantDoOperation, id);
            }

            if (!IsWithinExpiration(existingTransaction.CreateTime, currentTime))
            {
                _logger.LogWarning(
                    "[Payme] CreateTransaction rejected: existing transaction expired. TransactionId={TransactionId} CreateTime={CreateTime} CurrentTime={CurrentTime}",
                    transactionId,
                    existingTransaction.CreateTime,
                    currentTime);
                existingTransaction.Status = (int)TransactionState.PendingCanceled;
                existingTransaction.Reason = 4;
                existingTransaction.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                throw new PaymentTransactionException(PaymeErrors.CantDoOperation, id);
            }

            return MapTransactionInfo(existingTransaction);
        }

        var originalOrder = await _dbContext.Transactions
            .FirstOrDefaultAsync(x => x.Id == orderId && x.Provider == "payme", cancellationToken);

        if (originalOrder is null)
        {
            var orderIdLower = orderId.ToLower();
            var caseInsensitiveOrder = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Provider == "payme" &&
                    x.Id.ToLower() == orderIdLower,
                    cancellationToken);

            _logger.LogWarning(
                "[Payme] CreateTransaction failed: order not found. OrderId={OrderId} CaseInsensitiveMatchId={CaseInsensitiveMatchId}",
                orderId,
                caseInsensitiveOrder?.Id);
            throw new PaymentTransactionException(PaymeErrors.OrderNotFound, id, "order_id");
        }

        if (originalOrder.Status == (int)TransactionState.Paid)
        {
            _logger.LogWarning(
                "[Payme] CreateTransaction rejected: order already paid. OrderId={OrderId} Status={Status}",
                originalOrder.Id,
                originalOrder.Status);
            throw new PaymentTransactionException(PaymeErrors.AlreadyDone, id);
        }

        if (originalOrder.Status == (int)TransactionState.Pending)
        {
            _logger.LogWarning(
                "[Payme] CreateTransaction rejected: order already pending. OrderId={OrderId} Status={Status}",
                originalOrder.Id,
                originalOrder.Status);
            throw new PaymentTransactionException(PaymeErrors.Pending, id);
        }

        originalOrder.TransactionId = transactionId;
        originalOrder.Status = (int)TransactionState.Pending;
        originalOrder.CreateTime = parameters.Time;
        originalOrder.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Payme] CreateTransaction linked order. OrderId={OrderId} TransactionId={TransactionId} CreateTime={CreateTime}",
            originalOrder.Id,
            transactionId,
            originalOrder.CreateTime);

        return MapTransactionInfo(originalOrder);
    }

    public async Task<PaymeTransactionInfo> PerformTransactionAsync(PaymeTransactionIdParams parameters, string? id, CancellationToken cancellationToken)
    {
        var transactionId = parameters.Id ?? string.Empty;
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId, cancellationToken);

        if (transaction is null)
        {
            var transactionIdLower = transactionId.ToLower();
            var caseInsensitiveTransaction = await _dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TransactionId != null &&
                    x.TransactionId.ToLower() == transactionIdLower,
                    cancellationToken);

            _logger.LogWarning(
                "[Payme] PerformTransaction failed: transaction not found. TransactionId={TransactionId} CaseInsensitiveMatchId={CaseInsensitiveMatchId}",
                transactionId,
                caseInsensitiveTransaction?.TransactionId);
            throw new PaymentTransactionException(PaymeErrors.TransactionNotFound, id);
        }

        if (transaction.Status != (int)TransactionState.Pending)
        {
            if (transaction.Status != (int)TransactionState.Paid)
            {
                _logger.LogWarning(
                    "[Payme] PerformTransaction rejected: invalid state. TransactionId={TransactionId} Status={Status}",
                    transactionId,
                    transaction.Status);
                throw new PaymentTransactionException(PaymeErrors.CantDoOperation, id);
            }

            _logger.LogInformation(
                "[Payme] PerformTransaction skipped: already paid. TransactionId={TransactionId}",
                transactionId);
            return MapTransactionInfo(transaction);
        }

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!IsWithinExpiration(transaction.CreateTime, currentTime))
        {
            _logger.LogWarning(
                "[Payme] PerformTransaction rejected: transaction expired. TransactionId={TransactionId} CreateTime={CreateTime} CurrentTime={CurrentTime}",
                transactionId,
                transaction.CreateTime,
                currentTime);
            transaction.Status = (int)TransactionState.PendingCanceled;
            transaction.Reason = 4;
            transaction.CancelTime = currentTime;
            transaction.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new PaymentTransactionException(PaymeErrors.CantDoOperation, id);
        }

        var processedOrderId = await _orderProcessor.ProcessAsync(transaction, cancellationToken);
        if (string.IsNullOrWhiteSpace(processedOrderId))
        {
            _logger.LogWarning(
                "[Payme] PerformTransaction processed without poster order id. TransactionId={TransactionId} OrderId={OrderId}",
                transactionId,
                transaction.Id);
        }

        transaction.Status = (int)TransactionState.Paid;
        transaction.PerformTime = currentTime;
        transaction.OrderId = processedOrderId ?? string.Empty;
        transaction.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapTransactionInfo(transaction);
    }

    public async Task<PaymeTransactionInfo> CancelTransactionAsync(PaymeCancelTransactionParams parameters, string? id, CancellationToken cancellationToken)
    {
        var transactionId = parameters.Id ?? string.Empty;
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId, cancellationToken);

        if (transaction is null)
        {
            _logger.LogWarning(
                "[Payme] CancelTransaction failed: transaction not found. TransactionId={TransactionId}",
                transactionId);
            throw new PaymentTransactionException(PaymeErrors.TransactionNotFound, id);
        }

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (transaction.Status > 0)
        {
            transaction.Status = -Math.Abs(transaction.Status);
            transaction.Reason = parameters.Reason;
            transaction.CancelTime = currentTime;
            transaction.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new PaymeTransactionInfo(
            transaction.TransactionId ?? string.Empty,
            transaction.CreateTime,
            transaction.PerformTime,
            transaction.CancelTime == 0 ? currentTime : transaction.CancelTime,
            -Math.Abs(transaction.Status),
            parameters.Reason);
    }

    public async Task<IReadOnlyCollection<PaymeStatementItem>> GetStatementAsync(PaymeStatementParams parameters, CancellationToken cancellationToken)
    {
        var transactions = await _dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.CreateTime >= parameters.From &&
                x.CreateTime <= parameters.To &&
                x.Provider == "payme")
            .ToListAsync(cancellationToken);

        return transactions.Select(transaction => new PaymeStatementItem(
            transaction.TransactionId ?? string.Empty,
            transaction.CreateTime,
            (long)transaction.Amount,
            transaction.Id,
            transaction.CreateTime,
            transaction.PerformTime,
            transaction.CancelTime,
            transaction.TransactionId ?? string.Empty,
            transaction.Status,
            transaction.Reason)).ToList();
    }

    public async Task<PaymeCheckoutResult> CreateCheckoutAsync(PaymeCheckoutRequest request, CancellationToken cancellationToken)
    {
        var hasServiceMode = TryGetServiceMode(request.OrderDetails, out var serviceMode);
        _logger.LogInformation(
            "[Payme] Checkout requested. UserId={UserId} Amount={Amount} CheckoutBaseUrl={CheckoutBaseUrl} ReturnUrl={ReturnUrl} ServiceMode={ServiceMode}",
            request.UserId,
            request.Amount,
            _options.CheckoutBaseUrl,
            request.Url,
            hasServiceMode ? serviceMode : null);

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var pending = await _dbContext.Transactions
                .Where(x =>
                    x.UserId == request.UserId &&
                    x.Provider == "payme" &&
                    x.Status == (int)TransactionState.Pending)
                .ToListAsync(cancellationToken);

            if (pending.Count > 0)
            {
                var pendingIdsPreview = string.Join(",", pending.Select(x => x.Id).Take(5));
                _logger.LogWarning(
                    "[Payme] Removing {PendingCount} pending transactions for UserId={UserId}. SampleIds={SampleIds}",
                    pending.Count,
                    request.UserId,
                    pendingIdsPreview);
                _dbContext.Transactions.RemoveRange(pending);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var now = DateTime.UtcNow;
        var orderDoc = new PaymentTransaction
        {
            OrderDetailsJson = request.OrderDetails?.ValueKind == JsonValueKind.Object
                ? request.OrderDetails.Value.GetRawText()
                : "{}",
            Status = 0,
            Provider = "payme",
            Amount = request.Amount,
            UserId = request.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _dbContext.Transactions.AddAsync(orderDoc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var baseUrl = request.Url ?? string.Empty;
        if (hasServiceMode && serviceMode != 1 && !string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = $"{baseUrl.TrimEnd('/')}/{orderDoc.Id}";
        }

        var amountForPayme = (long)(request.Amount * 100);
        var payload = $"m={_options.MerchantId};ac.order_id={orderDoc.Id};a={amountForPayme};c={baseUrl}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));

        var checkoutUrl = $"{_options.CheckoutBaseUrl.TrimEnd('/')}/{encoded}";
        _logger.LogInformation(
            "[Payme] Checkout created. OrderId={OrderId} Amount={Amount} PayloadAmount={PayloadAmount} CheckoutBaseUrl={CheckoutBaseUrl}",
            orderDoc.Id,
            orderDoc.Amount,
            amountForPayme,
            _options.CheckoutBaseUrl);
        return new PaymeCheckoutResult(checkoutUrl, orderDoc.Id);
    }

    private static decimal NormalizeAmount(long amount) => Math.Floor(amount / 100m);

    private static bool IsWithinExpiration(long createTime, long currentTime)
    {
        var elapsedMinutes = (currentTime - createTime) / 60000m;
        return elapsedMinutes < ExpirationMinutes;
    }

    private static PaymeTransactionInfo MapTransactionInfo(PaymentTransaction transaction)
    {
        // According to Payme spec:
        // - perform_time should be 0 if transaction hasn't been performed (pending or pending canceled)
        // - cancel_time should be 0 if transaction hasn't been cancelled (state > 0)
        var isPerformed = transaction.Status is (int)TransactionState.Paid or (int)TransactionState.PaidCanceled;
        var performTime = isPerformed ? transaction.PerformTime : 0;
        var cancelTime = transaction.Status < 0 ? transaction.CancelTime : 0;

        return new PaymeTransactionInfo(
            transaction.TransactionId ?? string.Empty,
            transaction.CreateTime,
            performTime,
            cancelTime,
            transaction.Status,
            transaction.Reason);
    }

    public sealed record PaymeAccount([property: JsonPropertyName("order_id")] string OrderId);

    public sealed record PaymeCheckPerformParams(long Amount, PaymeAccount Account);

    public sealed record PaymeCreateTransactionParams([property: JsonConverter(typeof(FlexibleStringConverter))] string Id, long Amount, PaymeAccount Account, long Time);

    public sealed record PaymeTransactionIdParams([property: JsonConverter(typeof(FlexibleStringConverter))] string Id);

    public sealed record PaymeCancelTransactionParams([property: JsonConverter(typeof(FlexibleStringConverter))] string Id, int Reason);

    public sealed record PaymeStatementParams(long From, long To);

    public sealed record PaymeTransactionInfo(
        string TransactionId,
        long CreateTime,
        long PerformTime,
        long CancelTime,
        int State,
        int? Reason);

    public sealed record PaymeStatementItem(
        string TransactionId,
        long Time,
        long Amount,
        string AccountOrderId,
        long CreateTime,
        long PerformTime,
        long CancelTime,
        string Transaction,
        int State,
        int? Reason);

    public sealed record PaymeCheckoutRequest(JsonElement? OrderDetails, decimal Amount, string? UserId, string? Url);

    public sealed record PaymeCheckoutResult(string Url, string OrderId);

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
}
