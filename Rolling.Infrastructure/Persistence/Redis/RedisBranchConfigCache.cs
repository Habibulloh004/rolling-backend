using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using StackExchange.Redis;

namespace Rolling.Infrastructure.Persistence.Redis;

public interface IRedisBranchConfigCache
{
    Task<List<BranchConfiguration>?> GetBranchConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<BranchConfiguration?> GetBranchConfigurationByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<BranchConfiguration?> GetBranchConfigurationBySpotIdAsync(int spotId, CancellationToken cancellationToken = default);
    Task SetBranchConfigurationsAsync(List<BranchConfiguration> configs, CancellationToken cancellationToken = default);
    Task SetBranchConfigurationAsync(BranchConfiguration config, CancellationToken cancellationToken = default);
    Task InvalidateBranchConfigCacheAsync(CancellationToken cancellationToken = default);
    Task InvalidateBranchConfigByIdAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class RedisBranchConfigCache : IRedisBranchConfigCache
{
    private const string BranchConfigsAllKey = "branch_configs:all";
    private const string BranchConfigByIdPrefix = "branch_config:id:";
    private const string BranchConfigBySpotIdPrefix = "branch_config:spot:";
    private const string BranchConfigsKeyPrefix = "branch_config";

    // Cache indefinitely - only invalidate on DB changes (create/update/delete)
    private static readonly TimeSpan? DefaultExpiration = null;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBranchConfigCache> _logger;

    public RedisBranchConfigCache(IConnectionMultiplexer redis, ILogger<RedisBranchConfigCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<List<BranchConfiguration>?> GetBranchConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            var cached = await db.StringGetAsync(BranchConfigsAllKey);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("🔍 Cache MISS: Branch Configurations");
                return null;
            }

            var documents = JsonSerializer.Deserialize<List<BranchConfigDocument>>(cached.ToString(), SerializerOptions);
            if (documents == null)
            {
                _logger.LogWarning("Failed to deserialize cached branch configurations");
                return null;
            }

            _logger.LogInformation("✅ Cache HIT: Branch Configurations (count: {Count})", documents.Count);
            return documents.Select(d => d.ToEntity()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch configurations from Redis cache");
            return null;
        }
    }

    public async Task<BranchConfiguration?> GetBranchConfigurationByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BranchConfigByIdPrefix}{id}";

            var cached = await db.StringGetAsync(key);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("🔍 Cache MISS: Branch Config ID {Id}", id);
                return null;
            }

            var document = JsonSerializer.Deserialize<BranchConfigDocument>(cached.ToString(), SerializerOptions);
            if (document == null)
            {
                _logger.LogWarning("Failed to deserialize cached branch config {Id}", id);
                return null;
            }

            _logger.LogInformation("✅ Cache HIT: Branch Config ID {Id}", id);
            return document.ToEntity();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch config {Id} from Redis cache", id);
            return null;
        }
    }

    public async Task<BranchConfiguration?> GetBranchConfigurationBySpotIdAsync(int spotId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BranchConfigBySpotIdPrefix}{spotId}";

            var cached = await db.StringGetAsync(key);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("🔍 Cache MISS: Branch Config SpotId {SpotId}", spotId);
                return null;
            }

            var document = JsonSerializer.Deserialize<BranchConfigDocument>(cached.ToString(), SerializerOptions);
            if (document == null)
            {
                _logger.LogWarning("Failed to deserialize cached branch config for spot {SpotId}", spotId);
                return null;
            }

            _logger.LogInformation("✅ Cache HIT: Branch Config SpotId {SpotId}", spotId);
            return document.ToEntity();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch config for spot {SpotId} from Redis cache", spotId);
            return null;
        }
    }

    public async Task SetBranchConfigurationsAsync(List<BranchConfiguration> configs, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            var documents = configs.Select(BranchConfigDocument.FromEntity).ToList();
            var json = JsonSerializer.Serialize(documents, SerializerOptions);

            await db.StringSetAsync(BranchConfigsAllKey, json, DefaultExpiration);
            _logger.LogInformation("💾 Cached: Branch Configurations (count: {Count})", configs.Count);

            // Also cache individual items for fast lookup
            foreach (var config in configs)
            {
                await SetBranchConfigurationAsync(config, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching branch configurations to Redis");
        }
    }

    public async Task SetBranchConfigurationAsync(BranchConfiguration config, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            var document = BranchConfigDocument.FromEntity(config);
            var json = JsonSerializer.Serialize(document, SerializerOptions);

            // Cache by ID
            var idKey = $"{BranchConfigByIdPrefix}{config.Id}";
            await db.StringSetAsync(idKey, json, DefaultExpiration);

            // Cache by SpotId
            var spotKey = $"{BranchConfigBySpotIdPrefix}{config.SpotId}";
            await db.StringSetAsync(spotKey, json, DefaultExpiration);

            _logger.LogInformation("💾 Cached: Branch Config ID {Id}, SpotId {SpotId}", config.Id, config.SpotId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching branch config {Id} to Redis", config.Id);
        }
    }

    public async Task InvalidateBranchConfigCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());

            // Delete all branch config keys
            var keys = server.Keys(pattern: $"{BranchConfigsKeyPrefix}*").ToArray();
            var allKey = new RedisKey(BranchConfigsAllKey);

            var batch = db.CreateBatch();
            var tasks = new List<Task>();

            tasks.Add(batch.KeyDeleteAsync(allKey));

            foreach (var key in keys)
            {
                tasks.Add(batch.KeyDeleteAsync(key));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            _logger.LogInformation("🗑️  Invalidated all branch config cache keys (count: {Count})", keys.Length + 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating branch config cache");
        }
    }

    public async Task InvalidateBranchConfigByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BranchConfigByIdPrefix}{id}";

            await db.KeyDeleteAsync(key);
            _logger.LogInformation("🗑️  Invalidated cache: Branch Config ID {Id}", id);

            // Also invalidate list cache since it contains this config
            await InvalidateBranchConfigCacheAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating branch config {Id} cache", id);
        }
    }

    // Document class for serialization
    private sealed record BranchConfigDocument(
        int Id,
        int SpotId,
        string NameEn,
        string? NameRu,
        string? NameUz,
        string? ShortDescriptionEn,
        string? ShortDescriptionRu,
        string? ShortDescriptionUz,
        bool IsActive,
        bool IsTemporarilyClosed,
        string? ClosureMessageEn,
        string? ClosureMessageRu,
        string? ClosureMessageUz,
        string? OpenTime,
        string? CloseTime,
        string? Timezone,
        bool Is24Hours,
        decimal? DeliveryFee,
        decimal? FreeDeliveryThreshold,
        decimal? MinOrderAmount,
        decimal? MaxOrderAmount,
        int? EtaMinMinutes,
        int? EtaMaxMinutes,
        string? EtaDisplayTextEn,
        string? EtaDisplayTextRu,
        string? EtaDisplayTextUz,
        double? DeliveryRadius,
        bool IsDeliveryEnabled,
        bool IsPickupEnabled,
        int? PickupEtaMinutes,
        decimal? PickupDiscountPercentage,
        string? PickupDiscountTextEn,
        string? PickupDiscountTextRu,
        string? PickupDiscountTextUz,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? Whatsapp,
        string? Telegram,
        string? Instagram,
        double Latitude,
        double Longitude,
        string? AddressEn,
        string? AddressRu,
        string? AddressUz,
        string? CityEn,
        string? CityRu,
        string? CityUz,
        string? DistrictEn,
        string? DistrictRu,
        string? DistrictUz,
        string? LandmarkEn,
        string? LandmarkRu,
        string? LandmarkUz,
        string? GoogleMapsUrl,
        string? YandexMapsUrl,
        bool AcceptsCash,
        bool AcceptsCard,
        bool AcceptsOnlinePayment,
        bool AcceptsApplePay,
        bool AcceptsClick,
        bool AcceptsPayme,
        bool AcceptsUzum,
        bool CardOnDelivery,
        bool IsDineInEnabled,
        bool IsReservationEnabled,
        bool IsPreOrderEnabled,
        bool IsLoyaltyEnabled,
        bool IsPromoCodesEnabled,
        bool IsGiftCardsEnabled,
        bool IsCateringEnabled,
        bool ShowRatings,
        bool ShowWaitTime,
        string? LogoUrl,
        string? BannerUrl,
        string? ThumbnailUrl,
        string? GalleryUrlsJson,
        int SortOrder,
        string? TagsJson,
        string? MetadataJson,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static BranchConfigDocument FromEntity(BranchConfiguration e) => new(
            e.Id, e.SpotId, e.NameEn, e.NameRu, e.NameUz,
            e.ShortDescriptionEn, e.ShortDescriptionRu, e.ShortDescriptionUz,
            e.IsActive, e.IsTemporarilyClosed,
            e.ClosureMessageEn, e.ClosureMessageRu, e.ClosureMessageUz,
            e.OpenTime, e.CloseTime, e.Timezone, e.Is24Hours,
            e.DeliveryFee, e.FreeDeliveryThreshold, e.MinOrderAmount, e.MaxOrderAmount,
            e.EtaMinMinutes, e.EtaMaxMinutes, e.EtaDisplayTextEn, e.EtaDisplayTextRu, e.EtaDisplayTextUz,
            e.DeliveryRadius, e.IsDeliveryEnabled,
            e.IsPickupEnabled, e.PickupEtaMinutes, e.PickupDiscountPercentage,
            e.PickupDiscountTextEn, e.PickupDiscountTextRu, e.PickupDiscountTextUz,
            e.Phone, e.AlternatePhone, e.Email, e.Whatsapp, e.Telegram, e.Instagram,
            e.Latitude, e.Longitude,
            e.AddressEn, e.AddressRu, e.AddressUz,
            e.CityEn, e.CityRu, e.CityUz,
            e.DistrictEn, e.DistrictRu, e.DistrictUz,
            e.LandmarkEn, e.LandmarkRu, e.LandmarkUz,
            e.GoogleMapsUrl, e.YandexMapsUrl,
            e.AcceptsCash, e.AcceptsCard, e.AcceptsOnlinePayment, e.AcceptsApplePay,
            e.AcceptsClick, e.AcceptsPayme, e.AcceptsUzum, e.CardOnDelivery,
            e.IsDineInEnabled, e.IsReservationEnabled, e.IsPreOrderEnabled,
            e.IsLoyaltyEnabled, e.IsPromoCodesEnabled, e.IsGiftCardsEnabled, e.IsCateringEnabled,
            e.ShowRatings, e.ShowWaitTime,
            e.LogoUrl, e.BannerUrl, e.ThumbnailUrl, e.GalleryUrlsJson,
            e.SortOrder, e.TagsJson, e.MetadataJson,
            e.CreatedAt, e.UpdatedAt
        );

        public BranchConfiguration ToEntity() => new()
        {
            Id = Id, SpotId = SpotId, NameEn = NameEn, NameRu = NameRu, NameUz = NameUz,
            ShortDescriptionEn = ShortDescriptionEn, ShortDescriptionRu = ShortDescriptionRu, ShortDescriptionUz = ShortDescriptionUz,
            IsActive = IsActive, IsTemporarilyClosed = IsTemporarilyClosed,
            ClosureMessageEn = ClosureMessageEn, ClosureMessageRu = ClosureMessageRu, ClosureMessageUz = ClosureMessageUz,
            OpenTime = OpenTime, CloseTime = CloseTime, Timezone = Timezone, Is24Hours = Is24Hours,
            DeliveryFee = DeliveryFee, FreeDeliveryThreshold = FreeDeliveryThreshold,
            MinOrderAmount = MinOrderAmount, MaxOrderAmount = MaxOrderAmount,
            EtaMinMinutes = EtaMinMinutes, EtaMaxMinutes = EtaMaxMinutes,
            EtaDisplayTextEn = EtaDisplayTextEn, EtaDisplayTextRu = EtaDisplayTextRu, EtaDisplayTextUz = EtaDisplayTextUz,
            DeliveryRadius = DeliveryRadius, IsDeliveryEnabled = IsDeliveryEnabled,
            IsPickupEnabled = IsPickupEnabled, PickupEtaMinutes = PickupEtaMinutes, PickupDiscountPercentage = PickupDiscountPercentage,
            PickupDiscountTextEn = PickupDiscountTextEn, PickupDiscountTextRu = PickupDiscountTextRu, PickupDiscountTextUz = PickupDiscountTextUz,
            Phone = Phone, AlternatePhone = AlternatePhone, Email = Email,
            Whatsapp = Whatsapp, Telegram = Telegram, Instagram = Instagram,
            Latitude = Latitude, Longitude = Longitude,
            AddressEn = AddressEn, AddressRu = AddressRu, AddressUz = AddressUz,
            CityEn = CityEn, CityRu = CityRu, CityUz = CityUz,
            DistrictEn = DistrictEn, DistrictRu = DistrictRu, DistrictUz = DistrictUz,
            LandmarkEn = LandmarkEn, LandmarkRu = LandmarkRu, LandmarkUz = LandmarkUz,
            GoogleMapsUrl = GoogleMapsUrl, YandexMapsUrl = YandexMapsUrl,
            AcceptsCash = AcceptsCash, AcceptsCard = AcceptsCard, AcceptsOnlinePayment = AcceptsOnlinePayment,
            AcceptsApplePay = AcceptsApplePay, AcceptsClick = AcceptsClick, AcceptsPayme = AcceptsPayme,
            AcceptsUzum = AcceptsUzum, CardOnDelivery = CardOnDelivery,
            IsDineInEnabled = IsDineInEnabled, IsReservationEnabled = IsReservationEnabled, IsPreOrderEnabled = IsPreOrderEnabled,
            IsLoyaltyEnabled = IsLoyaltyEnabled, IsPromoCodesEnabled = IsPromoCodesEnabled,
            IsGiftCardsEnabled = IsGiftCardsEnabled, IsCateringEnabled = IsCateringEnabled,
            ShowRatings = ShowRatings, ShowWaitTime = ShowWaitTime,
            LogoUrl = LogoUrl, BannerUrl = BannerUrl, ThumbnailUrl = ThumbnailUrl, GalleryUrlsJson = GalleryUrlsJson,
            SortOrder = SortOrder, TagsJson = TagsJson, MetadataJson = MetadataJson,
            CreatedAt = CreatedAt, UpdatedAt = UpdatedAt
        };
    }
}
