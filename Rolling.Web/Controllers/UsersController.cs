using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Web.Auth;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/users")]
[AdminAuthorize]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public UsersController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(take, 1, 500);
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderByDescending(u => u.UserId)
            .Take(size)
            .Select(u => new
            {
                user_id = u.UserId,
                name = u.Name,
                login = u.Login,
                role_name = u.RoleName,
                role_id = u.RoleId,
                user_type = u.UserType,
                access_mask = u.AccessMask,
                last_in = u.LastIn
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }
}
