using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Integrations;

/// <summary>A server from the MCP registry that Jarvis can install, with the ways it can run.</summary>
public sealed record McpCatalogEntry(string Name, string Title, string? Description, string Version,
    string? WebsiteUrl, string? RepositoryUrl, IReadOnlyList<McpCatalogInstallOption> Options);

/// <summary>
/// One way to run a catalog server. <see cref="Kind"/> is remote (Jarvis connects to the publisher's HTTPS
/// endpoint), npm, or pypi (Jarvis downloads the pinned package and runs it on this server).
/// </summary>
public sealed record McpCatalogInstallOption(string Id, string Kind, string Summary,
    IReadOnlyList<McpCatalogSecret> Secrets)
{
    [JsonIgnore]
    public McpServerDefinition Definition { get; init; } = null!;
}

public sealed record McpCatalogSecret(string Name, string Label, string? Description, bool Required);

public sealed record McpCatalogPage(IReadOnlyList<McpCatalogEntry> Servers, string? NextCursor);

public sealed class McpCatalogUnavailableException(string message) : Exception(message);

/// <summary>Searches an MCP server registry. Results are third-party data.</summary>
public interface IMcpCatalog
{
    Task<McpCatalogPage> SearchAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken);
    Task<McpCatalogEntry?> GetAsync(string name, CancellationToken cancellationToken);
}

public sealed record McpCatalogInstallRequest(string Name, string? Option);

/// <summary>
/// The installed server and what the owner does next: ready, secrets (fill in the listed secrets), or sign_in
/// (the server uses OAuth).
/// </summary>
public sealed record McpCatalogInstallResult(UserMcpServer Server, string NextStep);

public static class McpCatalogInstaller
{
    /// <summary>
    /// Installs a catalog server for the owner. The entry is fetched again from the registry by name, so a
    /// client cannot substitute its own endpoint or package.
    /// </summary>
    public static async Task<UserMcpServer> InstallAsync(Guid ownerId, McpCatalogInstallRequest request,
        IMcpCatalog catalog, IUserMcpServerRegistry servers, CancellationToken cancellationToken)
    {
        var entry = await catalog.GetAsync(request.Name?.Trim() ?? "", cancellationToken)
                    ?? throw new ArgumentException("That app is not in the catalog, or Jarvis cannot run it.");
        var option = string.IsNullOrWhiteSpace(request.Option)
            ? entry.Options[0]
            : entry.Options.FirstOrDefault(item => item.Id == request.Option)
              ?? throw new ArgumentException("That install option is not available for this app.");
        return await servers.AddDefinitionAsync(ownerId, option.Definition, cancellationToken);
    }

    public static string NextStep(UserMcpServer server, bool usesOAuth) =>
        server.Secrets?.Any(secret => secret.Required && !secret.IsSet) == true ? "secrets"
        : usesOAuth ? "sign_in"
        : "ready";
}

/// <summary>Maps official MCP registry server.json documents to installable catalog entries.</summary>
public static partial class McpRegistryMapper
{
    public static McpCatalogEntry? Map(JsonElement server)
    {
        var name = String(server, "name");
        var version = String(server, "version");
        if (name is null || version is null || name.Length > 200) return null;
        var title = DisplayName(String(server, "title"), name);
        var options = new List<McpCatalogInstallOption>();

        if (server.TryGetProperty("remotes", out var remotes) && remotes.ValueKind == JsonValueKind.Array)
        {
            foreach (var remote in remotes.EnumerateArray())
                if (MapRemote(remote, title, name, options.Count) is { } option) options.Add(option);
        }
        if (server.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
        {
            foreach (var package in packages.EnumerateArray())
                if (MapPackage(package, title, name, options.Count) is { } option) options.Add(option);
        }
        if (options.Count == 0) return null;

        string? repository = null;
        if (server.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object)
            repository = HttpsUrl(String(repo, "url"));
        return new McpCatalogEntry(name, title, Trim(String(server, "description"), 400), version,
            HttpsUrl(String(server, "websiteUrl")), repository, options);
    }

    private static McpCatalogInstallOption? MapRemote(JsonElement remote, string title, string catalogName, int index)
    {
        if (String(remote, "type") != "streamable-http") return null;
        var url = HttpsUrl(String(remote, "url"));
        if (url is null || url.Contains('{')) return null;
        var bindings = new List<McpSecretBinding>();
        if (remote.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headers.EnumerateArray())
            {
                var headerName = String(header, "name");
                if (headerName is null || !McpSecretBindings.IsAllowedHeaderName(headerName)) return null;
                var template = String(header, "value");
                string? prefix = null;
                if (template is not null)
                {
                    var match = HeaderTemplate().Match(template);
                    // A fixed header value with no placeholder is configuration Jarvis does not send.
                    if (!match.Success) return null;
                    prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : null;
                }
                bindings.Add(new McpSecretBinding(McpSecretBindings.SecretNameFor(headerName),
                    McpSecretBindings.HeaderTarget, headerName, prefix, headerName,
                    Trim(String(header, "description"), 300), Bool(header, "isRequired")));
            }
        }
        return Option($"remote-{index}", "remote", url, bindings,
            new McpServerDefinition(ServerName(title), "streamableHttp", url, null, null, [McpToolSelection.All],
                bindings, catalogName));
    }

    private static McpCatalogInstallOption? MapPackage(JsonElement package, string title, string catalogName,
        int index)
    {
        var registryType = String(package, "registryType");
        var identifier = String(package, "identifier");
        var version = String(package, "version");
        if (registryType is not ("npm" or "pypi") || identifier is null || version is null) return null;
        if (package.TryGetProperty("transport", out var transport) && String(transport, "type") is { } kind &&
            kind != "stdio") return null;
        // Required command-line arguments need a setup step this flow does not offer.
        if (HasRequiredArgument(package, "packageArguments") || HasRequiredArgument(package, "runtimeArguments"))
            return null;

        var spec = $"{identifier}@{version}";
        var (command, arguments) = registryType == "npm" ? ("npx", new[] { "-y", spec }) : ("uvx", new[] { spec });
        try { McpStdioCommandValidator.Normalize(command, arguments); }
        catch (ArgumentException) { return null; }

        var bindings = new List<McpSecretBinding>();
        if (package.TryGetProperty("environmentVariables", out var variables) &&
            variables.ValueKind == JsonValueKind.Array)
        {
            foreach (var variable in variables.EnumerateArray())
            {
                var variableName = String(variable, "name");
                if (variableName is null || !McpSecretBindings.IsAllowedEnvironmentName(variableName)) return null;
                var required = Bool(variable, "isRequired") && String(variable, "default") is null;
                bindings.Add(new McpSecretBinding(McpSecretBindings.SecretNameFor(variableName),
                    McpSecretBindings.EnvironmentTarget, variableName, null, variableName,
                    Trim(String(variable, "description"), 300), required));
            }
        }
        return Option($"package-{index}", registryType, spec, bindings,
            new McpServerDefinition(ServerName(title), "stdio", null, command, arguments, [McpToolSelection.All],
                bindings, catalogName));
    }

    private static McpCatalogInstallOption? Option(string id, string kind, string summary,
        List<McpSecretBinding> bindings, McpServerDefinition definition)
    {
        try { McpSecretBindings.Normalize(bindings, definition.Transport); }
        catch (ArgumentException) { return null; }
        return new McpCatalogInstallOption(id, kind, summary,
            bindings.Select(binding => new McpCatalogSecret(binding.SecretName, binding.Label, binding.Description,
                binding.Required)).ToArray())
        {
            Definition = definition
        };
    }

    private static bool HasRequiredArgument(JsonElement package, string property) =>
        package.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array &&
        list.EnumerateArray().Any(argument => Bool(argument, "isRequired") && String(argument, "value") is null &&
                                              String(argument, "default") is null);

    /// <summary>A readable name such as "Brave Search" from io.github.brave/brave-search-mcp-server.</summary>
    public static string DisplayName(string? title, string name)
    {
        if (!string.IsNullOrWhiteSpace(title)) return Trim(title, 80)!;
        var last = McpSuffix().Replace(name[(name.LastIndexOf('/') + 1)..], "");
        // A server named only "mcp" (com.notion/mcp) is the publisher's own: use the publisher's name.
        if (last.Length == 0) last = Publisher(name) ?? "";
        var words = Separators().Split(last).Where(word => word.Length > 0)
            .Select(word => Acronyms.Contains(word) ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..]);
        var display = string.Join(' ', words);
        return display.Length == 0 ? "MCP server" : Trim(display, 80)!;
    }

    /// <summary>The last label of the namespace: "notion" for com.notion/mcp, "brave" for io.github.brave/x.</summary>
    public static string? Publisher(string name)
    {
        var slash = name.IndexOf('/');
        if (slash <= 0) return null;
        var label = name[..slash].Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(label) ? null : label;
    }

    /// <summary>
    /// Orders search results so a publisher's own server comes before third-party wrappers: an exact
    /// publisher match first, then titles that start with the search, keeping registry order otherwise.
    /// </summary>
    public static IReadOnlyList<McpCatalogEntry> Rank(IEnumerable<McpCatalogEntry> entries, string? query)
    {
        var term = query?.Trim() ?? "";
        if (term.Length == 0) return entries.ToArray();
        return entries.Select((entry, index) => (entry, index, score: Score(entry, term)))
            .OrderByDescending(item => item.score).ThenBy(item => item.index)
            .Select(item => item.entry).ToArray();
    }

    private static int Score(McpCatalogEntry entry, string term)
    {
        var publisher = Publisher(entry.Name);
        var score = 0;
        if (publisher is not null && term.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(word => publisher.Equals(word, StringComparison.OrdinalIgnoreCase))) score += 2;
        if (entry.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 1;
        return score;
    }

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
        { "api", "ai", "sql", "aws", "gcp", "mcp", "ui", "pdf", "url", "dns", "ssh", "sdk" };

    private static string ServerName(string title)
    {
        var cleaned = NameCharacters().Replace(title, " ").Trim();
        cleaned = Spaces().Replace(cleaned, " ");
        if (cleaned.Length == 0 || !char.IsAsciiLetterOrDigit(cleaned[0])) cleaned = "App " + cleaned;
        return cleaned.Length > 80 ? cleaned[..80].TrimEnd() : cleaned;
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static bool Bool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static string? HttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) && value!.Length <= 500
            ? value
            : null;

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"^(?:(?<prefix>[A-Za-z][A-Za-z0-9-]{0,31}) )?\{[A-Za-z0-9_.-]{1,64}\}$")]
    private static partial Regex HeaderTemplate();

    [GeneratedRegex(@"[-_.]?(mcp[-_.]?server|server[-_.]?mcp|mcp)$", RegexOptions.IgnoreCase)]
    private static partial Regex McpSuffix();

    [GeneratedRegex(@"[-_. ]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"[^A-Za-z0-9 _-]+")]
    private static partial Regex NameCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
