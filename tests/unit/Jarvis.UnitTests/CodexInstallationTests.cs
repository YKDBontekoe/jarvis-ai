using System.Text;
using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CodexInstallationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jarvis-codex-install-").FullName;

    [Fact]
    public void Version_parsing_accepts_cli_output_and_stable_releases_only()
    {
        Assert.Equal("0.145.0", CodexInstallation.ReadVersion("codex-cli 0.145.0\n"));
        Assert.False(CodexInstallation.IsStableRelease("0.158.0-alpha.1"));
        Assert.False(CodexInstallation.IsStableRelease("0.1.0; rm -rf /"));
        Assert.True(CodexInstallation.IsStableRelease("0.167.0"));
    }

    [Fact]
    public void ParseVoices_reads_the_cli_voice_mode_catalog_and_its_default()
    {
        using var document = JsonDocument.Parse("""
            {"voices":{"v1":["juniper","cove","spruce"],"v2":["marin"],"defaultV1":"cove","defaultV2":"marin"}}
            """);

        var voices = CodexInstallation.ParseVoices(document.RootElement);

        Assert.Equal(["juniper", "cove", "spruce"], voices.Voices.Select(voice => voice.Id));
        Assert.Equal("Cove", voices.Voices.Single(voice => voice.IsDefault).Name);
        Assert.Equal("cove", voices.DefaultVoice);
        Assert.DoesNotContain(voices.Voices, voice => voice.Id == "marin");
    }

    [Fact]
    public void ParseModels_reads_the_app_server_catalog()
    {
        using var document = JsonDocument.Parse("""
            [
              {"id":"gpt-5.4-mini","model":"gpt-5.4-mini","displayName":"GPT-5.4 Mini","hidden":false,"isDefault":false,"inputModalities":["text"]},
              {"id":"gpt-5.4","model":"gpt-5.4","displayName":"GPT-5.4","description":"Default","hidden":false,"isDefault":true,"inputModalities":["text","image"],"supportedReasoningEfforts":[{"reasoningEffort":"low","description":"Lower latency"},{"reasoningEffort":"high","description":"More reasoning"}],"defaultReasoningEffort":"medium"},
              {"model":"legacy","hidden":true,"isDefault":false},
              {"displayName":"missing id"}
            ]
            """);

        var models = CodexInstallation.ParseModels(document.RootElement);

        Assert.Equal(["gpt-5.4", "gpt-5.4-mini", "legacy"], models.Select(model => model.Model));
        Assert.True(models[0].IsDefault);
        Assert.True(models[0].SupportsImages);
        Assert.Equal(["low", "high"], models[0].SupportedReasoningEfforts.Select(effort => effort.ReasoningEffort));
        Assert.Equal("medium", models[0].DefaultReasoningEffort);
        Assert.False(models[1].SupportsImages);
        Assert.Equal(["text", "image"], models[2].InputModalities);
        Assert.True(models[2].Hidden);
    }

    [Fact]
    public void Managed_install_replaces_only_the_stock_executable()
    {
        var managed = Path.Combine(_root, "managed");
        var stock = new CodexExecutable(Path.Combine(_root, "codex"), managed);
        Assert.Equal(stock.ConfiguredPath, stock.Resolve());

        Directory.CreateDirectory(Path.Combine(managed, "bin"));
        var installed = Path.Combine(managed, "bin", "codex");
        File.WriteAllText(installed, "#!/bin/sh\n");
        Assert.Equal(installed, stock.Resolve());
        Assert.True(stock.IsManagedInstallActive);

        var custom = new CodexExecutable(Path.Combine(_root, "fake_codex_app_server.mjs"), managed);
        Assert.Equal(custom.ConfiguredPath, custom.Resolve());
        Assert.False(custom.IsManagedInstallActive);
    }

    [Fact]
    public async Task Status_lists_models_from_the_installed_cli_and_caches_them()
    {
        var installation = CreateInstallation();

        var first = await installation.Status.GetStatusAsync(CancellationToken.None);
        var second = await installation.Status.GetStatusAsync(CancellationToken.None);

        Assert.Equal("0.145.0", first.InstalledVersion);
        Assert.Equal(CodexVersion.Pinned, first.PinnedVersion);
        Assert.False(first.MatchesPinnedVersion);
        Assert.Equal(["gpt-5.4", "gpt-5.4-mini", "hidden-model"], first.Models.Select(model => model.Model));
        Assert.Equal(["juniper", "cove", "spruce"], first.Voices.Voices.Select(voice => voice.Id));
        Assert.Equal("cove", first.Voices.DefaultVoice);
        Assert.True(first.Voices.Voices.Single(voice => voice.Id == "cove").IsDefault);
        Assert.Null(first.VoiceError);
        Assert.Equal(1, CountCalls(installation.CliDirectory, "app-server"));
        Assert.Equal(first.InstalledVersion, second.InstalledVersion);
        Assert.Equal(1, CountCalls(installation.CliDirectory, "app-server"));
    }

    [Fact]
    public async Task Version_check_does_not_wait_for_a_cli_that_reads_stdin()
    {
        // A wrapper or fixture that drains stdin used to hang the check until its 15 second timeout, so the
        // installed version was reported unknown and the first Settings load took 15 seconds.
        const string versionBranch = "if \"--version\" in sys.argv:\n";
        Assert.Contains(versionBranch, CurrentCliScript, StringComparison.Ordinal);
        var script = CurrentCliScript.Replace(versionBranch, versionBranch + "    sys.stdin.read()\n",
            StringComparison.Ordinal);
        var installation = CreateInstallation(cliScript: script);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var status = await installation.Status.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.145.0", status.InstalledVersion);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"took {started.Elapsed}");
    }

    [Fact]
    public async Task A_stock_cli_of_another_version_gets_the_pinned_release_installed()
    {
        var installation = CreateInstallation();

        await installation.Status.EnsurePinnedAsync(CancellationToken.None);
        var status = await installation.Status.GetStatusAsync(CancellationToken.None);

        var log = await File.ReadAllTextAsync(Path.Combine(installation.ManagedDirectory, "install.log"));
        Assert.Contains("@openai/codex@" + CodexVersion.Pinned, log, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", log, StringComparison.Ordinal);
        Assert.Equal(CodexVersion.Pinned, status.InstalledVersion);
        Assert.True(status.MatchesPinnedVersion);
        Assert.True(status.UsingManagedInstall);
        Assert.Equal("gpt-5.6-sol", Assert.Single(status.Models).Model);
        Assert.Equal(Path.Combine(installation.ManagedDirectory, "bin", "codex"), installation.Executable.Resolve());
        // Installed beside the managed directory and moved into place, so nothing half-installed is left behind.
        Assert.False(Directory.Exists(installation.ManagedDirectory + ".installing"));
    }

    [Fact]
    public async Task A_stock_cli_at_the_pinned_release_is_used_as_is()
    {
        var installation = CreateInstallation(stockVersion: CodexVersion.Pinned);

        await installation.Status.EnsurePinnedAsync(CancellationToken.None);

        Assert.False(Directory.Exists(installation.ManagedDirectory));
        Assert.Equal(installation.Executable.ConfiguredPath, installation.Executable.Resolve());
    }

    [Fact]
    public async Task A_managed_install_of_another_version_is_removed()
    {
        // An earlier Jarvis let the owner install the latest release; Jarvis now runs the version it pins.
        var installation = CreateInstallation(stockVersion: CodexVersion.Pinned);
        Directory.CreateDirectory(Path.Combine(installation.ManagedDirectory, "bin"));
        WriteExecutable(Path.Combine(installation.ManagedDirectory, "bin", "codex"),
            "#!/bin/sh\necho codex-cli 0.199.0\n");

        await installation.Status.EnsurePinnedAsync(CancellationToken.None);

        Assert.False(Directory.Exists(installation.ManagedDirectory));
        Assert.Equal(installation.Executable.ConfiguredPath, installation.Executable.Resolve());
    }

    [Fact]
    public async Task A_managed_install_at_the_pinned_release_is_kept()
    {
        var installation = CreateInstallation();
        Directory.CreateDirectory(Path.Combine(installation.ManagedDirectory, "bin"));
        var managed = Path.Combine(installation.ManagedDirectory, "bin", "codex");
        WriteExecutable(managed, $"#!/bin/sh\necho codex-cli {CodexVersion.Pinned}\n");

        await installation.Status.EnsurePinnedAsync(CancellationToken.None);

        Assert.Equal(managed, installation.Executable.Resolve());
        Assert.False(File.Exists(Path.Combine(installation.ManagedDirectory, "install.log")));
    }

    [Fact]
    public async Task A_custom_executable_is_left_alone()
    {
        var installation = CreateInstallation(stockName: false);

        var outcome = await installation.Status.EnsurePinnedAsync(CancellationToken.None);

        Assert.Contains("custom", outcome, StringComparison.Ordinal);
        Assert.False(Directory.Exists(installation.ManagedDirectory));
        Assert.Equal(installation.Executable.ConfiguredPath, installation.Executable.Resolve());
    }

    [Fact]
    public void The_container_image_installs_the_pinned_release()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "infra", "compose", "Dockerfile")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Repository root not found.");
        var dockerfile = File.ReadAllText(Path.Combine(root, "infra", "compose", "Dockerfile"));

        Assert.Contains("@openai/codex@" + CodexVersion.Pinned + "\n", dockerfile.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.True(CodexInstallation.IsStableRelease(CodexVersion.Pinned));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private static void WriteExecutable(string path, string contents)
    {
        File.WriteAllText(path, contents);
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    private static int CountCalls(string directory, string argument) =>
        File.ReadAllLines(Path.Combine(directory, "calls.log")).Count(line => line.Contains(argument, StringComparison.Ordinal));

    private Installation CreateInstallation(bool stockName = true, string? cliScript = null,
        string stockVersion = "0.145.0")
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var cliDirectory = Path.Combine(directory, "current");
        Directory.CreateDirectory(cliDirectory);
        var executableName = stockName ? "codex" : "fake_codex_app_server.mjs";
        var executablePath = Path.Combine(cliDirectory, executableName);
        WriteExecutable(executablePath, (cliScript ?? CurrentCliScript).Replace("codex-cli 0.145.0",
            "codex-cli " + stockVersion, StringComparison.Ordinal));

        var npmPath = Path.Combine(directory, "npm");
        WriteExecutable(npmPath, NpmScript);

        var managed = Path.Combine(directory, "managed");
        var executable = new CodexExecutable(executablePath, managed, npmPath);
        var installation = new CodexInstallation(executable, new CodexProcessLimiter(),
            NullLogger<CodexInstallation>.Instance);
        return new Installation(installation, executable, cliDirectory, managed);
    }

    private sealed record Installation(CodexInstallation Status, CodexExecutable Executable, string CliDirectory,
        string ManagedDirectory);

    private const string CurrentCliScript = """
        #!/usr/bin/env python3
        import json, sys
        from pathlib import Path
        log = Path(__file__).with_name("calls.log")
        with log.open("a", encoding="utf-8") as handle:
            handle.write(" ".join(sys.argv[1:]) + "\n")
        if "--version" in sys.argv:
            print("codex-cli 0.145.0", flush=True)
            raise SystemExit(0)
        if "app-server" in sys.argv:
            for line in sys.stdin:
                line = line.strip()
                if not line:
                    continue
                message = json.loads(line)
                method = message.get("method")
                request_id = message.get("id")
                if method == "initialize":
                    print(json.dumps({"id": request_id, "result": {}}), flush=True)
                elif method == "model/list":
                    print(json.dumps({"id": request_id, "result": {"data": [
                        {"id": "gpt-5.4-mini", "model": "gpt-5.4-mini", "displayName": "GPT-5.4 Mini", "description": "Faster", "hidden": False, "isDefault": False, "inputModalities": ["text"]},
                        {"id": "gpt-5.4", "model": "gpt-5.4", "displayName": "GPT-5.4", "description": "Default", "hidden": False, "isDefault": True, "inputModalities": ["text", "image"]},
                        {"id": "hidden-model", "model": "hidden-model", "displayName": "Hidden", "hidden": True, "isDefault": False, "inputModalities": ["text"]}
                    ], "nextCursor": None}}), flush=True)
                elif method == "thread/realtime/listVoices":
                    print(json.dumps({"id": request_id, "result": {"voices": {
                        "v1": ["juniper", "cove", "spruce"], "v2": ["marin"],
                        "defaultV1": "cove", "defaultV2": "marin"
                    }}}), flush=True)
        """;

    private const string NpmScript = """
        #!/usr/bin/env python3
        import sys
        from pathlib import Path
        args = sys.argv[1:]
        prefix = Path(args[args.index("--prefix") + 1])
        package = next(arg for arg in args if arg.startswith("@openai/codex@"))
        version = package.rsplit("@", 1)[1]
        (prefix / "install.log").write_text("\n".join(args), encoding="utf-8")
        binary = prefix / "bin" / "codex"
        binary.parent.mkdir(parents=True, exist_ok=True)
        binary.write_text('''#!/usr/bin/env python3
        import json, sys
        if "--version" in sys.argv:
            print("codex-cli VERSION", flush=True)
            raise SystemExit(0)
        if "app-server" in sys.argv:
            for line in sys.stdin:
                line = line.strip()
                if not line:
                    continue
                message = json.loads(line)
                method = message.get("method")
                request_id = message.get("id")
                if method == "initialize":
                    print(json.dumps({"id": request_id, "result": {}}), flush=True)
                elif method == "model/list":
                    print(json.dumps({"id": request_id, "result": {"data": [
                        {"id": "gpt-5.6-sol", "model": "gpt-5.6-sol", "displayName": "GPT-5.6 Sol", "hidden": False, "isDefault": True, "inputModalities": ["text", "image"]}
                    ], "nextCursor": None}}), flush=True)
                elif method == "thread/realtime/listVoices":
                    print(json.dumps({"id": request_id, "result": {"voices": {
                        "v1": ["cove"], "v2": [], "defaultV1": "cove", "defaultV2": "marin"
                    }}}), flush=True)
        '''.replace("VERSION", version), encoding="utf-8")
        binary.chmod(0o755)
        """;
}
