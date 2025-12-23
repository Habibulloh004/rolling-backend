using Microsoft.AspNetCore.Mvc;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/seed")]
public sealed class SeedController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationRepository _notificationRepository;
    private readonly Random _random = new();

    public SeedController(AppDbContext dbContext, INotificationRepository notificationRepository)
    {
        _dbContext = dbContext;
        _notificationRepository = notificationRepository;
    }

    /// <summary>
    /// Seeds fake data. Use ?model=transactions|orders|users|notifications|times|events|all and ?count=5
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SeedAsync([FromQuery] string? model = "all", [FromQuery] int count = 5, CancellationToken cancellationToken = default)
    {
        var normalized = (model ?? "all").Trim().ToLowerInvariant();
        var target = Math.Clamp(count, 1, 500);
        var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (normalized is "all" or "transactions")
        {
            var items = Enumerable.Range(0, target).Select(_ => FakeTransaction()).ToList();
            await _dbContext.Transactions.AddRangeAsync(items, cancellationToken);
            results["transactions"] = items.Count;
        }

        if (normalized is "all" or "orders")
        {
            var items = Enumerable.Range(0, target).Select(_ => FakeOrder()).ToList();
            await _dbContext.Orders.AddRangeAsync(items, cancellationToken);
            results["orders"] = items.Count;
        }

        if (normalized is "all" or "users")
        {
            var items = Enumerable.Range(0, target).Select(_ => FakeUser()).ToList();
            await _dbContext.Users.AddRangeAsync(items, cancellationToken);
            results["users"] = items.Count;
        }

        var seedDomainNotifications = normalized is "all" or "notifications" or "events";
        if (normalized is "all" or "notifications")
        {
            var newsItems = Enumerable.Range(0, target).Select(_ => FakeNotificationRecord()).ToList();
            await _dbContext.Notifications.AddRangeAsync(newsItems, cancellationToken);
            results["notification_records"] = newsItems.Count;
        }

        if (normalized is "all" or "times")
        {
            var items = Enumerable.Range(0, target).Select(_ => FakeTime()).ToList();
            await _dbContext.Times.AddRangeAsync(items, cancellationToken);
            results["times"] = items.Count;
        }

        if (seedDomainNotifications)
        {
            var channel = normalized == "events" ? "seed:events" : "seed:notifications";
            var domainNotifications = Enumerable.Range(0, target).Select(_ => FakeDomainNotification(channel)).ToList();
            foreach (var notification in domainNotifications)
            {
                await _notificationRepository.SaveAsync(notification, cancellationToken);
            }

            results["notifications"] = domainNotifications.Count;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { inserted = results });
    }

    private PaymentTransaction FakeTransaction()
    {
        var now = DateTime.UtcNow;
        var amount = (decimal)(_random.Next(5_000, 50_000));
        var createTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new PaymentTransaction
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            UserId = $"user_{_random.Next(1000, 9999)}",
            OrderDetailsJson = "{}",
            Status = 1,
            Amount = amount,
            OrderId = $"order_{_random.Next(1000, 9999)}",
            CreateTime = createTime,
            PerformTime = createTime + 5000,
            CancelTime = 0,
            Reason = null,
            Provider = "seed",
            PrepareId = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private CourierOrder FakeOrder() => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        CourierId = _random.NextInt64(1, 10_000),
        OrderDataJson = "{}",
        ProductsJson = "[]",
        Status = "waiting"
    };

    private PosterUser FakeUser() => new()
    {
        UserId = _random.NextInt64(1, 10_000_000),
        Name = $"User {_random.Next(1, 9999)}",
        Login = $"login{_random.Next(1000, 9999)}",
        RoleName = "courier",
        RoleId = 2,
        UserType = 1,
        AccessMask = 0,
        LastIn = DateTime.UtcNow.ToString("O")
    };

    private NotificationRecord FakeNotificationRecord()
    {
        var id = _random.Next(1, 9999);
        return new NotificationRecord
        {
            EnTitle = $"EN title {id}",
            EnBody = $"EN body {id}",
            RuTitle = $"RU title {id}",
            RuBody = $"RU body {id}",
            UzTitle = $"UZ title {id}",
            UzBody = $"UZ body {id}"
        };
    }

    private BusinessTime FakeTime() => new()
    {
        OpenedTime = "09:00",
        ClosedTime = "23:00"
    };

    private Notification FakeDomainNotification(string channel)
    {
        var id = _random.Next(1, 9999);
        return Notification.Create(channel, $"Seed title {id}", $"Seed message {id}", DateTimeOffset.UtcNow);
    }
}
