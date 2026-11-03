using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Jarvis.Infrastructure.Identity;

/// <summary>Who may call <c>POST /auth/register</c>.</summary>
public enum RegistrationMode
{
    /// <summary>Anyone (Development and test deployments that need several accounts).</summary>
    Open,
    /// <summary>Only while no account exists: the owner signs up once and registration closes itself.</summary>
    FirstAccount,
    /// <summary>Nobody.</summary>
    Closed,
}

public sealed class AccountTokenOptions
{
    public const string DevelopmentSigningKey = "dev-only-jarvis-signing-key-32chars!";

    public string Issuer { get; init; } = "jarvis";
    public string Audience { get; init; } = "jarvis-api";
    public string SigningKey { get; init; } = "";
    public RegistrationMode Registration { get; init; } = RegistrationMode.Open;
    public bool AllowRegistration => Registration != RegistrationMode.Closed;
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);

    public SymmetricSecurityKey SecurityKey => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SecurityKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "sub",
    };

    /// <summary>
    /// <c>true</c> opens registration, <c>false</c> closes it, and <c>first</c> allows it only until the first
    /// account exists. Unset means open, as before.
    /// </summary>
    public static RegistrationMode ParseRegistration(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "true" or "open" => RegistrationMode.Open,
        "false" or "closed" => RegistrationMode.Closed,
        "first" or "first-account" or "bootstrap" => RegistrationMode.FirstAccount,
        _ => throw new InvalidOperationException(
            "Authentication:AllowRegistration must be true, false or first."),
    };

    public static AccountTokenOptions From(IConfiguration configuration, bool isDevelopment)
    {
        var signingKey = configuration["Authentication:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!isDevelopment)
                throw new InvalidOperationException(
                    "Configure Authentication:SigningKey with at least 32 bytes before running Jarvis outside Development.");
            signingKey = DevelopmentSigningKey;
        }

        if (!isDevelopment && string.Equals(signingKey, DevelopmentSigningKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Replace Authentication:SigningKey before running Jarvis outside Development.");

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Authentication:SigningKey must contain at least 32 bytes.");

        var issuer = configuration["Authentication:Issuer"];
        var audience = configuration["Authentication:Audience"];
        if (!isDevelopment && (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience)))
            throw new InvalidOperationException(
                "Configure Authentication:Issuer and Authentication:Audience before running Jarvis outside Development.");

        return new AccountTokenOptions
        {
            Issuer = string.IsNullOrWhiteSpace(issuer) ? "jarvis" : issuer.Trim(),
            Audience = string.IsNullOrWhiteSpace(audience) ? "jarvis-api" : audience.Trim(),
            SigningKey = signingKey,
            Registration = ParseRegistration(configuration["Authentication:AllowRegistration"]),
            AccessTokenLifetime = TimeSpan.FromMinutes(
                Math.Clamp(configuration.GetValue("Authentication:AccessTokenMinutes", 15), 5, 120)),
            RefreshTokenLifetime = TimeSpan.FromDays(
                Math.Clamp(configuration.GetValue("Authentication:RefreshTokenDays", 30), 1, 90)),
        };
    }
}
