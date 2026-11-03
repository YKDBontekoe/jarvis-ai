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
using Microsoft.AspNetCore.Http;
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

    [Theory]
    [InlineData(null, RegistrationMode.Open)]
    [InlineData("true", RegistrationMode.Open)]
    [InlineData("True", RegistrationMode.Open)]
    [InlineData("false", RegistrationMode.Closed)]
    [InlineData("first", RegistrationMode.FirstAccount)]
    [InlineData(" FIRST ", RegistrationMode.FirstAccount)]
    public void Registration_mode_reads_true_false_and_first(string? value, RegistrationMode expected) =>
        Assert.Equal(expected, AccountTokenOptions.ParseRegistration(value));

    [Fact]
    public void Registration_mode_rejects_an_unknown_value() =>
        Assert.Throws<InvalidOperationException>(() => AccountTokenOptions.ParseRegistration("sometimes"));
}

public sealed class IdentityAccountTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
    private ServiceProvider? _services;
    private AccountTokenOptions _tokens = null!;

    public async ValueTask InitializeAsync()
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

    public async ValueTask DisposeAsync()
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

    [Fact]
    public async Task Pruning_removes_long_expired_refresh_tokens_but_keeps_live_and_revoked_ones()
    {
        await using var scope = _services!.CreateAsyncScope();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<RefreshTokenStore>();
        var database = scope.ServiceProvider.GetRequiredService<Jarvis.Infrastructure.Persistence.JarvisDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<JarvisUser>>();
        var email = $"prune-{Guid.NewGuid():N}@example.com";
        var user = new JarvisUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, "Correct-horse-1")).Succeeded);

        var longGone = await refreshTokens.IssueAsync(user.Id, TimeSpan.FromDays(-3), CancellationToken.None);
        var live = await refreshTokens.IssueAsync(user.Id, TimeSpan.FromDays(30), CancellationToken.None);
        var rotatedAway = await refreshTokens.IssueAsync(user.Id, TimeSpan.FromDays(30), CancellationToken.None);
        var replacement = await refreshTokens.RotateAsync(rotatedAway, TimeSpan.FromDays(30), CancellationToken.None);

        var removed = await refreshTokens.PruneAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await database.AuthRefreshTokens.AnyAsync(t => t.TokenHash == RefreshTokenStore.Hash(longGone)));
        Assert.True(await database.AuthRefreshTokens.AnyAsync(t => t.TokenHash == RefreshTokenStore.Hash(live)));
        // The rotated-away token is still known, so replaying it still revokes the whole session.
        Assert.False((await refreshTokens.RotateAsync(rotatedAway, TimeSpan.FromDays(30), CancellationToken.None)).Succeeded);
        Assert.False((await refreshTokens.RotateAsync(replacement.RefreshToken, TimeSpan.FromDays(30), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task First_account_registration_admits_one_owner_then_closes()
    {
        var tokens = WithRegistration(RegistrationMode.FirstAccount);

        // Several sign-ups at once on an empty server: exactly one becomes the owner.
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(index =>
            RegisterAsync(tokens, $"first-{index}-{Guid.NewGuid():N}@example.com")));

        Assert.Equal(1, results.Count(status => status == StatusCodes.Status200OK));
        Assert.Equal(4, results.Count(status => status == StatusCodes.Status403Forbidden));
        Assert.Equal(StatusCodes.Status403Forbidden, await RegisterAsync(tokens, $"later-{Guid.NewGuid():N}@example.com"));
        await using var scope = _services!.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<UserManager<JarvisUser>>().Users.CountAsync());
    }

    [Fact]
    public async Task Open_registration_admits_more_accounts_and_closed_admits_none()
    {
        Assert.Equal(StatusCodes.Status403Forbidden,
            await RegisterAsync(WithRegistration(RegistrationMode.Closed), $"closed-{Guid.NewGuid():N}@example.com"));
        var open = WithRegistration(RegistrationMode.Open);
        Assert.Equal(StatusCodes.Status200OK, await RegisterAsync(open, $"a-{Guid.NewGuid():N}@example.com"));
        Assert.Equal(StatusCodes.Status200OK, await RegisterAsync(open, $"b-{Guid.NewGuid():N}@example.com"));
    }

    private AccountTokenOptions WithRegistration(RegistrationMode mode) => new()
    {
        Issuer = _tokens.Issuer,
        Audience = _tokens.Audience,
        SigningKey = _tokens.SigningKey,
        Registration = mode,
    };

    private async Task<int> RegisterAsync(AccountTokenOptions tokens, string email)
    {
        await using var scope = _services!.CreateAsyncScope();
        var result = await Jarvis.Api.Security.IdentityAuthEndpoints.Register(
            new Jarvis.Api.Security.AuthCredentialsRequest(email, "Correct-horse-1"),
            scope.ServiceProvider.GetRequiredService<UserManager<JarvisUser>>(),
            scope.ServiceProvider.GetRequiredService<RefreshTokenStore>(), tokens, CancellationToken.None);
        return Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result).StatusCode
               ?? StatusCodes.Status200OK;
    }
}
