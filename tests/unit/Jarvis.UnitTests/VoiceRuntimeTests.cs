using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Agents;
using Jarvis.Api.Realtime;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class VoiceSpeechRuntimeTests
{
    [Fact]
    public void Voice_prompt_answers_in_realtime_with_jarvis_tools()
    {
        Assert.Contains("Answer the user directly", VoiceSpeech.Prompt);
        Assert.Contains("Jarvis tools", VoiceSpeech.Prompt);
        Assert.Contains("live web search", VoiceSpeech.Prompt);
        Assert.DoesNotContain("Do not answer from your own knowledge", VoiceSpeech.Prompt);
    }

    [Fact]
    public void Blank_voice_lets_the_installed_cli_choose()
    {
        Assert.Null(VoiceSpeech.ResolveVoice(null, null));
        Assert.Null(VoiceSpeech.ResolveVoice("  ", "not a voice"));
        Assert.Equal("cove", VoiceSpeech.ResolveVoice("Cove", null));
        Assert.Equal("spruce", VoiceSpeech.ResolveVoice(null, "Spruce"));
        Assert.Equal("arbor", VoiceSpeech.ResolveVoice("arbor", "juniper"));
    }

    [Fact]
    public void Playback_echo_matches_a_span_of_the_spoken_text()
    {
        const string spoken = "I finished the calendar check for tomorrow morning.";
        Assert.True(VoiceSpeech.IsEcho("finished the calendar check for tomorrow", spoken));
        Assert.False(VoiceSpeech.IsEcho("stop please", spoken));
        Assert.False(VoiceSpeech.IsEcho("hi", spoken));
    }

    [Fact]
    public void Barge_in_waits_and_ignores_echo()
    {
        var barge = new VoiceBargeIn(0.45);
        const string spoken = "I finished the calendar check for tomorrow morning.";
        Assert.False(barge.Consider("finished the calendar check", spoken, true, 0));
        Assert.False(barge.Consider("What about the other meeting", spoken, true, 1));
        Assert.True(barge.Consider("What about the other meeting tomorrow", spoken, true, 1.5));
    }

    [Fact]
    public void Barge_in_does_not_arm_while_idle()
    {
        var barge = new VoiceBargeIn(0.45);
        Assert.False(barge.Consider("What about the other meeting", "", false, 2));
    }
}

public sealed class VoicePlaybackGateRuntimeTests
{
    [Fact]
    public void Realtime_audio_opens_for_the_live_session()
    {
        var gate = new VoicePlaybackGate();
        Assert.Null(gate.AcceptFrame());
        Assert.True(gate.AllowRealtimeOutput());
        Assert.Equal(0, gate.AcceptFrame());
    }

    [Fact]
    public void Realtime_audio_stays_suppressed_after_barge_in_until_next_turn()
    {
        var gate = new VoicePlaybackGate();
        gate.AllowRealtimeOutput();
        gate.Duck(true);
        Assert.False(gate.AllowRealtimeOutput());
        Assert.Null(gate.AcceptFrame());

        gate.BeginTurn();
        Assert.True(gate.AllowRealtimeOutput());
        Assert.Equal(1, gate.AcceptFrame());
    }

    [Fact]
    public void Barge_in_drops_late_frames_until_next_speak()
    {
        var gate = new VoicePlaybackGate();
        Assert.True(gate.TryStartSpeak());
        Assert.Equal(0, gate.AcceptFrame());

        gate.Duck(true);
        Assert.Null(gate.AcceptFrame());
        Assert.False(gate.TryStartSpeak());
        Assert.Null(gate.AcceptFrame());

        gate.BeginTurn();
        Assert.True(gate.TryStartSpeak());
        Assert.Equal(1, gate.AcceptFrame());
    }

    [Fact]
    public void In_flight_speak_cannot_undo_barge_in()
    {
        var gate = new VoicePlaybackGate();
        Assert.True(gate.TryStartSpeak());
        gate.Duck(true);
        gate.FinishSpeak();
        Assert.False(gate.OutputAllowed);
        Assert.False(gate.TryStartSpeak());
    }

    [Fact]
    public void Queued_generation_is_invalid_after_duck()
    {
        var gate = new VoicePlaybackGate();
        Assert.True(gate.TryStartSpeak());
        var stamped = gate.AcceptFrame();
        Assert.NotNull(stamped);
        gate.Duck(true);
        Assert.NotEqual(stamped, gate.Generation);
        Assert.Null(gate.AcceptFrame());
    }
}

public sealed class VoicePcmTests
{
    [Fact]
    public void Opus_round_trip_keeps_24khz_mono_samples()
    {
        short[] original = [100, -200, 300, -400];
        var opus = VoicePcm.ToOpusPcm(original);
        Assert.Equal(original.Length * 4, opus.Length);
        var restored = VoicePcm.FromOpusPcm(opus);
        Assert.Equal(original, restored);
        Assert.Equal(original, VoicePcm.ToInt16(VoicePcm.ToBytes(original)));
    }
}

public sealed class VoiceMcpRuntimeTests
{
    [Fact]
    public void Codex_realtime_attaches_jarvis_mcp_and_live_search()
    {
        var config = VoiceMcpStdio.McpServerConfig("dotnet", ["/app/Jarvis.Api.dll", "voice-mcp"],
            new Dictionary<string, string>
            {
                ["JARVIS_INTERNAL_API_URL"] = "http://127.0.0.1:5082",
                ["VOICE_WORKER_SECRET"] = "test-secret"
            });
        Assert.Contains("mcp_servers={jarvis={", config, StringComparison.Ordinal);
        Assert.Contains("voice-mcp", config, StringComparison.Ordinal);
        Assert.Contains("JARVIS_INTERNAL_API_URL", config, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp_servers={}", config, StringComparison.Ordinal);
        Assert.Contains("web_search=\"live\"", CodexRealtimeSession.LiveWebSearchConfigOverrides);
        var start = CodexRealtimeSession.CreateAppServerStart("codex", Path.GetTempPath(), config);
        Assert.Contains("realtime_conversation", start.ArgumentList);
        Assert.Contains(config, start.ArgumentList);
        Assert.Equal("--enable", start.ArgumentList[start.ArgumentList.IndexOf("standalone_web_search") - 1]);
        Assert.DoesNotContain("mcp_servers={}", start.ArgumentList);
    }

    [Fact]
    public void Livekit_urls_become_websocket_endpoints()
    {
        Assert.Equal("ws://livekit:7880", LiveKitAccess.ToWebSocketUrl("http://livekit:7880"));
        Assert.Equal("wss://voice.example.com", LiveKitAccess.ToWebSocketUrl("https://voice.example.com"));
        Assert.Equal("ws://localhost:7880", LiveKitAccess.ToWebSocketUrl("ws://localhost:7880"));
    }

    [Fact]
    public async Task Tool_call_returns_safe_error_without_exposing_configuration()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var client = new VoiceMcpClient(http, "http://jarvis-api:5082/api/v1/voice/internal/conversation-id",
            "owner-id");
        using var message = JsonDocument.Parse("""
            {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"SearchMemory","arguments":{"query":"x"}}}
            """);
        var (response, stop) = await VoiceMcpStdio.HandleAsync(message.RootElement, client, CancellationToken.None);
        Assert.False(stop);
        Assert.NotNull(response);
        Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
        Assert.DoesNotContain("test-secret", response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tool_discovery_proxies_backend_jarvis_tools()
    {
        using var http = new HttpClient(new JsonHandler("""
            {"instructions":"You are Jarvis speaking in realtime.","tools":[
              {"name":"SearchMemory","description":"Search","inputSchema":{"type":"object"}},
              {"name":"CreateReminder","description":"Remind","inputSchema":{"type":"object"}}
            ]}
            """));
        var client = new VoiceMcpClient(http, "http://jarvis-api:5082/api/v1/voice/internal/conversation-id",
            "owner-id");
        using var message = JsonDocument.Parse("""{"jsonrpc":"2.0","id":4,"method":"tools/list"}""");
        var (response, stop) = await VoiceMcpStdio.HandleAsync(message.RootElement, client, CancellationToken.None);
        Assert.False(stop);
        var names = response!["result"]!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(["SearchMemory", "CreateReminder"], names);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret=test-secret");
    }
}
