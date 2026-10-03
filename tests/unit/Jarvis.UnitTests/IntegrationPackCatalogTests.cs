using System.Text.RegularExpressions;
using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed partial class IntegrationPackCatalogTests
{
    [Fact]
    public void Suggested_npm_packages_are_pinned_to_an_exact_version()
    {
        var packages = IntegrationPackCatalog.All
            .Where(pack => pack.SuggestedCommand == "npx")
            .SelectMany(pack => pack.SuggestedArguments ?? [])
            .Where(argument => !argument.StartsWith('-'))
            .ToArray();

        Assert.NotEmpty(packages);
        Assert.All(packages, package => Assert.Matches(PinnedPackage(), package));
    }

    [GeneratedRegex(@"^(@[a-z0-9-]+/)?[a-z0-9.-]+@\d+\.\d+\.\d+$")]
    private static partial Regex PinnedPackage();
}
