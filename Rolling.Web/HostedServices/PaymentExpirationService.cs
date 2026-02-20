using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Payments;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.HostedServices;

public sealed class PaymentExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PendingPaymentTracker _paymentTracker;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly TakeawayOrderTracker _takeawayOrderTracker;
    private readonly ILogger<PaymentExpirationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentTrackingOptions _options;
    private bool _hydratedOnStartup;

    public PaymentExpirationService(
        IServiceScopeFactory scopeFactory,
        PendingPaymentTracker paymentTracker,
        ActiveOrderTracker orderTracker,
        TakeawayOrderTracker takeawayOrderTracker,
        ILogger<PaymentExpirationService> logger,
        TimeProvider timeProvider,
        IOptions<PaymentTrackingOptions> options)
    {
        _scopeFactory = scopeFactory;
        _paymentTracker = paymentTracker;
        _orderTracker = orderTracker;
        _takeawayOrderTracker = takeawayOrderTracker;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment expiration sweep is disabled");
            return;
        }

        try
        {
            await HydrateTrackedPaymentsOnStartupAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hydrate pending payments on startup");
        }

        var sweepInterval = GetSweepInterval();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sweep pending payments");
            }

            try
            {
                await Task.Delay(sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        PruneTrackedPayments(now);

        var trackedPayments = _paymentTracker.GetTrackedPayments();
        if (trackedPayments.Count == 0)
        {
            return;
        }

        var cutoffMillis = new DateTimeOffset(now - GetExpirationWindow()).ToUnixTimeMilliseconds();
        var expiredPaymentIds = trackedPayments
            .Where(payment => IsPendingStatus(payment.Status) &&
                payment.CreateTime > 0 &&
                payment.CreateTime < cutoffMillis)
            .Select(payment => payment.PaymentId)
            .ToList();

        if (expiredPaymentIds.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderUpdatesPublisher = scope.ServiceProvider.GetRequiredService<IOrderUpdatesPublisher>();

        var expiredTransactions = await dbContext.Transactions
            .Where(transaction =>
                expiredPaymentIds.Contains(transaction.Id) &&
                transaction.Status >= 0 &&
                transaction.Status < (int)TransactionState.Paid)
            .ToListAsync(cancellationToken);

        if (expiredTransactions.Count == 0)
        {
            return;
        }

        var expiredCount = 0;
        var updatedOrders = new List<Order>();
        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)TransactionState.PendingCanceled;
            transaction.CancelTime = transaction.CancelTime == 0
                ? _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()
                : transaction.CancelTime;
            transaction.Reason ??= 4;
            transaction.UpdatedAt = now;

            var order = await dbContext.Orders.FirstOrDefaultAsync(order =>
                    order.PaymentTransactionId == transaction.Id ||
                    order.OrderNumber == transaction.Id,
                cancellationToken);

            if (order is not null && order.Status == OrderStatus.AwaitingPayment)
            {
                order.Status = OrderStatus.Cancelled;
                order.PaymentErrorMessage ??= "Payment expired";
                order.UpdatedAt = now;
                updatedOrders.Add(order);
            }

            expiredCount++;
            _paymentTracker.UntrackPayment(transaction.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var order in updatedOrders)
        {
            _orderTracker.UntrackOrder(order.Id);
            if (order.ServiceMode == 2)
            {
                _takeawayOrderTracker.UntrackOrder(order.Id);
            }

            await orderUpdatesPublisher.PublishAsync(
                OrderUpdateEventFactory.Create(order, "updated"),
                cancellationToken);
        }

        _logger.LogInformation("Expired {Count} pending payments", expiredCount);
    }

    private async Task HydrateTrackedPaymentsOnStartupAsync(CancellationToken cancellationToken)
    {
        if (_hydratedOnStartup)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = dbContext.Transactions
            .AsNoTracking()
            .Where(transaction =>
                (transaction.Provider == "payme" || transaction.Provider == "click") &&
                transaction.Status >= 0 &&
                transaction.Status < (int)TransactionState.Paid &&
                transaction.CreateTime > 0);

        var pending = await query.ToListAsync(cancellationToken);
        foreach (var transaction in pending)
        {
            _paymentTracker.TrackPayment(transaction);
        }

        _hydratedOnStartup = true;

        if (pending.Count > 0)
        {
            _logger.LogInformation("Hydrated {Count} pending payments for expiration tracking", pending.Count);
        }
    }

    private void PruneTrackedPayments(DateTime now)
    {
        var maxTrackingMinutes = _options.MaxTrackingMinutes;
        if (maxTrackingMinutes <= 0)
        {
            return;
        }

        var maxTracking = TimeSpan.FromMinutes(maxTrackingMinutes);
        foreach (var payment in _paymentTracker.GetTrackedPayments())
        {
            if (now - payment.TrackedAt > maxTracking)
            {
                _logger.LogDebug(
                    "Removing payment {PaymentId} from tracking - exceeded max tracking time",
                    payment.PaymentId);
                _paymentTracker.UntrackPayment(payment.PaymentId);
            }
        }
    }

    private TimeSpan GetSweepInterval()
    {
        var seconds = _options.SweepIntervalSeconds;
        if (seconds <= 0)
        {
            seconds = 60;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private TimeSpan GetExpirationWindow()
    {
        var minutes = _options.ExpirationMinutes;
        if (minutes <= 0)
        {
            minutes = 15;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static bool IsPendingStatus(int status) =>
        status >= 0 && status < (int)TransactionState.Paid;
}
