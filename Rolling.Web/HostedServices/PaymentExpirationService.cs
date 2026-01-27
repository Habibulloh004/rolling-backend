using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Payments;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.HostedServices;

public sealed class PaymentExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PendingPaymentTracker _paymentTracker;
    private readonly ILogger<PaymentExpirationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentTrackingOptions _options;
    private DateTime _lastHydratedAtUtc;

    public PaymentExpirationService(
        IServiceScopeFactory scopeFactory,
        PendingPaymentTracker paymentTracker,
        ILogger<PaymentExpirationService> logger,
        TimeProvider timeProvider,
        IOptions<PaymentTrackingOptions> options)
    {
        _scopeFactory = scopeFactory;
        _paymentTracker = paymentTracker;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options.Value;
        _lastHydratedAtUtc = DateTime.MinValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment expiration sweep is disabled");
            return;
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
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        PruneTrackedPayments(now);
        await HydrateTrackedPaymentsIfNeededAsync(dbContext, now, cancellationToken);

        var trackedPayments = _paymentTracker.GetTrackedPayments();
        if (trackedPayments.Count == 0)
        {
            return;
        }

        trackedPayments = await SyncTrackedPaymentsFromDbAsync(dbContext, trackedPayments, now, cancellationToken);
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
            }

            expiredCount++;
            _paymentTracker.UntrackPayment(transaction.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Expired {Count} pending payments", expiredCount);
    }

    private async Task HydrateTrackedPaymentsIfNeededAsync(
        AppDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var hydrationInterval = GetHydrationInterval();
        if (now - _lastHydratedAtUtc < hydrationInterval)
        {
            return;
        }

        var query = dbContext.Transactions
            .AsNoTracking()
            .Where(transaction =>
                (transaction.Provider == "payme" || transaction.Provider == "click") &&
                transaction.Status >= 0 &&
                transaction.Status < (int)TransactionState.Paid &&
                transaction.CreateTime > 0);

        if (_lastHydratedAtUtc != DateTime.MinValue)
        {
            var lookbackMinutes = _options.ActivePaymentsLookbackMinutes;
            if (lookbackMinutes > 0)
            {
                var cutoff = now.AddMinutes(-lookbackMinutes);
                var cutoffMillis = new DateTimeOffset(cutoff).ToUnixTimeMilliseconds();
                query = query.Where(transaction => transaction.CreateTime >= cutoffMillis);
            }
        }

        var pending = await query.ToListAsync(cancellationToken);
        foreach (var transaction in pending)
        {
            _paymentTracker.TrackPayment(transaction);
        }

        _lastHydratedAtUtc = now;

        if (pending.Count > 0)
        {
            _logger.LogDebug("Hydrated {Count} pending payments for expiration tracking", pending.Count);
        }
    }

    private async Task<IReadOnlyCollection<TrackedPayment>> SyncTrackedPaymentsFromDbAsync(
        AppDbContext dbContext,
        IReadOnlyCollection<TrackedPayment> trackedPayments,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (trackedPayments.Count == 0)
        {
            return trackedPayments;
        }

        var trackedIds = trackedPayments
            .Select(payment => payment.PaymentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedIds.Count == 0)
        {
            return trackedPayments;
        }

        var statuses = await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => trackedIds.Contains(transaction.Id))
            .Select(transaction => new
            {
                transaction.Id,
                transaction.Status,
                transaction.Provider,
                transaction.CreateTime
            })
            .ToListAsync(cancellationToken);

        var statusLookup = statuses.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        foreach (var trackedPayment in trackedPayments)
        {
            if (!statusLookup.TryGetValue(trackedPayment.PaymentId, out var dbPayment))
            {
                _paymentTracker.UntrackPayment(trackedPayment.PaymentId);
                continue;
            }

            if (!IsPendingStatus(dbPayment.Status))
            {
                _paymentTracker.UntrackPayment(trackedPayment.PaymentId);
                continue;
            }

            if (trackedPayment.Status != dbPayment.Status ||
                trackedPayment.CreateTime != dbPayment.CreateTime ||
                !string.Equals(trackedPayment.Provider, dbPayment.Provider, StringComparison.OrdinalIgnoreCase) ||
                trackedPayment.LastCheckedAt == null)
            {
                _paymentTracker.TrackPayment(trackedPayment with
                {
                    Status = dbPayment.Status,
                    Provider = dbPayment.Provider,
                    CreateTime = dbPayment.CreateTime,
                    LastCheckedAt = now
                });
            }
        }

        return _paymentTracker.GetTrackedPayments();
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

    private TimeSpan GetHydrationInterval()
    {
        var seconds = _options.HydrationIntervalSeconds;
        if (seconds <= 0)
        {
            seconds = 300;
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
