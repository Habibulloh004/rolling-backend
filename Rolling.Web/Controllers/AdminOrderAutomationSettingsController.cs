using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Auth;
using Rolling.Web.Models.Orders;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/admin/order-automation-settings")]
[AdminAuthorize]
public sealed class AdminOrderAutomationSettingsController : ControllerBase
{
    private const int MinDelaySeconds = 0;
    private const int MaxDelaySeconds = 24 * 60 * 60;

    private readonly AppDbContext _dbContext;
    private readonly TakeawayAutomationSettingsCache _settingsCache;
    private readonly ILogger<AdminOrderAutomationSettingsController> _logger;

    public AdminOrderAutomationSettingsController(
        AppDbContext dbContext,
        TakeawayAutomationSettingsCache settingsCache,
        ILogger<AdminOrderAutomationSettingsController> logger)
    {
        _dbContext = dbContext;
        _settingsCache = settingsCache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        return Ok(new TakeawayAutomationSettingsResponse
        {
            PreparingDelaySeconds = settings.PreparingDelaySeconds,
            ReadyForPickupDelaySeconds = settings.ReadyForPickupDelaySeconds,
            UpdatedAt = settings.UpdatedAt
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync(
        [FromBody] UpdateTakeawayAutomationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PreparingDelaySeconds < MinDelaySeconds ||
            request.PreparingDelaySeconds > MaxDelaySeconds)
        {
            return BadRequest(new
            {
                error = $"preparingDelaySeconds must be between {MinDelaySeconds} and {MaxDelaySeconds}"
            });
        }

        if (request.ReadyForPickupDelaySeconds < MinDelaySeconds ||
            request.ReadyForPickupDelaySeconds > MaxDelaySeconds)
        {
            return BadRequest(new
            {
                error = $"readyForPickupDelaySeconds must be between {MinDelaySeconds} and {MaxDelaySeconds}"
            });
        }

        if (request.ReadyForPickupDelaySeconds < request.PreparingDelaySeconds)
        {
            return BadRequest(new
            {
                error = "readyForPickupDelaySeconds must be greater than or equal to preparingDelaySeconds"
            });
        }

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.PreparingDelaySeconds = request.PreparingDelaySeconds;
        settings.ReadyForPickupDelaySeconds = request.ReadyForPickupDelaySeconds;
        settings.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _settingsCache.Set(
            settings.PreparingDelaySeconds,
            settings.ReadyForPickupDelaySeconds,
            settings.UpdatedAt,
            DateTime.UtcNow);

        _logger.LogInformation(
            "Updated takeaway automation settings: PreparingDelaySeconds={PreparingDelaySeconds}, ReadyForPickupDelaySeconds={ReadyForPickupDelaySeconds}",
            settings.PreparingDelaySeconds,
            settings.ReadyForPickupDelaySeconds);

        return Ok(new TakeawayAutomationSettingsResponse
        {
            PreparingDelaySeconds = settings.PreparingDelaySeconds,
            ReadyForPickupDelaySeconds = settings.ReadyForPickupDelaySeconds,
            UpdatedAt = settings.UpdatedAt
        });
    }

    private async Task<TakeawayAutomationSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TakeawayAutomationSettings
            .FirstOrDefaultAsync(item => item.Id == TakeawayAutomationSettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new TakeawayAutomationSettings
        {
            Id = TakeawayAutomationSettings.SingletonId,
            PreparingDelaySeconds = 300,
            ReadyForPickupDelaySeconds = 1800,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.TakeawayAutomationSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _settingsCache.Set(
            settings.PreparingDelaySeconds,
            settings.ReadyForPickupDelaySeconds,
            settings.UpdatedAt,
            DateTime.UtcNow);
        return settings;
    }
}
