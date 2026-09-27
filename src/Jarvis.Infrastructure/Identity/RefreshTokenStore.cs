using System.Security.Cryptography;
using System.Text;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Identity;

public sealed class RefreshTokenStore(JarvisDbContext database)
{
    public readonly record struct Rotation(Guid? UserId, string? RefreshToken)
    {
        public bool Succeeded => UserId is not null && !string.IsNullOrEmpty(RefreshToken);
    }

    public async Task<string> IssueAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var token = CreateToken();
        var now = DateTimeOffset.UtcNow;
        database.AuthRefreshTokens.Add(new AuthRefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        });
        await database.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<Rotation> RotateAsync(string? refreshToken, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
            return default;

        var hash = Hash(refreshToken);
        var now = DateTimeOffset.UtcNow;
        var replacement = CreateToken();
        var replacementHash = Hash(replacement);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var updated = await database.AuthRefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAt == null && token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.ReplacedByHash, replacementHash), cancellationToken);

        if (updated == 1)
        {
            var userId = await database.AuthRefreshTokens.AsNoTracking()
                .Where(token => token.TokenHash == hash)
                .Select(token => token.UserId)
                .SingleAsync(cancellationToken);
            database.AuthRefreshTokens.Add(new AuthRefreshToken
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                TokenHash = replacementHash,
                CreatedAt = now,
                ExpiresAt = now.Add(lifetime),
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new Rotation(userId, replacement);
        }

        var existing = await database.AuthRefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => new { token.UserId, token.RevokedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing?.RevokedAt is not null)
        {
            await database.AuthRefreshTokens
                .Where(token => token.UserId == existing.UserId && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return default;
    }

    public async Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
            return;

        var hash = Hash(refreshToken);
        var now = DateTimeOffset.UtcNow;
        await database.AuthRefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
    }

    public static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
