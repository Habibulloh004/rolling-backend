using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Payments;

public sealed class PendingPaymentTracker
{
    private readonly ConcurrentDictionary<string, TrackedPayment> _tracked = new();
    private readonly ILogger<PendingPaymentTracker> _logger;

    public PendingPaymentTracker(ILogger<PendingPaymentTracker> logger)
    {
        _logger = logger;
    }

    public void TrackPayment(TrackedPayment payment)
    {
        if (string.IsNullOrWhiteSpace(payment.PaymentId))
        {
            _logger.LogWarning("Cannot track payment with empty id");
            return;
        }

        _tracked.AddOrUpdate(
            payment.PaymentId,
            payment with { TrackedAt = payment.TrackedAt == default ? DateTime.UtcNow : payment.TrackedAt },
            (_, existing) => payment with
            {
                TrackedAt = existing.TrackedAt == default ? DateTime.UtcNow : existing.TrackedAt
            });

        _logger.LogDebug(
            "Tracking pending payment {PaymentId} (Provider={Provider}, Status={Status})",
            payment.PaymentId,
            payment.Provider,
            payment.Status);
    }

    public void TrackPayment(PaymentTransaction transaction)
    {
        if (transaction is null)
        {
            return;
        }

        TrackPayment(new TrackedPayment
        {
            PaymentId = transaction.Id,
            Provider = transaction.Provider,
            Status = transaction.Status,
            CreateTime = transaction.CreateTime
        });
    }

    public bool UntrackPayment(string paymentId)
    {
        if (_tracked.TryRemove(paymentId, out _))
        {
            _logger.LogDebug("Stopped tracking payment {PaymentId}", paymentId);
            return true;
        }

        return false;
    }

    public IReadOnlyCollection<TrackedPayment> GetTrackedPayments() =>
        _tracked.Values.ToList().AsReadOnly();

    public bool IsTracked(string paymentId) => _tracked.ContainsKey(paymentId);

    public int TrackedCount => _tracked.Count;

    public void ClearAll()
    {
        _tracked.Clear();
        _logger.LogDebug("Cleared all tracked payments");
    }
}

public sealed record TrackedPayment
{
    public required string PaymentId { get; init; }

    public required string Provider { get; init; }

    public required int Status { get; init; }

    public required long CreateTime { get; init; }

    public DateTime TrackedAt { get; init; } = DateTime.UtcNow;

    public DateTime? LastCheckedAt { get; init; }
}
