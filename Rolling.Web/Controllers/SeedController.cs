using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Notifications.Commands;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Persistence.Redis;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/seed")]
public sealed class SeedController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationRepository _notificationRepository;
    private readonly IRedisBranchConfigCache _branchCache;
    private readonly Random _random = new();

    public SeedController(
        AppDbContext dbContext,
        INotificationRepository notificationRepository,
        IRedisBranchConfigCache branchCache)
    {
        _dbContext = dbContext;
        _notificationRepository = notificationRepository;
        _branchCache = branchCache;
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
            await _dbContext.CourierOrders.AddRangeAsync(items, cancellationToken);
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
            var commands = Enumerable.Range(0, target).Select(_ => FakeNotificationCommand()).ToList();
            foreach (var command in commands)
            {
                await _notificationRepository.SaveAsync(command, cancellationToken);
            }

            results["notifications"] = commands.Count;
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

    private CreateNotificationCommand FakeNotificationCommand()
    {
        var id = _random.Next(1, 9999);
        return new CreateNotificationCommand(
            $"Seed title {id}",
            $"Seed message {id}",
            $"Заголовок {id}",
            $"Сообщение {id}",
            $"Sarlavha {id}",
            $"Xabar {id}"
        );
    }

    /// <summary>
    /// Seeds branch configurations matching Poster spots.
    /// WARNING: This will DELETE all existing branch configurations and insert new ones.
    /// </summary>
    [HttpPost("branches")]
    public async Task<IActionResult> SeedBranchesAsync(CancellationToken cancellationToken)
    {
        // Delete all existing branch configurations
        var existing = await _dbContext.BranchConfigurations.ToListAsync(cancellationToken);
        _dbContext.BranchConfigurations.RemoveRange(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Create new branch configurations matching Poster spots
        var branches = new List<BranchConfiguration>
        {
            // Spot 1: possible4 (68VF+49W, Ташкент)
            new BranchConfiguration
            {
                SpotId = 1,
                NameEn = "Rolling Sushi - Possible4",
                NameRu = "Rolling Sushi - Possible4",
                NameUz = "Rolling Sushi - Possible4",
                ShortDescriptionEn = "Fresh sushi and Japanese cuisine",
                ShortDescriptionRu = "Свежие суши и японская кухня",
                ShortDescriptionUz = "Yangi sushi va yapon oshxonasi",
                IsActive = true,
                IsTemporarilyClosed = false,
                OpenTime = "10:00",
                CloseTime = "23:00",
                Timezone = "Asia/Tashkent",
                Is24Hours = false,
                DeliveryFee = 15000,
                FreeDeliveryThreshold = 150000,
                MinOrderAmount = 50000,
                MaxOrderAmount = 5000000,
                EtaMinMinutes = 30,
                EtaMaxMinutes = 45,
                EtaDisplayTextEn = "30-45 min",
                EtaDisplayTextRu = "30-45 мин",
                EtaDisplayTextUz = "30-45 daq",
                DeliveryRadius = 7.5,
                IsDeliveryEnabled = true,
                IsPickupEnabled = true,
                PickupEtaMinutes = 20,
                PickupDiscountPercentage = 10.00m,
                PickupDiscountTextEn = "10% off for pickup",
                PickupDiscountTextRu = "Скидка 10% при самовывозе",
                PickupDiscountTextUz = "Olib ketishda 10% chegirma",
                Phone = "+998712345678",
                AlternatePhone = "+998901234567",
                Email = "possible4@rollingsushi.uz",
                Whatsapp = "+998901234567",
                Telegram = "@rollingsushi",
                Instagram = "@rollingsushi.uz",
                Latitude = 41.2428631,
                Longitude = 69.3234633,
                AddressEn = "68VF+49W, Tashkent, Tashkent Region",
                AddressRu = "68VF+49W, Ташкент, Ташкентская область",
                AddressUz = "68VF+49W, Toshkent, Toshkent viloyati",
                CityEn = "Tashkent",
                CityRu = "Ташкент",
                CityUz = "Toshkent",
                GoogleMapsUrl = "https://maps.google.com/?q=41.2428631,69.3234633",
                YandexMapsUrl = "https://yandex.uz/maps/?pt=69.3234633,41.2428631",
                AcceptsCash = true,
                AcceptsCard = true,
                AcceptsOnlinePayment = true,
                AcceptsApplePay = false,
                AcceptsClick = true,
                AcceptsPayme = true,
                AcceptsUzum = true,
                CardOnDelivery = true,
                IsDineInEnabled = true,
                IsReservationEnabled = true,
                IsPreOrderEnabled = true,
                IsLoyaltyEnabled = true,
                IsPromoCodesEnabled = true,
                IsGiftCardsEnabled = false,
                IsCateringEnabled = true,
                ShowRatings = true,
                ShowWaitTime = true,
                SortOrder = 1,
                TagsJson = "[\"sushi\", \"japanese\", \"delivery\", \"dine-in\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            // Spot 2: possible2 (ул Бадахшон 2 проезд)
            new BranchConfiguration
            {
                SpotId = 2,
                NameEn = "Rolling Sushi - Possible2",
                NameRu = "Rolling Sushi - Possible2",
                NameUz = "Rolling Sushi - Possible2",
                ShortDescriptionEn = "Premium sushi experience",
                ShortDescriptionRu = "Премиальные суши",
                ShortDescriptionUz = "Premium sushi tajribasi",
                IsActive = true,
                IsTemporarilyClosed = false,
                OpenTime = "10:00",
                CloseTime = "23:00",
                Timezone = "Asia/Tashkent",
                Is24Hours = false,
                DeliveryFee = 15000,
                FreeDeliveryThreshold = 150000,
                MinOrderAmount = 50000,
                MaxOrderAmount = 5000000,
                EtaMinMinutes = 30,
                EtaMaxMinutes = 45,
                EtaDisplayTextEn = "30-45 min",
                EtaDisplayTextRu = "30-45 мин",
                EtaDisplayTextUz = "30-45 daq",
                DeliveryRadius = 8.0,
                IsDeliveryEnabled = true,
                IsPickupEnabled = true,
                PickupEtaMinutes = 25,
                PickupDiscountPercentage = 10.00m,
                PickupDiscountTextEn = "10% off for pickup",
                PickupDiscountTextRu = "Скидка 10% при самовывозе",
                PickupDiscountTextUz = "Olib ketishda 10% chegirma",
                Phone = "+998712345679",
                AlternatePhone = "+998901234568",
                Email = "possible2@rollingsushi.uz",
                Whatsapp = "+998901234568",
                Telegram = "@rollingsushi",
                Instagram = "@rollingsushi.uz",
                Latitude = 41.2942411,
                Longitude = 69.2239158,
                AddressEn = "House 1, Badakhshan 2nd Lane, 100000, Tashkent",
                AddressRu = "дом №1, ул Бадахшон 2 проезд, 100000, Ташкент",
                AddressUz = "1-uy, Badaxshon 2 o'tish, 100000, Toshkent",
                CityEn = "Tashkent",
                CityRu = "Ташкент",
                CityUz = "Toshkent",
                GoogleMapsUrl = "https://maps.google.com/?q=41.2942411,69.2239158",
                YandexMapsUrl = "https://yandex.uz/maps/?pt=69.2239158,41.2942411",
                AcceptsCash = true,
                AcceptsCard = true,
                AcceptsOnlinePayment = true,
                AcceptsApplePay = false,
                AcceptsClick = true,
                AcceptsPayme = true,
                AcceptsUzum = false,
                CardOnDelivery = true,
                IsDineInEnabled = false,
                IsReservationEnabled = false,
                IsPreOrderEnabled = true,
                IsLoyaltyEnabled = true,
                IsPromoCodesEnabled = true,
                IsGiftCardsEnabled = false,
                IsCateringEnabled = false,
                ShowRatings = true,
                ShowWaitTime = true,
                SortOrder = 2,
                TagsJson = "[\"sushi\", \"japanese\", \"delivery\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            // Spot 3: Чилонзор
            new BranchConfiguration
            {
                SpotId = 3,
                NameEn = "Rolling Sushi - Chilonzor",
                NameRu = "Rolling Sushi - Чилонзор",
                NameUz = "Rolling Sushi - Chilonzor",
                ShortDescriptionEn = "Cozy sushi bar in Chilonzor",
                ShortDescriptionRu = "Уютный суши-бар в Чилонзоре",
                ShortDescriptionUz = "Chilonzorda qulay sushi bar",
                IsActive = true,
                IsTemporarilyClosed = false,
                OpenTime = "10:00",
                CloseTime = "23:00",
                Timezone = "Asia/Tashkent",
                Is24Hours = false,
                DeliveryFee = 12000,
                FreeDeliveryThreshold = 120000,
                MinOrderAmount = 45000,
                MaxOrderAmount = 5000000,
                EtaMinMinutes = 25,
                EtaMaxMinutes = 40,
                EtaDisplayTextEn = "25-40 min",
                EtaDisplayTextRu = "25-40 мин",
                EtaDisplayTextUz = "25-40 daq",
                DeliveryRadius = 6.5,
                IsDeliveryEnabled = true,
                IsPickupEnabled = true,
                PickupEtaMinutes = 15,
                PickupDiscountPercentage = 15.00m,
                PickupDiscountTextEn = "15% off for pickup",
                PickupDiscountTextRu = "Скидка 15% при самовывозе",
                PickupDiscountTextUz = "Olib ketishda 15% chegirma",
                Phone = "+998712345680",
                AlternatePhone = "+998901234569",
                Email = "chilonzor@rollingsushi.uz",
                Whatsapp = "+998901234569",
                Telegram = "@rollingsushi_chilonzor",
                Instagram = "@rollingsushi.uz",
                Latitude = 0,  // No coordinates in Poster
                Longitude = 0, // No coordinates in Poster
                AddressEn = "Chilonzor District, Tashkent",
                AddressRu = "Чилонзорский район, Ташкент",
                AddressUz = "Chilonzor tumani, Toshkent",
                CityEn = "Tashkent",
                CityRu = "Ташкент",
                CityUz = "Toshkent",
                DistrictEn = "Chilonzor",
                DistrictRu = "Чилонзор",
                DistrictUz = "Chilonzor",
                AcceptsCash = true,
                AcceptsCard = true,
                AcceptsOnlinePayment = true,
                AcceptsApplePay = true,
                AcceptsClick = true,
                AcceptsPayme = true,
                AcceptsUzum = true,
                CardOnDelivery = true,
                IsDineInEnabled = true,
                IsReservationEnabled = true,
                IsPreOrderEnabled = true,
                IsLoyaltyEnabled = true,
                IsPromoCodesEnabled = true,
                IsGiftCardsEnabled = true,
                IsCateringEnabled = true,
                ShowRatings = true,
                ShowWaitTime = true,
                SortOrder = 3,
                TagsJson = "[\"sushi\", \"japanese\", \"delivery\", \"dine-in\", \"premium\", \"catering\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        await _dbContext.BranchConfigurations.AddRangeAsync(branches, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache
        await _branchCache.InvalidateBranchConfigCacheAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Branch configurations seeded successfully",
            deleted = existing.Count,
            inserted = branches.Count,
            branches = branches.Select(b => new { b.SpotId, b.NameEn, b.AddressRu })
        });
    }
}
