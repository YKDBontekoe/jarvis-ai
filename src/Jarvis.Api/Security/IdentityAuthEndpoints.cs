using System.ComponentModel.DataAnnotations;
using Jarvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Security;

public static class IdentityAuthEndpoints
{
    private static readonly string DummyPasswordHash = new PasswordHasher<JarvisUser>()
        .HashPassword(new JarvisUser(), "not-a-real-password");

    public static RouteGroupBuilder MapAccountAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/register", Register).AllowAnonymous();
        auth.MapPost("/login", Login).AllowAnonymous();
        auth.MapPost("/refresh", Refresh).AllowAnonymous();
        auth.MapPost("/logout", Logout).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> Register(
        AuthCredentialsRequest request,
        UserManager<JarvisUser> users,
        RefreshTokenStore refreshTokens,
        AccountTokenOptions tokens,
        CancellationToken cancellationToken)
    {
        if (!tokens.AllowRegistration)
            return Results.Json(new { message = "Account registration is disabled." }, statusCode: StatusCodes.Status403Forbidden);

        var email = request.Email?.Trim() ?? "";
        var password = request.Password ?? "";
        if (!IsEmail(email))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Enter a valid email address."],
            });
        if (password.Length is < 8 or > 128)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password must contain 8 to 128 characters."],
            });

        var user = new JarvisUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "An account with that email already exists." });
        }
        if (!created.Succeeded)
        {
            if (created.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
                return Results.Conflict(new { message = "An account with that email already exists." });

            var errors = created.Errors
                .GroupBy(error => error.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).Distinct().ToArray());
            return Results.ValidationProblem(errors);
        }

        return await IssueSession(user, refreshTokens, tokens, cancellationToken);
    }

    private static async Task<IResult> Login(
        AuthCredentialsRequest request,
        UserManager<JarvisUser> users,
        SignInManager<JarvisUser> signIn,
        RefreshTokenStore refreshTokens,
        AccountTokenOptions tokens,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? "";
        var password = request.Password ?? "";
        if (!IsEmail(email) || password.Length is < 1 or > 128)
            return InvalidCredentials();

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new JarvisUser(), DummyPasswordHash, password);
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Results.Json(new { message = "This account is temporarily locked. Try again later." },
                statusCode: StatusCodes.Status401Unauthorized);
        if (!result.Succeeded)
            return InvalidCredentials();

        return await IssueSession(user, refreshTokens, tokens, cancellationToken);
    }

    private static async Task<IResult> Refresh(
        RefreshTokenRequest request,
        UserManager<JarvisUser> users,
        RefreshTokenStore refreshTokens,
        AccountTokenOptions tokens,
        CancellationToken cancellationToken)
    {
        var rotated = await refreshTokens.RotateAsync(request.RefreshToken, tokens.RefreshTokenLifetime, cancellationToken);
        if (!rotated.Succeeded)
            return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status401Unauthorized);

        var user = await users.FindByIdAsync(rotated.UserId!.Value.ToString());
        if (user is null)
            return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status401Unauthorized);

        var (accessToken, expiresAt) = AccessTokenIssuer.Create(user.Id, user.Email, tokens);
        return Results.Ok(new AuthSessionResponse(accessToken, rotated.RefreshToken!, expiresAt, user.Email ?? ""));
    }

    private static async Task<IResult> Logout(RefreshTokenRequest request, RefreshTokenStore refreshTokens,
        CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> IssueSession(JarvisUser user, RefreshTokenStore refreshTokens,
        AccountTokenOptions tokens, CancellationToken cancellationToken)
    {
        var (accessToken, expiresAt) = AccessTokenIssuer.Create(user.Id, user.Email, tokens);
        var refreshToken = await refreshTokens.IssueAsync(user.Id, tokens.RefreshTokenLifetime, cancellationToken);
        return Results.Ok(new AuthSessionResponse(accessToken, refreshToken, expiresAt, user.Email ?? ""));
    }

    private static IResult InvalidCredentials() =>
        Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);

    private static bool IsEmail(string email) =>
        email.Length <= 256 && new EmailAddressAttribute().IsValid(email);
}

public sealed record AuthCredentialsRequest(string? Email, string? Password);
public sealed record RefreshTokenRequest(string? RefreshToken);
public sealed record AuthSessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string Email);
