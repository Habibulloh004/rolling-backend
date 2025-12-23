using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Models.News;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class NewsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly NotificationService _notificationService;

    public NewsController(AppDbContext dbContext, NotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    [HttpGet("getNews")]
    public async Task<IActionResult> GetNewsAsync(CancellationToken cancellationToken)
    {
        var news = await _dbContext.Notifications
            .AsNoTracking()
            .Select(x => new
            {
                id = x.Id,
                en = new { title = x.EnTitle, body = x.EnBody },
                ru = new { title = x.RuTitle, body = x.RuBody },
                uz = new { title = x.UzTitle, body = x.UzBody }
            })
            .ToListAsync(cancellationToken);

        return Ok(news);
    }

    [HttpPost("createNews")]
    public async Task<IActionResult> CreateNewsAsync([FromBody] CreateNewsRequest request, CancellationToken cancellationToken)
    {
        var languages = new[] { "en", "ru", "uz" };
        var results = new List<object>();

        foreach (var language in languages)
        {
            NotificationPayload? custom = null;
            if (request.CustomMessages is not null && request.CustomMessages.TryGetValue(language, out var payload))
            {
                custom = new NotificationPayload(payload.Title, payload.Body);
            }
            else
            {
                var fallback = language switch
                {
                    "en" => request.En,
                    "ru" => request.Ru,
                    "uz" => request.Uz,
                    _ => null
                };
                if (fallback is not null)
                {
                    custom = new NotificationPayload(fallback.Title, fallback.Body);
                }
            }

            var topic = $"all_users_{language}";
            try
            {
                await _notificationService.SendToTopicAsync(topic, language, request.MessageType, custom, request.Data, cancellationToken);
                results.Add(new { language, topic, success = true });
            }
            catch (Exception ex)
            {
                results.Add(new { language, topic, success = false, error = ex.Message });
            }
        }

        if (request.En is not null || request.Ru is not null || request.Uz is not null)
        {
            await _dbContext.Notifications.AddAsync(request.ToEntity(), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { success = true, results });
    }

    [HttpDelete("deleteNews/{newsId}")]
    public async Task<IActionResult> DeleteNewsAsync(string newsId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(newsId, out var idValue))
        {
            return BadRequest(new { error = "Invalid news id" });
        }

        var entity = await _dbContext.Notifications.FirstOrDefaultAsync(x => x.Id == idValue, cancellationToken);
        if (entity is null)
        {
            return NotFound(new { error = "News not found" });
        }

        _dbContext.Notifications.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { deleted = 1 });
    }
}
