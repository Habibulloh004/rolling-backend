using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Seeding;

/// <summary>
/// Service to seed branch configurations from Poster API when the database is empty.
/// Fetches spots data and optionally branch config from product_production_description.
/// </summary>
public sealed class BranchConfigurationSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly PosterOptions _options;
    private readonly ILogger<BranchConfigurationSeeder> _logger;

    // Product ID that contains branch configuration in product_production_description
    private const string BranchConfigProductId = "444";

    public BranchConfigurationSeeder(
        AppDbContext dbContext,
        HttpClient httpClient,
        IOptions<PosterOptions> options,
        ILogger<BranchConfigurationSeeder> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Seeds branch configurations from Poster API if database is empty.
    /// </summary>
    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        var hasAnyBranches = await _dbContext.BranchConfigurations.AnyAsync(cancellationToken);
        if (hasAnyBranches)
        {
            _logger.LogInformation("Branch configurations already exist, skipping seeding");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl) || string.IsNullOrWhiteSpace(_options.Token))
        {
            _logger.LogWarning("Poster API configuration is missing, cannot seed branch configurations");
            return;
        }

        _logger.LogInformation("No branch configurations found, seeding from Poster API...");

        try
        {
            // Fetch spots from Poster API
            var spots = await FetchSpotsAsync(cancellationToken);
            if (spots == null || spots.Count == 0)
            {
                _logger.LogWarning("No spots returned from Poster API");
                return;
            }

            // Fetch extended branch config from product
            var branchConfigs = await FetchBranchConfigFromProductAsync(cancellationToken);

            // Create branch configurations
            var branches = new List<BranchConfiguration>();
            foreach (var spot in spots)
            {
                var branch = CreateBranchFromSpot(spot, branchConfigs);
                if (branch != null)
                {
                    branches.Add(branch);
                }
            }

            if (branches.Count > 0)
            {
                await _dbContext.BranchConfigurations.AddRangeAsync(branches, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully seeded {Count} branch configurations", branches.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed branch configurations from Poster API");
        }
    }

    private async Task<List<PosterSpot>?> FetchSpotsAsync(CancellationToken cancellationToken)
    {
        var url = $"{_options.ApiBaseUrl!.TrimEnd('/')}/api/spots.getSpots?token={_options.Token}";
        _logger.LogInformation("Fetching spots from Poster API...");

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster spots call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("response", out var responseArray))
        {
            return null;
        }

        var spots = new List<PosterSpot>();
        foreach (var element in responseArray.EnumerateArray())
        {
            var spotId = element.TryGetProperty("spot_id", out var spotIdEl) ? spotIdEl.GetInt32() : 0;
            var name = element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var address = element.TryGetProperty("address", out var addrEl) ? addrEl.GetString() : null;
            var lat = element.TryGetProperty("lat", out var latEl) ? latEl.GetString() : null;
            var lng = element.TryGetProperty("lng", out var lngEl) ? lngEl.GetString() : null;
            var deleted = element.TryGetProperty("spot_delete", out var delEl) && delEl.GetInt32() == 1;

            if (spotId > 0 && !deleted)
            {
                spots.Add(new PosterSpot
                {
                    SpotId = spotId,
                    Name = name ?? $"Branch {spotId}",
                    Address = address,
                    Latitude = double.TryParse(lat, out var parsedLat) ? parsedLat : 0,
                    Longitude = double.TryParse(lng, out var parsedLng) ? parsedLng : 0
                });
            }
        }

        _logger.LogInformation("Fetched {Count} active spots from Poster API", spots.Count);
        return spots;
    }

    private async Task<Dictionary<int, BranchConfigData>?> FetchBranchConfigFromProductAsync(CancellationToken cancellationToken)
    {
        var url = $"{_options.ApiBaseUrl!.TrimEnd('/')}/api/menu.getProduct?token={_options.Token}&product_id={BranchConfigProductId}";
        _logger.LogInformation("Fetching branch config from product {ProductId}...", BranchConfigProductId);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Poster product call failed: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            {
                return null;
            }

            if (!responseEl.TryGetProperty("product_production_description", out var descEl))
            {
                return null;
            }

            var descJson = descEl.GetString();
            if (string.IsNullOrWhiteSpace(descJson))
            {
                return null;
            }

            // Parse the nested JSON in product_production_description
            using var configDoc = JsonDocument.Parse(descJson);
            if (!configDoc.RootElement.TryGetProperty("branches", out var branchesArray))
            {
                return null;
            }

            var configs = new Dictionary<int, BranchConfigData>();
            foreach (var branch in branchesArray.EnumerateArray())
            {
                var spotId = branch.TryGetProperty("spot_id", out var spotIdEl) ? spotIdEl.GetInt32() : 0;
                if (spotId <= 0) continue;

                configs[spotId] = new BranchConfigData
                {
                    SpotId = spotId,
                    Name = branch.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
                    OpenTime = branch.TryGetProperty("open", out var openEl) ? openEl.GetString() : null,
                    CloseTime = branch.TryGetProperty("close", out var closeEl) ? closeEl.GetString() : null,
                    DeliveryFee = branch.TryGetProperty("delivery_fee", out var feeEl) ? feeEl.GetInt32() : 0,
                    Eta = branch.TryGetProperty("eta", out var etaEl) ? etaEl.GetString() : null,
                    Phone = branch.TryGetProperty("phone", out var phoneEl) ? phoneEl.GetString() : null,
                    GoogleMapsUrl = branch.TryGetProperty("google_maps", out var gmapEl) ? gmapEl.GetString() : null
                };
            }

            _logger.LogInformation("Parsed branch config for {Count} spots from product", configs.Count);
            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch/parse branch config from product, will use defaults");
            return null;
        }
    }

    private BranchConfiguration? CreateBranchFromSpot(PosterSpot spot, Dictionary<int, BranchConfigData>? configs)
    {
        var config = configs?.GetValueOrDefault(spot.SpotId);

        // Parse ETA (e.g. "35-45" -> min=35, max=45)
        int? etaMin = null, etaMax = null;
        if (!string.IsNullOrEmpty(config?.Eta))
        {
            var parts = config.Eta.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var min) && int.TryParse(parts[1].Trim(), out var max))
            {
                etaMin = min;
                etaMax = max;
            }
        }

        var branchName = config?.Name ?? spot.Name;

        return new BranchConfiguration
        {
            SpotId = spot.SpotId,
            NameEn = branchName,
            NameRu = branchName,
            NameUz = branchName,
            IsActive = true,
            IsTemporarilyClosed = false,
            OpenTime = config?.OpenTime ?? "10:00",
            CloseTime = config?.CloseTime ?? "23:00",
            Timezone = "Asia/Tashkent",
            Is24Hours = false,
            DeliveryFee = config?.DeliveryFee ?? 10000,
            EtaMinMinutes = etaMin ?? 35,
            EtaMaxMinutes = etaMax ?? 45,
            EtaDisplayTextEn = config?.Eta ?? "35-45 min",
            EtaDisplayTextRu = config?.Eta != null ? $"{config.Eta} мин" : "35-45 мин",
            EtaDisplayTextUz = config?.Eta != null ? $"{config.Eta} min" : "35-45 min",
            IsDeliveryEnabled = true,
            IsPickupEnabled = true,
            PickupEtaMinutes = 15,
            Phone = config?.Phone,
            Latitude = spot.Latitude,
            Longitude = spot.Longitude,
            AddressEn = spot.Address,
            AddressRu = spot.Address,
            AddressUz = spot.Address,
            CityEn = "Tashkent",
            CityRu = "Ташкент",
            CityUz = "Toshkent",
            GoogleMapsUrl = config?.GoogleMapsUrl,
            AcceptsCash = true,
            AcceptsCard = true,
            AcceptsOnlinePayment = true,
            AcceptsClick = true,
            AcceptsPayme = true,
            AcceptsUzum = false,
            CardOnDelivery = true,
            IsLoyaltyEnabled = true,
            IsPromoCodesEnabled = true,
            ShowRatings = true,
            ShowWaitTime = true,
            SortOrder = spot.SpotId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class PosterSpot
    {
        public int SpotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private sealed class BranchConfigData
    {
        public int SpotId { get; set; }
        public string? Name { get; set; }
        public string? OpenTime { get; set; }
        public string? CloseTime { get; set; }
        public int DeliveryFee { get; set; }
        public string? Eta { get; set; }
        public string? Phone { get; set; }
        public string? GoogleMapsUrl { get; set; }
    }
}
