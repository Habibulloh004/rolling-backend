using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Persistence.Redis;
using StackExchange.Redis;
using Xunit;

namespace Rolling.Infrastructure.Tests;

public sealed class RedisNotificationCacheTests
{
    [Fact]
    public async Task GetNotificationsAsync_ReturnsNull_WhenCacheEmpty()
    {
        var database = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var logger = new Mock<ILogger<RedisNotificationCache>>();

        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        database.Setup(db => db.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var cache = new RedisNotificationCache(multiplexer.Object, logger.Object);

        var result = await cache.GetNotificationsAsync(10, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetNotificationsAsync_DeserializesNotifications()
    {
        var database = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var logger = new Mock<ILogger<RedisNotificationCache>>();

        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var notifications = new[]
        {
            new
            {
                id = 1,
                enTitle = "Title EN",
                enBody = "Message EN",
                ruTitle = "Title RU",
                ruBody = "Message RU",
                uzTitle = "Title UZ",
                uzBody = "Message UZ",
                createdAt = DateTime.UtcNow
            }
        };

        var payload = JsonSerializer.Serialize(notifications, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        database.Setup(db => db.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(payload);

        var cache = new RedisNotificationCache(multiplexer.Object, logger.Object);

        var result = await cache.GetNotificationsAsync(10, CancellationToken.None);

        Assert.NotNull(result);
        var item = Assert.Single(result);
        Assert.Equal("Title EN", item.EnTitle);
        Assert.Equal("Message EN", item.EnBody);
        Assert.Equal("Title RU", item.RuTitle);
        Assert.Equal("Message RU", item.RuBody);
    }

    [Fact]
    public async Task SetNotificationsAsync_SerializesAndStores()
    {
        var database = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var logger = new Mock<ILogger<RedisNotificationCache>>();

        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var cache = new RedisNotificationCache(multiplexer.Object, logger.Object);

        var notifications = new List<NotificationRecord>
        {
            new()
            {
                Id = 1,
                EnTitle = "Sale",
                EnBody = "Big sale today!",
                RuTitle = "Распродажа",
                RuBody = "Большая распродажа!",
                UzTitle = "Chegirma",
                UzBody = "Katta chegirma!",
                CreatedAt = DateTime.UtcNow
            }
        };

        await cache.SetNotificationsAsync(notifications, CancellationToken.None);

        database.Verify(
            db => db.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == "notifications:all"),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateNotificationCacheAsync_DeletesKey()
    {
        var database = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var logger = new Mock<ILogger<RedisNotificationCache>>();

        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var cache = new RedisNotificationCache(multiplexer.Object, logger.Object);

        await cache.InvalidateNotificationCacheAsync(CancellationToken.None);

        database.Verify(
            db => db.KeyDeleteAsync(
                It.Is<RedisKey>(k => k.ToString() == "notifications:all"),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }
}
