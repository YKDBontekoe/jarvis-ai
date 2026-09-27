using System.Net;
using System.Net.Http;
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
        Assert.True(CodexInstallation.IsNewerRelease("0.157.0", "0.145.0"));
        Assert.False(CodexInstallation.IsNewerRelease("0.145.0", "0.145.0"));
        Assert.True(CodexInstallation.IsNewerRelease("0.157.0", null));
        Assert.False(CodexInstallation.IsStableRelease("0.158.0-alpha.1"));
        Assert.False(CodexInstallation.IsStableRelease("0.1.0; rm -rf /"));
        Assert.False(CodexInstallation.IsNewerRelease("latest", "0.1.0"));
    }

    [Fact]
    public void ParseModels_reads_the_app_server_catalog()
    {
        using var document = JsonDocument.Parse("""
            [
              {"id":"gpt-5.4-mini","model":"gpt-5.4-mini","displayName":"GPT-5.4 Mini","hidden":false,"isDefault":false,"inputModalities":["text"]},
              {"id":"gpt-5.4","model":"gpt-5.4","displayName":"GPT-5.4","description":"Default","hidden":false,"isDefault":true,"inputModalities":["text","image"]},
              {"model":"legacy","hidden":true,"isDefault":false},
              {"displayName":"missing id"}
            ]
            """);

        var models = CodexInstallation.ParseModels(document.RootElement);

        Assert.Equal(["gpt-5.4", "gpt-5.4-mini", "legacy"], models.Select(model => model.Model));
        Assert.True(models[0].IsDefault);
        Assert.True(models[0].SupportsImages);
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
        var installation = CreateInstallation("""{"version":"0.157.0"}""");

        var first = await installation.Status.GetStatusAsync(CancellationToken.None);
        var second = await installation.Status.GetStatusAsync(CancellationToken.None);

        Assert.Equal("0.145.0", first.InstalledVersion);
        Assert.Equal("0.157.0", first.LatestVersion);
        Assert.True(first.UpdateAvailable);
        Assert.True(first.CanUpdate);
        Assert.Equal(["gpt-5.4", "gpt-5.4-mini", "hidden-model"], first.Models.Select(model => model.Model));
        Assert.Equal(1, CountCalls(installation.CliDirectory, "app-server"));
        Assert.Equal(first.InstalledVersion, second.InstalledVersion);
        Assert.Equal(1, CountCalls(installation.CliDirectory, "app-server"));
    }

    [Fact]
    public async Task Update_installs_the_registry_release_and_loads_its_models()
    {
        var installation = CreateInstallation("""{"version":"0.157.0"}""");

        var updated = await installation.Status.UpdateAsync(CancellationToken.None);

        var log = await File.ReadAllTextAsync(Path.Combine(installation.ManagedDirectory, "install.log"));
        Assert.Contains("@openai/codex@0.157.0", log, StringComparison.Ordinal);
        Assert.Contains("--prefix", log, StringComparison.Ordinal);
        Assert.Contains(installation.ManagedDirectory, log, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", log, StringComparison.Ordinal);
        Assert.Equal("0.157.0", updated.InstalledVersion);
        Assert.False(updated.UpdateAvailable);
        Assert.True(updated.UsingManagedInstall);
        Assert.Equal("gpt-5.6-sol", Assert.Single(updated.Models).Model);
        Assert.Equal(Path.Combine(installation.ManagedDirectory, "bin", "codex"), installation.Executable.Resolve());
    }

    [Fact]
    public async Task Update_does_nothing_when_the_installed_release_is_current()
    {
        var installation = CreateInstallation("""{"version":"0.145.0"}""");

        var status = await installation.Status.UpdateAsync(CancellationToken.None);

        Assert.Equal("0.145.0", status.InstalledVersion);
        Assert.False(status.UpdateAvailable);
        Assert.False(status.UsingManagedInstall);
        Assert.False(File.Exists(Path.Combine(installation.ManagedDirectory, "install.log")));
    }

    [Fact]
    public async Task Update_refuses_an_unrecognized_registry_version()
    {
        var installation = CreateInstallation("""{"version":"0.1.0;touch /tmp/pwned"}""");

        var status = await installation.Status.GetStatusAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installation.Status.UpdateAsync(CancellationToken.None));

        Assert.Null(status.LatestVersion);
        Assert.False(status.UpdateAvailable);
        Assert.Contains("stable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(installation.ManagedDirectory, "install.log")));
    }

    [Fact]
    public async Task Update_leaves_a_custom_executable_in_place()
    {
        var installation = CreateInstallation("""{"version":"0.157.0"}""", stockName: false);

        var status = await installation.Status.GetStatusAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installation.Status.UpdateAsync(CancellationToken.None));

        Assert.False(status.CanUpdate);
        Assert.Contains("custom Codex executable", error.Message, StringComparison.Ordinal);
        Assert.Equal(installation.Executable.ConfiguredPath, installation.Executable.Resolve());
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

    private Installation CreateInstallation(string registryBody, bool stockName = true)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var cliDirectory = Path.Combine(directory, "current");
        Directory.CreateDirectory(cliDirectory);
        var executableName = stockName ? "codex" : "fake_codex_app_server.mjs";
        var executablePath = Path.Combine(cliDirectory, executableName);
        WriteExecutable(executablePath, CurrentCliScript);

        var npmPath = Path.Combine(directory, "npm");
        WriteExecutable(npmPath, NpmScript);

        var managed = Path.Combine(directory, "managed");
        var executable = new CodexExecutable(executablePath, managed, npmPath);
        var handler = new RegistryHandler(registryBody);
        var http = new HttpClient(handler);
        var installation = new CodexInstallation(new ClientFactory(http), executable, new CodexProcessLimiter(),
            NullLogger<CodexInstallation>.Instance);
        return new Installation(installation, executable, cliDirectory, managed);
    }

    private sealed record Installation(CodexInstallation Status, CodexExecutable Executable, string CliDirectory,
        string ManagedDirectory);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RegistryHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("@openai/codex/latest", Uri.UnescapeDataString(request.RequestUri!.ToString()),
                StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

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
        '''.replace("VERSION", version), encoding="utf-8")
        binary.chmod(0o755)
        """;
}
