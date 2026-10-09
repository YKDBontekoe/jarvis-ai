using System.Net.Http.Headers;
using Jarvis.Application.Computer;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Infrastructure.Computer;

/// <summary>
/// Where the computer-use sandbox (infra/computer) listens. Set by the AppHost's <c>computer</c> feature; when
/// <see cref="ControlUrl"/> is empty the feature is off.
/// </summary>
/// <param name="ControlUrl">The sandbox control server (desktop MCP, /reset, /health), e.g. http://computer-sandbox:8932.</param>
/// <param name="ViewUrl">websockify with the noVNC client, e.g. http://computer-sandbox:6080.</param>
/// <param name="Token">Bearer token the control server requires.</param>
/// <param name="IdleTimeout">How long an unused session keeps the sandbox before another conversation may take it.</param>
public sealed record ComputerSandboxOptions(Uri? ControlUrl, Uri? ViewUrl, string? Token, TimeSpan IdleTimeout)
{
    public bool IsConfigured => ControlUrl is not null;

    public static ComputerSandboxOptions From(IConfiguration configuration)
    {
        var idleMinutes = configuration.GetValue("Computer:IdleTimeoutMinutes", 30);
        if (idleMinutes is < 5 or > 24 * 60)
            throw new InvalidOperationException("Computer:IdleTimeoutMinutes must be between 5 and 1440.");
        var token = configuration["Computer:Token"];
        return new ComputerSandboxOptions(ParseUrl(configuration["Computer:ControlUrl"], "Computer:ControlUrl"),
            ParseUrl(configuration["Computer:ViewUrl"], "Computer:ViewUrl"),
            string.IsNullOrWhiteSpace(token) ? null : token.Trim(), TimeSpan.FromMinutes(idleMinutes));
    }

    private static Uri? ParseUrl(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{key} must be an absolute http(s) URL.");
        return uri;
    }
}

public sealed class ComputerSandbox(HttpClient http, ComputerSandboxOptions options) : IComputerSandbox
{
    public bool IsConfigured => options.IsConfigured;

    public TimeSpan IdleTimeout => options.IdleTimeout;

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        if (options.ControlUrl is not { } controlUrl)
            throw new InvalidOperationException("The computer sandbox is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(controlUrl, "/reset"));
        if (options.Token is { } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
