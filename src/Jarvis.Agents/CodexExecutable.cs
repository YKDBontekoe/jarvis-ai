using Microsoft.Extensions.Configuration;

namespace Jarvis.Agents;

/// <summary>
/// Resolves the Codex CLI. A Settings update installs into <see cref="ManagedDirectory"/> and is preferred
/// when Jarvis is still pointed at the stock <c>codex</c> executable.
/// </summary>
public sealed class CodexExecutable
{
    public CodexExecutable(string configuredPath, string managedDirectory, string npmPath = "npm")
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("A Codex executable is required.", nameof(configuredPath));
        ConfiguredPath = configuredPath.Trim();
        ManagedDirectory = Path.GetFullPath(managedDirectory);
        NpmPath = string.IsNullOrWhiteSpace(npmPath) ? "npm" : npmPath.Trim();
    }

    public string ConfiguredPath { get; }

    public string ManagedDirectory { get; }

    public string NpmPath { get; }

    public bool UsesStockCli
    {
        get
        {
            var name = Path.GetFileName(ConfiguredPath);
            return name is "codex" or "codex.exe" or "codex.cmd";
        }
    }

    public bool IsManagedInstallActive => UsesStockCli && FindManagedExecutable() is not null;

    public static CodexExecutable From(IConfiguration configuration)
    {
        var configured = configuration["Codex:ExecutablePath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = "codex";
        var managed = configuration["Codex:ManagedInstallDirectory"];
        if (string.IsNullOrWhiteSpace(managed)) managed = DefaultManagedDirectory();
        var npm = configuration["Codex:NpmExecutablePath"];
        return new CodexExecutable(configured, managed, string.IsNullOrWhiteSpace(npm) ? "npm" : npm);
    }

    public static string DefaultManagedDirectory()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            var userHome = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(userHome))
                userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            home = Path.Combine(userHome, ".codex");
        }
        return Path.Combine(home, "cli");
    }

    public string Resolve() => FindManagedExecutable() is { } managed && UsesStockCli ? managed : ConfiguredPath;

    public string? ResolveNpm()
    {
        if (Path.IsPathRooted(NpmPath) || NpmPath.Contains(Path.DirectorySeparatorChar))
            return File.Exists(NpmPath) ? NpmPath : null;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, NpmPath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public string? FindManagedExecutable()
    {
        foreach (var relative in new[] { Path.Combine("bin", "codex"), Path.Combine("node_modules", ".bin", "codex") })
        {
            var path = Path.Combine(ManagedDirectory, relative);
            if (!File.Exists(path)) continue;
            EnsureExecutable(path);
            return path;
        }
        return null;
    }

    public bool CanWriteManagedDirectory()
    {
        try
        {
            Directory.CreateDirectory(ManagedDirectory);
            var probe = Path.Combine(ManagedDirectory, ".jarvis-write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            var desired = mode | UnixFileMode.UserRead | UnixFileMode.UserExecute;
            if (mode != desired) File.SetUnixFileMode(path, desired);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The bundled executable still runs when the managed copy cannot be marked executable.
        }
    }
}
