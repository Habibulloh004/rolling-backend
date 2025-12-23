using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Models.Poster;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class CourierAuthController : ControllerBase
{
    private static readonly string[] CourierRoleNames = { "курьер", "кур’єр", "курьер" };

    private readonly AppDbContext _dbContext;
    private readonly PosterService _posterService;

    public CourierAuthController(AppDbContext dbContext, PosterService posterService)
    {
        _dbContext = dbContext;
        _posterService = posterService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] CourierLoginRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var document = await _posterService.GetEmployeesAsync(cancellationToken);
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Unable to reach Poster" });
        }

        PosterUser? posterUser = null;
        string? posterLogin = null;

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var employees) || employees.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return NotFound(new { message = "User not found" });
            }

            foreach (var employee in employees.EnumerateArray())
            {
                var login = employee.TryGetProperty("login", out var loginElement) ? loginElement.GetString() : null;
                var roleName = employee.TryGetProperty("role_name", out var roleElement) ? roleElement.GetString() : null;

                if (string.IsNullOrWhiteSpace(login) || !IsCourierRole(roleName))
                {
                    continue;
                }

                if (!string.Equals(login, request.Email, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                posterUser = MapPosterUser(employee);
                posterLogin = login;
                break;
            }
        }

        if (posterUser is null || string.IsNullOrWhiteSpace(posterLogin))
        {
            return NotFound(new { message = "User not found" });
        }

        var existing = await _dbContext.Users
            .FirstOrDefaultAsync(user => user.Login != null && user.Login.ToLower() == normalizedEmail, cancellationToken);

        if (existing is null)
        {
            await _dbContext.Users.AddAsync(posterUser, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            existing = posterUser;
        }

        return Ok(ToUserResponse(existing));
    }

    private static bool IsCourierRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var normalized = roleName.Trim().ToLowerInvariant();
        return CourierRoleNames.Contains(normalized);
    }

    private static PosterUser MapPosterUser(System.Text.Json.JsonElement element)
    {
        long GetLong(string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
            {
                return 0;
            }

            return prop.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when prop.TryGetInt64(out var number) => number,
                System.Text.Json.JsonValueKind.String when long.TryParse(prop.GetString(), out var parsed) => parsed,
                _ => 0
            };
        }

        string? GetString(string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
        }

        return new PosterUser
        {
            UserId = GetLong("user_id"),
            Name = GetString("name"),
            Login = GetString("login") ?? string.Empty,
            RoleName = GetString("role_name"),
            RoleId = GetLong("role_id"),
            UserType = GetLong("user_type"),
            AccessMask = GetLong("access_mask"),
            LastIn = GetString("last_in")
        };
    }

    private static object ToUserResponse(PosterUser user) => new
    {
        user_id = user.UserId,
        name = user.Name,
        login = user.Login,
        role_name = user.RoleName,
        role_id = user.RoleId,
        user_type = user.UserType,
        access_mask = user.AccessMask,
        last_in = user.LastIn
    };
}
