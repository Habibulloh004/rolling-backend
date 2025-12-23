using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Models.Time;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class TimeController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public TimeController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("get_time")]
    public async Task<IActionResult> GetTimeAsync(CancellationToken cancellationToken)
    {
        var time = await _dbContext.Times.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return time is null ? NotFound() : Ok(time);
    }

    [HttpPost("edit_time")]
    public async Task<IActionResult> EditTimeAsync([FromBody] TimeUpdateRequest request, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Times.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            await _dbContext.Times.AddAsync(new BusinessTime
            {
                OpenedTime = request.OpenedTime,
                ClosedTime = request.ClosedTime
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { success = true });
        }

        existing.OpenedTime = request.OpenedTime;
        existing.ClosedTime = request.ClosedTime;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true });
    }
}
