using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Auth;

public sealed class AdminAuthService
{
    private const int PasswordHashIterations = 120_000;
    private const int PasswordHashSize = 32;
    private const int PasswordSaltSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly AdminAuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdminAuthService> _logger;
    private readonly byte[] _signingKeyBytes;

    public AdminAuthService(
        AppDbContext dbContext,
        IOptions<AdminAuthOptions> options,
        TimeProvider timeProvider,
        ILogger<AdminAuthService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            _logger.LogWarning("AdminAuth signing key is not configured. Using an insecure fallback key; set AdminAuth:SigningKey.");
            _signingKeyBytes = Encoding.UTF8.GetBytes("rolling-admin-insecure-signing-key-change-me");
        }
        else
        {
            _signingKeyBytes = Encoding.UTF8.GetBytes(_options.SigningKey.Trim());
        }
    }

    public async Task EnsureSeedAdminAsync(CancellationToken cancellationToken = default)
    {
        if (await _dbContext.AdminCredentials.AsNoTracking().AnyAsync(cancellationToken))
        {
            return;
        }

        var login = string.IsNullOrWhiteSpace(_options.SeedLogin) ? "admin" : _options.SeedLogin.Trim();
        var password = string.IsNullOrWhiteSpace(_options.SeedPassword) ? "admin12345" : _options.SeedPassword;

        if (password.Length < 8)
        {
            password = "admin12345";
        }

        var (passwordHash, passwordSalt) = HashPassword(password);
        var now = _timeProvider.GetUtcNow();

        var credential = new AdminCredential
        {
            Login = login,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            Name = "Administrator",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _dbContext.AdminCredentials.AddAsync(credential, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded initial admin credential with login '{Login}'", login);
    }

    public async Task<AdminAuthTokens?> LoginAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalized = login.Trim().ToLowerInvariant();

        var credential = await _dbContext.AdminCredentials
            .FirstOrDefaultAsync(item => item.Login.ToLower() == normalized, cancellationToken);

        if (credential is null)
        {
            return null;
        }

        if (!VerifyPassword(password, credential.PasswordHash, credential.PasswordSalt))
        {
            return null;
        }

        return await IssueTokensAsync(credential, null, cancellationToken);
    }

    public async Task<AdminAuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = ComputeSha256Hex(refreshToken);
        var now = _timeProvider.GetUtcNow();

        var existingToken = await _dbContext.AdminRefreshTokens
            .Include(token => token.AdminCredential)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            return null;
        }

        if (existingToken.RevokedAt.HasValue || existingToken.ExpiresAt <= now)
        {
            if (!existingToken.RevokedAt.HasValue)
            {
                existingToken.RevokedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        return await IssueTokensAsync(existingToken.AdminCredential, existingToken, cancellationToken);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var tokenHash = ComputeSha256Hex(refreshToken);
        var token = await _dbContext.AdminRefreshTokens
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);

        if (token is null || token.RevokedAt.HasValue)
        {
            return false;
        }

        token.RevokedAt = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminAuthUser?> GetUserAsync(long adminId, CancellationToken cancellationToken = default)
    {
        var credential = await _dbContext.AdminCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == adminId, cancellationToken);

        return credential is null ? null : MapUser(credential);
    }

    public async Task<AdminAuthTokens?> ChangeCredentialsAsync(
        long adminId,
        string currentPassword,
        string? newLogin,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return null;
        }

        var credential = await _dbContext.AdminCredentials
            .FirstOrDefaultAsync(item => item.Id == adminId, cancellationToken);

        if (credential is null)
        {
            return null;
        }

        if (!VerifyPassword(currentPassword, credential.PasswordHash, credential.PasswordSalt))
        {
            return null;
        }

        var normalizedLogin = string.IsNullOrWhiteSpace(newLogin) ? credential.Login : newLogin.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLogin))
        {
            normalizedLogin = credential.Login;
        }

        if (!string.Equals(normalizedLogin, credential.Login, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _dbContext.AdminCredentials
                .AsNoTracking()
                .AnyAsync(
                    item => item.Id != credential.Id && item.Login.ToLower() == normalizedLogin.ToLower(),
                    cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException("Login is already in use.");
            }

            credential.Login = normalizedLogin;
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            var passwordToSet = newPassword.Trim();
            if (passwordToSet.Length < 8)
            {
                throw new InvalidOperationException("New password must be at least 8 characters long.");
            }

            var (passwordHash, passwordSalt) = HashPassword(passwordToSet);
            credential.PasswordHash = passwordHash;
            credential.PasswordSalt = passwordSalt;
        }

        var now = _timeProvider.GetUtcNow();
        credential.UpdatedAt = now;

        var activeTokens = await _dbContext.AdminRefreshTokens
            .Where(token => token.AdminCredentialId == credential.Id &&
                            !token.RevokedAt.HasValue &&
                            token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        return await IssueTokensAsync(credential, null, cancellationToken);
    }

    public async Task<bool> ResetPasswordByOwnerOtpAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        var passwordToSet = (newPassword ?? string.Empty).Trim();
        if (passwordToSet.Length < 8)
        {
            throw new InvalidOperationException("New password must be at least 8 characters long.");
        }

        var seedLogin = (_options.SeedLogin ?? string.Empty).Trim();
        AdminCredential? credential = null;

        if (!string.IsNullOrWhiteSpace(seedLogin))
        {
            var normalized = seedLogin.ToLowerInvariant();
            credential = await _dbContext.AdminCredentials
                .FirstOrDefaultAsync(item => item.Login.ToLower() == normalized, cancellationToken);
        }

        credential ??= await _dbContext.AdminCredentials
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (credential is null)
        {
            return false;
        }

        var (passwordHash, passwordSalt) = HashPassword(passwordToSet);
        credential.PasswordHash = passwordHash;
        credential.PasswordSalt = passwordSalt;
        credential.UpdatedAt = _timeProvider.GetUtcNow();

        var now = _timeProvider.GetUtcNow();
        var activeTokens = await _dbContext.AdminRefreshTokens
            .Where(token => token.AdminCredentialId == credential.Id &&
                            !token.RevokedAt.HasValue &&
                            token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public AdminAuthSession? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var payloadPart = parts[0];
        var signaturePart = parts[1];

        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(signaturePart);
        }
        catch
        {
            return null;
        }

        var expectedSignature = ComputeSignature(payloadPart);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return null;
        }

        AccessTokenPayload? payload;
        try
        {
            var payloadBytes = Base64UrlDecode(payloadPart);
            payload = JsonSerializer.Deserialize<AccessTokenPayload>(payloadBytes, JsonOptions);
        }
        catch
        {
            return null;
        }

        if (payload is null || payload.Ver != 1 || payload.Sid <= 0 || string.IsNullOrWhiteSpace(payload.Login))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
        if (expiresAt <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new AdminAuthSession(payload.Sid, payload.Login, expiresAt, payload.Jti);
    }

    private async Task<AdminAuthTokens> IssueTokensAsync(
        AdminCredential credential,
        AdminRefreshToken? rotatedToken,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var accessLifetime = TimeSpan.FromMinutes(Math.Clamp(_options.AccessTokenLifetimeMinutes, 5, 180));
        var refreshLifetime = TimeSpan.FromDays(Math.Clamp(_options.RefreshTokenLifetimeDays, 1, 90));

        var accessExpiresAt = now.Add(accessLifetime);
        var refreshExpiresAt = now.Add(refreshLifetime);

        var tokenId = GenerateOpaqueToken(16);
        var accessToken = GenerateAccessToken(credential, tokenId, now, accessExpiresAt);

        var refreshToken = GenerateOpaqueToken(48);
        var refreshTokenHash = ComputeSha256Hex(refreshToken);

        if (rotatedToken is not null)
        {
            rotatedToken.RevokedAt = now;
            rotatedToken.ReplacedByTokenHash = refreshTokenHash;
        }

        var refreshTokenEntity = new AdminRefreshToken
        {
            AdminCredentialId = credential.Id,
            TokenHash = refreshTokenHash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt
        };

        await _dbContext.AdminRefreshTokens.AddAsync(refreshTokenEntity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AdminAuthTokens(
            accessToken,
            accessExpiresAt,
            refreshToken,
            refreshExpiresAt,
            MapUser(credential));
    }

    private string GenerateAccessToken(
        AdminCredential credential,
        string tokenId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var payload = new AccessTokenPayload(
            Ver: 1,
            Sid: credential.Id,
            Login: credential.Login,
            Iat: issuedAt.ToUnixTimeSeconds(),
            Exp: expiresAt.ToUnixTimeSeconds(),
            Jti: tokenId);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadPart = Base64UrlEncode(payloadBytes);

        var signatureBytes = ComputeSignature(payloadPart);
        var signaturePart = Base64UrlEncode(signatureBytes);

        return $"{payloadPart}.{signaturePart}";
    }

    private byte[] ComputeSignature(string payloadPart)
    {
        return HMACSHA256.HashData(_signingKeyBytes, Encoding.UTF8.GetBytes(payloadPart));
    }

    private static string GenerateOpaqueToken(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeSha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, PasswordHashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private static bool VerifyPassword(string password, string hashBase64, string saltBase64)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hashBase64) || string.IsNullOrWhiteSpace(saltBase64))
        {
            return false;
        }

        byte[] expectedHash;
        byte[] salt;

        try
        {
            expectedHash = Convert.FromBase64String(hashBase64);
            salt = Convert.FromBase64String(saltBase64);
        }
        catch
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static AdminAuthUser MapUser(AdminCredential credential)
    {
        return new AdminAuthUser(credential.Id, credential.Login, credential.Name, "admin");
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = normalized.Length % 4;
        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(normalized);
    }

    private sealed record AccessTokenPayload(
        int Ver,
        long Sid,
        string Login,
        long Iat,
        long Exp,
        string Jti);
}
