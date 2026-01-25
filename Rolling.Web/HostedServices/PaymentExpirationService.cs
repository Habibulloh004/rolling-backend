using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Payments;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.HostedServices;

public sealed class PaymentExpirationService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExpirationWindow = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentExpirationService> _logger;
    private readonly TimeProvider _timeProvider;

    public PaymentExpirationService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentExpirationService> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                await Task.Delay(SweepInterval, stoppingToken);
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
        var cutoff = now - ExpirationWindow;
        var cutoffMillis = new DateTimeOffset(cutoff).ToUnixTimeMilliseconds();

        var expiredTransactions = await dbContext.Transactions
            .Where(transaction =>
                (transaction.Provider == "payme" || transaction.Provider == "click") &&
                transaction.Status >= 0 &&
                transaction.Status < (int)TransactionState.Paid &&
                transaction.CreateTime > 0 &&
                transaction.CreateTime < cutoffMillis)
            .ToListAsync(cancellationToken);

        if (expiredTransactions.Count == 0)
        {
            return;
        }

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
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Expired {Count} pending payments", expiredTransactions.Count);
    }
}
