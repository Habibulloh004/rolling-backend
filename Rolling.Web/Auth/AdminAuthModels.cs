namespace Rolling.Web.Auth;

public sealed record AdminAuthUser(
    long Id,
    string Login,
    string? Name,
    string RoleName = "admin");

public sealed record AdminAuthSession(
    long AdminId,
    string Login,
    DateTimeOffset ExpiresAt,
    string TokenId);

public sealed record AdminAuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AdminAuthUser User);
