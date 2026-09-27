using System.Text.Json;
using Jarvis.Application.Agents;
using Jarvis.Application.Browser;
using Jarvis.Application.Devices;
using Jarvis.Application.Surfaces;
using Jarvis.Agents.Networking;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PlatformExpansionTests
{
    [Fact]
    public void Ui_schema_normalizes_a_choice_card()
    {
        using var items = JsonDocument.Parse("""[{"id":"a","title":"Train"},{"id":"b","title":"Plane"}]""");
        using var actions = JsonDocument.Parse("""[{"id":"a","label":"Train","style":"primary"}]""");

        var json = UiSurfaceSchema.Normalize("CHOICE", " Travel ", "Pick one", items.RootElement, null,
            actions.RootElement);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("choice", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Travel", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("primary", document.RootElement.GetProperty("actions")[0].GetProperty("style").GetString());
    }

    [Fact]
    public void Ui_schema_rejects_unknown_kinds()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            UiSurfaceSchema.Normalize("popup", "Hi", null, null, null, null));
        Assert.Contains("card", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remote_agent_urls_must_be_https_except_localhost()
    {
        var (name, url) = RemoteAgentValidation.Normalize("Travel", "https://assistant.example/a2a");
        Assert.Equal("Travel", name);
        Assert.Equal("https://assistant.example/a2a", url);

        Assert.Throws<ArgumentException>(() =>
            RemoteAgentValidation.Normalize("Bad", "http://assistant.example/a2a"));
        var local = RemoteAgentValidation.Normalize("Local", "http://localhost:5082/a2a");
        Assert.Contains("localhost", local.Url, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => RemoteAgentValidation.Normalize("Creds", "https://user:pass@x/a2a"));
    }

    [Fact]
    public void Browser_urls_reject_private_hosts()
    {
        Assert.Equal("https://example.com/path", BrowserUrls.Normalize("https://example.com/path"));
        Assert.Null(BrowserUrls.Normalize(" "));
        Assert.Throws<ArgumentException>(() => BrowserUrls.Normalize("http://localhost/admin"));
        Assert.Throws<ArgumentException>(() => BrowserUrls.Normalize("https://user:pass@example.com"));
    }

    [Fact]
    public void A2A_replies_prefer_text_parts()
    {
        var described = RemoteAgentTools.Describe("Travel",
            """{"jsonrpc":"2.0","result":{"parts":[{"kind":"text","text":"Pack light."}]}}""");
        Assert.Contains("Pack light.", described);
        Assert.StartsWith("Travel replied", described);

        var waiting = RemoteAgentTools.Describe("Travel",
            """{"jsonrpc":"2.0","result":{"status":{"state":"input-required"}}}""");
        Assert.Contains("input-required", waiting);

        var error = RemoteAgentTools.Describe("Travel",
            """{"jsonrpc":"2.0","error":{"message":"unauthorized"}}""");
        Assert.Contains("unauthorized", error);
    }

    [Fact]
    public void Device_settings_gate_capabilities()
    {
        var locked = DeviceSettings.Default;
        Assert.True(locked.Allows(DeviceCapabilities.Battery));
        Assert.True(locked.Allows(DeviceCapabilities.OpenUrl));
        Assert.False(locked.Allows(DeviceCapabilities.Location));
        Assert.False(locked.Allows(DeviceCapabilities.Clipboard));
        Assert.False(locked.Allows("camera"));

        var open = locked with { Location = true, Clipboard = true };
        Assert.True(open.Allows(DeviceCapabilities.Location));
        Assert.True(open.Allows(DeviceCapabilities.Clipboard));
    }

    [Fact]
    public void A2A_token_names_are_strict()
    {
        Assert.Equal("Home lab", RemoteAgentValidation.NormalizeTokenName(" Home lab "));
        Assert.Throws<ArgumentException>(() => RemoteAgentValidation.NormalizeTokenName(""));
        Assert.Throws<ArgumentException>(() => RemoteAgentValidation.NormalizeTokenName("no/slash"));
    }

    [Fact]
    public void Browser_urls_reject_mdns_hosts()
    {
        var error = Assert.Throws<ArgumentException>(() => BrowserUrls.Normalize("https://printer.local/status"));
        Assert.Contains("local", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
