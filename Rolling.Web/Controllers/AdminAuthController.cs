using Microsoft.AspNetCore.Mvc;
using Rolling.Web.Auth;
using Rolling.Web.Services.Sms;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/admin/auth")]
public sealed class AdminAuthController : ControllerBase
{
    private const string OwnerPhoneDigits = "998931102200";

    private readonly AdminAuthService _authService;
    private readonly ISmsService _smsService;

    public AdminAuthController(AdminAuthService authService, ISmsService smsService)
    {
        _authService = authService;
        _smsService = smsService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var login = request.Login ?? request.Email;
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Login and password are required." });
        }

        var tokens = await _authService.LoginAsync(login, request.Password, cancellationToken);
        if (tokens is null)
        {
            return Unauthorized(new { error = "Invalid login or password." });
        }

        return Ok(ToAuthResponse(tokens));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required." });
        }

        var tokens = await _authService.RefreshAsync(request.RefreshToken, cancellationToken);
        if (tokens is null)
        {
            return Unauthorized(new { error = "Refresh token is invalid or expired." });
        }

        return Ok(ToAuthResponse(tokens));
    }

    [HttpPost("forgot-password/request")]
    public async Task<IActionResult> RequestForgotPasswordOtpAsync(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPhone = NormalizePhone(request.Phone);
        if (normalizedPhone != OwnerPhoneDigits)
        {
            return BadRequest(new { error = "Unsupported phone number for admin recovery." });
        }

        var result = await _smsService.RequestLoginCodeAsync(OwnerPhoneDigits, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new { error = result.Message, code = result.Code });
        }

        return Ok(new
        {
            success = true,
            phone = $"+{OwnerPhoneDigits}",
            code = result.Code,
            expiresAt = result.ExpiresAt
        });
    }

    [HttpPost("forgot-password/reset")]
    public async Task<IActionResult> ResetForgottenPasswordAsync(
        [FromBody] ForgotPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPhone = NormalizePhone(request.Phone);
        if (normalizedPhone != OwnerPhoneDigits)
        {
            return BadRequest(new { error = "Unsupported phone number for admin recovery." });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "OTP code is required." });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Trim().Length < 8)
        {
            return BadRequest(new { error = "New password must be at least 8 characters long." });
        }

        var verifyResult = _smsService.VerifyLoginCode(OwnerPhoneDigits, request.Code.Trim());
        if (!verifyResult.Success)
        {
            return BadRequest(new { error = verifyResult.Message, code = verifyResult.Code, expired = verifyResult.Expired });
        }

        try
        {
            var changed = await _authService.ResetPasswordByOwnerOtpAsync(request.NewPassword, cancellationToken);
            if (!changed)
            {
                return NotFound(new { error = "No admin account found." });
            }

            return Ok(new { success = true, message = "Password reset successful. Please login again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("logout")]
    [AdminAuthorize]
    public async Task<IActionResult> LogoutAsync([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _authService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        }

        return Ok(new { success = true });
    }

    [HttpGet("me")]
    [AdminAuthorize]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        var session = HttpContext.GetAdminSession();
        if (session is null)
        {
            return Unauthorized(new { error = "Unauthorized" });
        }

        var user = await _authService.GetUserAsync(session.AdminId, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new { error = "Unauthorized" });
        }

        return Ok(ToUserResponse(user));
    }

    [HttpPost("change-credentials")]
    [AdminAuthorize]
    public async Task<IActionResult> ChangeCredentialsAsync(
        [FromBody] ChangeCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        var session = HttpContext.GetAdminSession();
        if (session is null)
        {
            return Unauthorized(new { error = "Unauthorized" });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(new { error = "Current password is required." });
        }

        if (string.IsNullOrWhiteSpace(request.NewLogin) && string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Provide at least one field to update." });
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword) && request.NewPassword.Trim().Length < 8)
        {
            return BadRequest(new { error = "New password must be at least 8 characters long." });
        }

        try
        {
            var tokens = await _authService.ChangeCredentialsAsync(
                session.AdminId,
                request.CurrentPassword,
                request.NewLogin,
                request.NewPassword,
                cancellationToken);

            if (tokens is null)
            {
                return Unauthorized(new { error = "Current password is invalid." });
            }

            return Ok(ToAuthResponse(tokens));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static object ToAuthResponse(AdminAuthTokens tokens)
    {
        return new
        {
            token = tokens.AccessToken,
            access_token = tokens.AccessToken,
            accessToken = tokens.AccessToken,
            accessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            refresh_token = tokens.RefreshToken,
            refreshToken = tokens.RefreshToken,
            refreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
            user = ToUserResponse(tokens.User)
        };
    }

    private static object ToUserResponse(AdminAuthUser user)
    {
        return new
        {
            id = user.Id.ToString(),
            user_id = user.Id,
            name = user.Name,
            login = user.Login,
            role = user.RoleName,
            role_name = user.RoleName
        };
    }

    public sealed record LoginRequest(string? Login, string? Email, string? Password);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record LogoutRequest(string? RefreshToken);

    public sealed record ChangeCredentialsRequest(string? CurrentPassword, string? NewLogin, string? NewPassword);

    public sealed record ForgotPasswordRequest(string? Phone);

    public sealed record ForgotPasswordResetRequest(string? Phone, string? Code, string? NewPassword);

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits;
    }
}
