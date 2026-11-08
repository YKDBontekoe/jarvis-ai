using System.Text.RegularExpressions;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Guards the rule in AGENTS.md that sensitive agent actions need the owner's approval. The tools below delete data,
/// send messages to other people or change what is remembered about the owner; each must stay wrapped in
/// <c>ApprovalRequiredAIFunction</c> at the place it is registered.
/// </summary>
public sealed partial class ApprovalRegistrationGuardTests
{
    private static readonly string[] MustAskFirst =
    [
        "DeleteExpense", "DeleteGoal", "DeleteHabit", "DeleteProject", "DeleteFile", "RemovePerson", "ForgetMemory",
        "ForgetGraphEntity", "SendWhatsAppMessage", "RemoveMcpServer", "RunMission", "RunAutomation", "InvokeMcpTool",
    ];

    [Fact]
    public void Destructive_and_outward_facing_tools_are_registered_behind_an_approval()
    {
        var root = RepositoryRoot();
        var source = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "src", "Jarvis.Agents"), "*.cs",
            SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

        var wrapped = Wrapped().Matches(source).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var plain = Plain().Matches(source).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        Assert.All(MustAskFirst, name => Assert.True(wrapped.Contains(name), $"{name} is not wrapped in ApprovalRequiredAIFunction"));
        Assert.All(MustAskFirst, name => Assert.False(plain.Contains(name), $"{name} is also registered without an approval"));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Could not find the repository root.");
    }

    [GeneratedRegex(@"ApprovalRequiredAIFunction\(\s*AIFunctionFactory\.Create\(\s*[\w.]+\.(?<name>\w+?)(Async)?[,)]")]
    private static partial Regex Wrapped();

    [GeneratedRegex(@"(?<!ApprovalRequiredAIFunction\(\s*)AIFunctionFactory\.Create\(\s*[\w.]+\.(?<name>\w+?)(Async)?[,)]")]
    private static partial Regex Plain();
}
