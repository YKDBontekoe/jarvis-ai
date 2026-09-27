using System.IdentityModel.Tokens.Jwt;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class AccountTokenOptionsTests
{
    [Fact]
    public void Development_uses_the_local_signing_key_when_none_is_configured()
    {
        var options = AccountTokenOptions.From(new ConfigurationBuilder().Build(), isDevelopment: true);
        var userId = Guid.CreateVersion7();

        Assert.Equal(AccountTokenOptions.DevelopmentSigningKey, options.SigningKey);
        Assert.Equal("jarvis", options.Issuer);
        Assert.Equal("jarvis-api", options.Audience);
        var (token, expiresAt) = AccessTokenIssuer.Create(userId, "owner@example.com", options);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, options.ValidationParameters(), out _);
        var subject = principal.FindFirst("sub")?.Value;
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(userId.ToString(), subject);
    }

    [Fact]
    public void Production_rejects_a_missing_or_development_signing_key()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccountTokenOptions.From(new ConfigurationBuilder().Build(), isDevelopment: false));

        var developmentKey = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "https://jarvis.example.com",
            ["Authentication:Audience"] = "jarvis-api",
            ["Authentication:SigningKey"] = AccountTokenOptions.DevelopmentSigningKey,
        }).Build();
        Assert.Throws<InvalidOperationException>(() => AccountTokenOptions.From(developmentKey, isDevelopment: false));
    }
}

public sealed class IdentityAccountTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
    private ServiceProvider? _services;
    private AccountTokenOptions _tokens = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tokens = AccountTokenOptions.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "jarvis-test",
            ["Authentication:Audience"] = "jarvis-api",
            ["Authentication:SigningKey"] = "integration-test-signing-key-32b!!",
        }).Build(), isDevelopment: false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<JarvisDbContext>(options =>
            options.UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector()));
        services.AddJarvisIdentity(_tokens);
        _services = services.BuildServiceProvider();

        await using var scope = _services!.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Password_sign_in_issues_an_account_token_and_rotates_refresh_tokens()
    {
        await using var scope = _services!.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<JarvisUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<JarvisUser>>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<RefreshTokenStore>();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var user = new JarvisUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var created = await users.CreateAsync(user, "Correct-horse-1");
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.False((await users.CreateAsync(new JarvisUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "short@example.com",
            Email = "short@example.com",
            EmailConfirmed = true,
        }, "short")).Succeeded);

        var signedIn = await signIn.CheckPasswordSignInAsync(user, "Correct-horse-1", lockoutOnFailure: false);
        Assert.True(signedIn.Succeeded);
        Assert.False((await signIn.CheckPasswordSignInAsync(user, "Wrong-horse-1", lockoutOnFailure: false)).Succeeded);

        var (accessToken, _) = AccessTokenIssuer.Create(user.Id, user.Email, _tokens);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(accessToken, _tokens.ValidationParameters(), out _);
        Assert.Equal(user.Id.ToString(), principal.FindFirst("sub")?.Value);

        var refreshToken = await refreshTokens.IssueAsync(user.Id, TimeSpan.FromDays(1), CancellationToken.None);
        var rotated = await refreshTokens.RotateAsync(refreshToken, TimeSpan.FromDays(1), CancellationToken.None);
        Assert.Equal(user.Id, rotated.UserId);
        Assert.NotEqual(refreshToken, rotated.RefreshToken);

        Assert.False((await refreshTokens.RotateAsync(refreshToken, TimeSpan.FromDays(1), CancellationToken.None)).Succeeded);
        Assert.False((await refreshTokens.RotateAsync(rotated.RefreshToken, TimeSpan.FromDays(1), CancellationToken.None)).Succeeded);
        await refreshTokens.RevokeAsync(rotated.RefreshToken, CancellationToken.None);
    }
}
