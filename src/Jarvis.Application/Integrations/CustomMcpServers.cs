using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Integrations;

/// <summary>
/// An MCP server Jarvis wrote for the owner: one Python file run with <c>uv run --script</c> in the MCP runner.
/// The draft is what Jarvis last wrote; the active source is the revision the owner approved, and only that one
/// runs as a live app.
/// </summary>
public sealed record CustomMcpServerSummary(Guid Id, string Name, string? Description, int Revision,
    string DraftSha256, string? ActiveSha256, int? ActiveRevision, string RegistryServerId,
    IReadOnlyList<McpSecretBinding> Secrets, CustomMcpTestResult? LastTest, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool HasUnapprovedChanges => ActiveSha256 != DraftSha256;
}

public sealed record CustomMcpServerDetail(CustomMcpServerSummary Server, string DraftSource, string? ActiveSource,
    IReadOnlyList<CustomMcpRevision> Revisions);

public sealed record CustomMcpRevision(int Revision, string Sha256, DateTimeOffset CreatedAt, bool Active);

/// <summary>What the last test run of a draft found. Diagnostics are redacted of the server's own keys.</summary>
public sealed record CustomMcpTestResult(bool Ok, string Sha256, IReadOnlyList<string> Tools, string? Error,
    string? Diagnostics, DateTimeOffset TestedAt, string? ToolResult = null);

public sealed record CustomMcpSaveRequest(string Name, string? Description, string Source,
    IReadOnlyList<CustomMcpSecretRequest>? Secrets);

/// <summary>An environment variable the server reads a key from, such as WEATHER_API_KEY.</summary>
public sealed record CustomMcpSecretRequest(string Variable, string? Label, string? Description, bool Required = true);

public sealed class CustomMcpScriptException(IReadOnlyList<string> errors)
    : ArgumentException("The server code was not saved: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Activation was refused for a review reason: <see cref="Code"/> is <c>hash_required</c> (no reviewed version was
/// named) or <c>not_tested</c> (no passing test for that exact code and no reason to go ahead anyway).
/// </summary>
public sealed class CustomMcpReviewException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public interface ICustomMcpServerStore
{
    Task<IReadOnlyList<CustomMcpServerSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<CustomMcpServerDetail?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<CustomMcpServerSummary?> FindByNameAsync(Guid ownerId, string name, CancellationToken cancellationToken);
    Task<CustomMcpServerSummary?> FindByRegistryIdAsync(Guid ownerId, string registryServerId,
        CancellationToken cancellationToken);

    /// <summary>Creates a server with its first draft, linked to the registry entry that holds its keys.</summary>
    Task<CustomMcpServerSummary> CreateAsync(Guid ownerId, Guid id, string registryServerId, string name,
        string? description, string source, string sha256, IReadOnlyList<McpSecretBinding> secrets,
        CancellationToken cancellationToken);

    /// <summary>Saves a new draft revision. The approved source keeps running until the draft is activated.</summary>
    Task<CustomMcpServerSummary?> SaveDraftAsync(Guid ownerId, Guid id, string? description, string source,
        string sha256, IReadOnlyList<McpSecretBinding> secrets, CancellationToken cancellationToken);

    /// <summary>Marks the draft as approved, but only if it is still the draft the owner reviewed.</summary>
    Task<CustomMcpServerSummary?> ActivateAsync(Guid ownerId, Guid id, string expectedSha256,
        CancellationToken cancellationToken);

    Task<CustomMcpServerSummary?> RevertDraftAsync(Guid ownerId, Guid id, int revision,
        CancellationToken cancellationToken);

    Task RecordTestAsync(Guid ownerId, Guid id, CustomMcpTestResult result, CancellationToken cancellationToken);

    /// <summary>The approved source and its hash, or null while nothing is approved.</summary>
    Task<(string Source, string Sha256)?> GetActiveSourceAsync(Guid ownerId, Guid id,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

/// <summary>Writes, activates and removes self-built servers, keeping the registry entry in step.</summary>
public static class CustomMcpServerManager
{
    public const int MaxRevisions = 10;
    public const int MaxServersPerOwner = 25;

    public static async Task<CustomMcpServerSummary> SaveAsync(Guid ownerId, CustomMcpSaveRequest request,
        ICustomMcpServerStore store, IUserMcpServerRegistry registry, CancellationToken cancellationToken)
    {
        var name = CustomMcpNames.Validate(request.Name);
        var check = CustomMcpScriptValidator.Validate(request.Source);
        if (!check.Ok) throw new CustomMcpScriptException(check.Errors);
        var secrets = SecretBindings(request.Secrets);
        var description = Clean(request.Description, 300);

        var existing = await store.FindByNameAsync(ownerId, name, cancellationToken);
        if (existing is not null)
        {
            var saved = await store.SaveDraftAsync(ownerId, existing.Id, description ?? existing.Description,
                            check.Source, check.Sha256, secrets, cancellationToken)
                        ?? throw new ArgumentException("That app no longer exists.");
            // New keys can be filled in before the draft goes live, so the registry learns about them now.
            await registry.SetSecretBindingsAsync(ownerId, saved.RegistryServerId, secrets, cancellationToken);
            return saved;
        }

        if ((await store.ListAsync(ownerId, cancellationToken)).Count >= MaxServersPerOwner)
            throw new ArgumentException($"You can keep up to {MaxServersPerOwner} self-built apps. Remove one first.");
        var id = Guid.NewGuid();
        // The registry entry exists from the start (paused) so the owner can add keys before approving the code.
        var server = await registry.AddDefinitionAsync(ownerId, new McpServerDefinition(name, "stdio", null,
            McpStdioCommandValidator.CustomScriptCommand, [id.ToString("N")], [McpToolSelection.All], secrets,
            CustomMcpNames.CatalogName(id)), cancellationToken);
        await registry.SetEnabledAsync(ownerId, server.Id, false, cancellationToken);
        try
        {
            return await store.CreateAsync(ownerId, id, server.Id, name, description, check.Source, check.Sha256,
                secrets, cancellationToken);
        }
        catch
        {
            await registry.RemoveAsync(ownerId, server.Id, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Approves exactly the draft the owner reviewed and turns the app on. The reviewed hash is required (a caller
    /// that omits it is refused rather than activating whatever is current), and the code must have passed a test
    /// unless the owner gives a reason to go ahead without one.
    /// </summary>
    public static async Task<(CustomMcpServerSummary Server, UserMcpServer? Registered, string? OverrideReason)>
        ActivateAsync(Guid ownerId, Guid id, string? expectedSha256, ICustomMcpServerStore store,
            IUserMcpServerRegistry registry, CancellationToken cancellationToken, string? overrideReason = null)
    {
        var detail = await store.GetAsync(ownerId, id, cancellationToken)
                     ?? throw new ArgumentException("That app does not exist.");
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new CustomMcpReviewException("hash_required",
                "Say which version you reviewed: send the sha256 of the draft. Open the app to review its code first.");
        var sha = expectedSha256.Trim();
        var check = CustomMcpScriptValidator.Validate(detail.DraftSource);
        if (!check.Ok) throw new CustomMcpScriptException(check.Errors);

        var tested = detail.Server.LastTest is { Ok: true } test &&
                     string.Equals(test.Sha256, sha, StringComparison.OrdinalIgnoreCase);
        var reason = Clean(overrideReason, MaxOverrideReason);
        if (!tested && (reason is null || reason.Length < MinOverrideReason))
            throw new CustomMcpReviewException("not_tested",
                "This exact code has not passed a test. Test it first, or say why you are switching it on without one.");

        var activated = await store.ActivateAsync(ownerId, id, sha, cancellationToken)
                        ?? throw new ArgumentException("The code changed since you reviewed it. Review the new version first.");
        var registered = await registry.SetEnabledAsync(ownerId, activated.RegistryServerId, true, cancellationToken);
        return (activated, registered, tested ? null : reason);
    }

    public const int MinOverrideReason = 10;
    public const int MaxOverrideReason = 200;

    public static async Task<bool> DeleteAsync(Guid ownerId, Guid id, ICustomMcpServerStore store,
        IUserMcpServerRegistry registry, CancellationToken cancellationToken)
    {
        var detail = await store.GetAsync(ownerId, id, cancellationToken);
        if (detail is null) return false;
        // The registry removes the code with the entry; deleting here covers an entry that is already gone.
        await registry.RemoveAsync(ownerId, detail.Server.RegistryServerId, cancellationToken);
        await store.DeleteAsync(ownerId, id, cancellationToken);
        return true;
    }

    /// <summary>Which revisions to drop so at most <see cref="MaxRevisions"/> remain, never the approved one.</summary>
    public static IReadOnlyList<int> RevisionsToTrim(IReadOnlyList<int> revisions, int? activeRevision)
    {
        var ordered = revisions.OrderByDescending(revision => revision).ToList();
        if (ordered.Count <= MaxRevisions) return [];
        var keep = ordered.Take(MaxRevisions).ToHashSet();
        if (activeRevision is { } active && !keep.Contains(active))
        {
            keep.Remove(ordered[MaxRevisions - 1]);
            keep.Add(active);
        }
        return ordered.Where(revision => !keep.Contains(revision)).ToArray();
    }

    public static IReadOnlyList<McpSecretBinding> SecretBindings(IReadOnlyList<CustomMcpSecretRequest>? secrets) =>
        McpSecretBindings.Normalize((secrets ?? []).Select(secret =>
        {
            var variable = secret.Variable?.Trim() ?? "";
            return new McpSecretBinding(McpSecretBindings.SecretNameFor(variable), McpSecretBindings.EnvironmentTarget,
                variable, null, string.IsNullOrWhiteSpace(secret.Label) ? variable : secret.Label,
                secret.Description, secret.Required);
        }).ToArray(), "stdio");

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }
}

public static partial class CustomMcpNames
{
    public const string CatalogPrefix = "custom:";

    public static string Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!NamePattern().IsMatch(trimmed))
            throw new ArgumentException("Use an app name of 1 to 60 letters, numbers, spaces, underscores, or hyphens.");
        return trimmed;
    }

    public static string CatalogName(Guid id) => CatalogPrefix + id.ToString("N");

    public static Guid? IdFromArguments(IReadOnlyList<string>? arguments) =>
        arguments is [var only] && Guid.TryParseExact(only, "N", out var id) ? id : null;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _-]{0,59}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

public sealed record CustomMcpScriptCheck(bool Ok, IReadOnlyList<string> Errors, string Source, string Sha256,
    IReadOnlyList<string> Dependencies);

/// <summary>
/// The rules a self-built server must follow before Jarvis stores or runs it. The runner checks them again.
/// One Python file with a PEP 723 header whose only keys are requires-python and dependencies, every dependency
/// pinned with ==, and the MCP SDK among them. No custom indexes, sources, URLs or local paths, so the code can
/// only pull the exact PyPI releases the owner sees in the review.
/// </summary>
public static partial class CustomMcpScriptValidator
{
    public const int MaxSourceBytes = 64 * 1024;
    public const int MaxDependencies = 20;

    public static CustomMcpScriptCheck Validate(string? source)
    {
        var errors = new List<string>();
        var text = (source ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (text.Trim().Length == 0) errors.Add("The server code is empty.");
        if (bytes > MaxSourceBytes) errors.Add($"Keep the server under {MaxSourceBytes / 1024} KB.");
        if (text.Contains('\0')) errors.Add("The code contains a NUL character.");

        var dependencies = new List<string>();
        var lines = text.Split('\n');
        var starts = lines.Select((line, index) => (line, index))
            .Where(pair => BlockStart().IsMatch(pair.line)).ToArray();
        if (starts.Length == 0)
            errors.Add("Start the file with a PEP 723 block: '# /// script' ... '# ///'.");
        else if (starts.Length > 1 || starts[0].line.TrimEnd() != "# /// script")
            errors.Add("Use exactly one '# /// script' metadata block and no other '# ///' blocks.");
        else
            ReadMetadata(lines, starts[0].index, errors, dependencies);

        var sha = Sha256(text);
        return new CustomMcpScriptCheck(errors.Count == 0, errors, text, sha, dependencies);
    }

    public static string Sha256(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static void ReadMetadata(string[] lines, int start, List<string> errors, List<string> dependencies)
    {
        var body = new List<string>();
        var closed = false;
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimEnd() == "# ///")
            {
                closed = true;
                break;
            }
            if (line == "#") body.Add(string.Empty);
            else if (line.StartsWith("# ", StringComparison.Ordinal)) body.Add(line[2..]);
            else
            {
                errors.Add("Every line inside the '# /// script' block must start with '# '.");
                return;
            }
        }
        if (!closed)
        {
            errors.Add("Close the metadata block with a '# ///' line.");
            return;
        }

        string? requiresPython = null;
        var inDependencies = false;
        var sawDependencies = false;
        foreach (var raw in body)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            if (inDependencies)
            {
                var closing = line.EndsWith(']');
                var items = closing ? line[..^1] : line;
                foreach (var item in items.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    AddDependency(item, errors, dependencies);
                if (closing) inDependencies = false;
                continue;
            }
            var match = KeyValue().Match(line);
            if (!match.Success)
            {
                errors.Add($"Only requires-python and dependencies may appear in the metadata block (found '{Clip(line)}').");
                continue;
            }
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            switch (key)
            {
                case "requires-python":
                    requiresPython = Unquote(value);
                    if (requiresPython is null || !PythonSpec().IsMatch(requiresPython))
                        errors.Add("Set requires-python to a version range such as \">=3.11\".");
                    break;
                case "dependencies":
                    sawDependencies = true;
                    if (!value.StartsWith('['))
                    {
                        errors.Add("dependencies must be a list.");
                        break;
                    }
                    var rest = value[1..];
                    var closes = rest.EndsWith(']');
                    if (closes) rest = rest[..^1];
                    foreach (var item in rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        AddDependency(item, errors, dependencies);
                    inDependencies = !closes;
                    break;
                default:
                    errors.Add($"Only requires-python and dependencies may appear in the metadata block (found '{key}').");
                    break;
            }
        }
        if (inDependencies) errors.Add("Close the dependencies list with ']'.");
        if (requiresPython is null) errors.Add("Add requires-python, for example requires-python = \">=3.11\".");
        if (!sawDependencies) errors.Add("Add a dependencies list that includes " + CustomMcpTemplate.McpRequirement + ".");
        else if (!dependencies.Any(dependency => dependency.StartsWith("mcp==", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Include the MCP SDK pinned exactly, for example " + CustomMcpTemplate.McpRequirement + ".");
        if (dependencies.Count > MaxDependencies)
            errors.Add($"Use at most {MaxDependencies} dependencies.");
        if (dependencies.GroupBy(Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("List each dependency once.");
    }

    private static void AddDependency(string item, List<string> errors, List<string> dependencies)
    {
        var value = Unquote(item);
        if (value is null || !PinnedRequirement().IsMatch(value))
        {
            errors.Add($"Pin '{Clip(item)}' to one PyPI release with ==, without extras, markers, URLs or paths, for example \"httpx==0.28.1\".");
            return;
        }
        dependencies.Add(value);
    }

    private static string Name(string requirement) => requirement[..requirement.IndexOf("==", StringComparison.Ordinal)]
        .Replace('_', '-').Replace('.', '-');

    private static string StripComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"') inString = !inString;
            else if (line[index] == '#' && !inString) return line[..index];
        }
        return line;
    }

    private static string? Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' && !trimmed[1..^1].Contains('"')
            ? trimmed[1..^1]
            : null;
    }

    private static string Clip(string value) => value.Length <= 60 ? value : value[..60] + "…";

    [GeneratedRegex(@"^#\s*///\s*\S")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_.-]+)\s*=\s*(?<value>.*)$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^[<>=!~]{1,2}\s*\d+(\.\d+){0,2}(\s*,\s*[<>=!~]{1,2}\s*\d+(\.\d+){0,2})*$")]
    private static partial Regex PythonSpec();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}==[0-9][A-Za-z0-9.+!-]{0,40}$")]
    private static partial Regex PinnedRequirement();
}

/// <summary>The starting point Jarvis copies when it writes a server, and the rules it must follow.</summary>
public static partial class CustomMcpTemplate
{
    /// <summary>The MCP Python SDK release self-built servers use. Bump it after checking the release notes.</summary>
    public const string McpVersion = "2.2.0";
    public const string McpRequirement = "mcp==" + McpVersion;

    public const string Source = """"
        # /// script
        # requires-python = ">=3.11"
        # dependencies = [
        #     "mcp==2.2.0",
        #     "httpx==0.28.1",
        # ]
        # ///
        """Weather lookups for Jarvis. Replace the tools with your own."""
        import json
        import os

        import httpx
        from mcp.server.mcpserver import MCPServer
        from mcp.types import ToolAnnotations

        mcp = MCPServer("weather")

        # Keys arrive as environment variables, one per declared secret. Never print them.
        API_KEY = os.environ.get("WEATHER_API_KEY", "")

        # Each run starts with an empty home, so memory and files are gone by the next tool call. A server that
        # logs in keeps its session here; Jarvis saves it encrypted and restores it on the next start. This
        # weather example needs no login, so it never calls save_state.
        STATE_FILE = os.environ.get("JARVIS_STATE_FILE", "")


        def load_state() -> dict:
            """What an earlier run saved. Empty when nothing was saved or the saved format is not understood."""
            try:
                with open(STATE_FILE, encoding="utf-8") as handle:
                    state = json.load(handle)
                return state if isinstance(state, dict) else {}
            except (OSError, ValueError):
                return {}


        def save_state(state: dict) -> None:
            """Keep the session for the next run. Call it as soon as it changes, before the tool returns."""
            if not STATE_FILE:
                return
            temporary = STATE_FILE + ".tmp"
            with open(temporary, "w", encoding="utf-8") as handle:
                json.dump(state, handle)
            os.replace(temporary, STATE_FILE)  # whole file at once; an empty dict or a deleted file logs out


        @mcp.tool(annotations=ToolAnnotations(readOnlyHint=True))
        def weather_current(city: str) -> str:
            """Current weather for a city, as one short line."""
            if not API_KEY:
                return "Add the Weather API key in Jarvis first."
            response = httpx.get(
                "https://api.example.com/v1/current",
                params={"q": city},
                headers={"Authorization": f"Bearer {API_KEY}"},
                timeout=15,
            )
            response.raise_for_status()
            data = response.json()
            return f"{data['temp_c']} C, {data['condition']}"


        if __name__ == "__main__":
            mcp.run("stdio")
        """";

    public const string Guidance = """
        Rules for a self-built MCP server:
        - One Python file. Start with the PEP 723 block exactly as in the template: requires-python and a
          dependencies list only, every dependency pinned with == to one PyPI release, and mcp==2.2.0 included.
          No [tool.uv], indexes, git, URLs or local paths.
        - Use the mcp 2.x API: from mcp.server.mcpserver import MCPServer; @mcp.tool(...); mcp.run("stdio").
          FastMCP no longer exists in mcp 2.x.
        - Name tools snake_case with a short app prefix (weather_current, not current) so they do not clash with
          other apps.
        - Mark tools that only read with annotations=ToolAnnotations(readOnlyHint=True); mark tools that delete or
          send with destructiveHint=True. Every call still goes through Jarvis's approval rules.
        - Read keys from environment variables and declare each one in secrets (variable, label, description).
          Never hard-code, log or return a key. Return a helpful message when a key is missing.
        - Write to stdout only through MCP. Print diagnostics to stderr if needed; Jarvis shows the last lines,
          with key values removed, when a test fails.
        - Keep network calls bounded with timeouts. The server runs in an isolated sandbox with no access to Jarvis,
          its database or other apps, and a fresh temporary home on each start. Module variables and files in the
          home are gone after each run, so a login done in one tool call is lost for the next.
        - Anything that must outlive one tool call (a login, tokens, a device list) goes in the file named by the
          JARVIS_STATE_FILE environment variable, using load_state and save_state from the template. Load it when a
          tool runs, and save it right after the session changes and before the tool returns, so the change is kept
          even if the run ends straight afterwards. It must be one JSON object of at most 16,000 characters; the
          file is replaced atomically (save_state does this), because a half-written file is ignored. Jarvis keeps
          it encrypted and restores it on the next start. Treat missing or unfamiliar content as "not logged in"
          (an older revision may have written it). Never return its contents from a tool or print it.
        """;

    /// <summary>The server's saved-state variable. Code that mentions it is assumed to keep its session.</summary>
    public const string StateVariable = "JARVIS_STATE_FILE";

    [System.Text.RegularExpressions.GeneratedRegex(
        @"log_?in|sign_?in|authenticat|verification|otp\b|session|refresh_token|access_token|password",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SessionTerms();

    /// <summary>
    /// A warning for code that looks like it logs in but never uses the state file, so its login is lost after
    /// every tool call. Null when the code keeps its state or has nothing to keep.
    /// </summary>
    public static string? StateAdvice(string source) =>
        source.Contains(StateVariable, StringComparison.Ordinal) || !SessionTerms().IsMatch(source)
            ? null
            : "This code looks like it logs in or keeps a session, but it never uses " + StateVariable + ". " +
              "Each run starts with an empty home and ends after the turn, so anything held in memory or in files " +
              "is gone by the next tool call and the user would have to log in again every time. Keep the session " +
              "with load_state and save_state from GetCustomMcpTemplate, save it right after login, and treat " +
              "missing state as not logged in.";
}

/// <summary>How much an app was used recently, read from turn traces (tool names only, no arguments).</summary>
public sealed record McpAppUsage(int CallsLast7Days, DateTimeOffset? LastUsedAt);

public static class McpAppUsageSummary
{
    public static McpAppUsage For(IEnumerable<(DateTimeOffset At, IEnumerable<string> Tools)> turns,
        IReadOnlyCollection<string> appTools)
    {
        if (appTools.Count == 0) return new McpAppUsage(0, null);
        var names = appTools as IReadOnlySet<string> ?? appTools.ToHashSet(StringComparer.Ordinal);
        var calls = 0;
        DateTimeOffset? last = null;
        foreach (var (at, tools) in turns)
        {
            var used = tools.Count(names.Contains);
            if (used == 0) continue;
            calls += used;
            if (last is null || at > last) last = at;
        }
        return new McpAppUsage(calls, last);
    }
}
