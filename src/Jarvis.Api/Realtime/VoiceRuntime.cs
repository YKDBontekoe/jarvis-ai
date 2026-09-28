using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Jarvis.Agents;
using LiveKit.Proto;
using LiveKit.Rtc;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class VoiceRuntime(
    IConfiguration configuration,
    VoiceBackendSession backend,
    CodexExecutable codex,
    IHubContext<JarvisEventsHub> hub,
    ILogger<VoiceRuntime> logger) : IHostedService
{
    private readonly ConcurrentDictionary<string, VoiceRoomSession> _sessions = new();

    public async Task StartSessionAsync(string room, Guid conversationId, Guid ownerId, string? voice,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["LiveKit:ApiKey"];
        var apiSecret = configuration["LiveKit:ApiSecret"];
        var internalUrl = configuration["LiveKit:InternalUrl"];
        var workerSecret = configuration["Voice:WorkerSecret"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret) ||
            string.IsNullOrWhiteSpace(workerSecret) || string.IsNullOrWhiteSpace(internalUrl))
            throw new InvalidOperationException("LiveKit is not configured.");

        var session = new VoiceRoomSession(room, conversationId, ownerId, voice, LiveKitAccess.ToWebSocketUrl(internalUrl),
            LiveKitAccess.CreateRoomToken(apiKey, apiSecret, room, "jarvis-voice", DateTimeOffset.UtcNow.AddMinutes(30),
                canUpdateOwnMetadata: true), LiveKitAccess.LoopbackApiUrl(configuration), workerSecret,
            configuration["Codex:ModelClasses:Realtime"], backend, hub, logger, codex.Resolve());
        if (!_sessions.TryAdd(room, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException("A voice session is already starting for that LiveKit room.");
        }

        try
        {
            await session.StartUntilReadyAsync(cancellationToken);
        }
        catch
        {
            _sessions.TryRemove(room, out _);
            await session.DisposeAsync();
            throw;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await session.RunAsync();
            }
            finally
            {
                _sessions.TryRemove(room, out _);
                await session.DisposeAsync();
            }
        });
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        _sessions.Clear();
    }
}

internal sealed class VoiceRoomSession(
    string roomName,
    Guid conversationId,
    Guid ownerId,
    string? voice,
    string liveKitUrl,
    string agentToken,
    string apiUrl,
    string workerSecret,
    string? realtimeModel,
    VoiceBackendSession backend,
    IHubContext<JarvisEventsHub> hub,
    ILogger logger,
    string codexExecutable) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly VoicePlaybackGate _playback = new();
    private readonly VoiceBargeIn _barge = new();
    private readonly Room _room = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _audioTasks = [];
    private AudioSource? _outputSource;
    private OrderedAudioOutput? _audioOut;
    private CodexRealtimeSession? _codex;
    private string _phase = "";
    private string _assistantTranscript = "";
    private double? _lastAudioFrameAt;
    private double? _audioEndsAt;
    private double? _assistantResponseDoneAt;

    public async Task StartUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _room.ConnectAsync(liveKitUrl, agentToken, new LiveKit.Rtc.RoomOptions { AutoSubscribe = true }, linked.Token);
        await SetAttributeAsync("jarvis.voice.status", "starting");
        string instructions;
        try
        {
            instructions = (await backend.GetSessionAsync(ownerId, conversationId, linked.Token)).Instructions;
            if (string.IsNullOrWhiteSpace(instructions)) instructions = VoiceSpeech.Prompt;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not load Jarvis voice tools and memory context.");
            instructions = VoiceSpeech.Prompt;
        }

        var (command, arguments) = VoiceMcpStdio.ResolveLaunch();
        var mcpEnv = new Dictionary<string, string>
        {
            ["JARVIS_INTERNAL_API_URL"] = apiUrl,
            ["VOICE_WORKER_SECRET"] = workerSecret,
            ["JARVIS_VOICE_CONVERSATION_ID"] = conversationId.ToString(),
            ["JARVIS_VOICE_OWNER_ID"] = ownerId.ToString()
        };
        foreach (var key in new[] { "PATH", "HOME", "DOTNET_ROOT", "DOTNET_HOST_PATH" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) mcpEnv[key] = value;
        }

        var mcp = VoiceMcpStdio.McpServerConfig(command, arguments, mcpEnv);
        try
        {
            _codex = await CodexRealtimeSession.StartAsync(codexExecutable, VoiceSpeech.SanitizeVoice(voice),
                string.IsNullOrWhiteSpace(realtimeModel) ? null : realtimeModel, mcp, instructions, linked.Token);
        }
        catch
        {
            await SetAttributeAsync("jarvis.voice.status", "unavailable");
            throw;
        }

        _outputSource = new AudioSource(VoicePcm.Rate, 1, 20_000);
        var outputTrack = LocalAudioTrack.Create("jarvis-voice", _outputSource);
        await _room.LocalParticipant!.PublishTrackAsync(outputTrack, cancellationToken: linked.Token);
        _audioOut = new OrderedAudioOutput(_outputSource);
        _playback.AllowRealtimeOutput();
        await SetAttributeAsync("jarvis.voice.status", "ready");
        await SetPhaseAsync("listening");
        _ready.TrySetResult();
    }

    public async Task RunAsync()
    {
        var ct = _lifetime.Token;
        Task? playbackPhase = null;
        try
        {
            await _ready.Task.WaitAsync(ct);
            var participant = await WaitForParticipantAsync(ct);
            logger.LogInformation("Voice runtime joined room {Room} for conversation {ConversationId}.",
                roomName, conversationId);
            _codex!.StartNotifications(OnCodexNotificationAsync);
            playbackPhase = MonitorPlaybackPhaseAsync(ct);
            _room.TrackSubscribed += (_, e) =>
            {
                if (e.Track.Kind == TrackKind.KindAudio)
                    _audioTasks.Add(ForwardAudioAsync(e.Track, ct));
            };
            var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _room.ParticipantDisconnected += (_, disconnected) =>
            {
                if (disconnected.Identity == participant.Identity) left.TrySetResult();
            };
            _room.Disconnected += (_, _) => left.TrySetResult();
            foreach (var publication in participant.TrackPublications.Values)
                if (publication.Track is { Kind: TrackKind.KindAudio } track)
                    _audioTasks.Add(ForwardAudioAsync(track, ct));

            var exited = _codex.WaitForExitAsync(ct);
            var finished = await Task.WhenAny(exited, left.Task);
            if (finished == exited && !ct.IsCancellationRequested)
            {
                logger.LogError("Codex app-server exited while the voice session was still live.");
                await SetAttributeAsync("jarvis.voice.status", "unavailable");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Voice session for conversation {ConversationId} failed.", conversationId);
        }
        finally
        {
            await _lifetime.CancelAsync();
            if (playbackPhase is not null)
                try { await playbackPhase; } catch (OperationCanceledException) { }
        }
    }

    private async Task<Participant> WaitForParticipantAsync(CancellationToken cancellationToken)
    {
        if (_room.RemoteParticipants.Count > 0)
            return _room.RemoteParticipants.Values.First();
        var arrived = new TaskCompletionSource<Participant>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnected(object? _, Participant participant) => arrived.TrySetResult(participant);
        _room.ParticipantConnected += OnConnected;
        try
        {
            if (_room.RemoteParticipants.Count > 0)
                return _room.RemoteParticipants.Values.First();
            return await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
        finally
        {
            _room.ParticipantConnected -= OnConnected;
        }
    }

    private async Task ForwardAudioAsync(Track track, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new AudioStream(track, VoicePcm.Rate, 1, 20);
            await foreach (var evt in stream.WithCancellation(cancellationToken))
            {
                var samples = VoicePcm.ToInt16(evt.Frame.DataBytes);
                _codex?.AppendMicrophone(samples);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not forward LiveKit microphone audio to Codex realtime.");
        }
    }

    private async Task OnCodexNotificationAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var method = message.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        if (!message.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            return;
        if (method == "thread/realtime/transcript/delta")
            await OnTranscriptDeltaAsync(parameters, cancellationToken);
        else if (method == "thread/realtime/transcript/done")
            await OnTranscriptDoneAsync(parameters, cancellationToken);
        else if (method == "thread/realtime/outputAudio/delta")
            await OnOutputAudioAsync(parameters, cancellationToken);
        else if (method == "thread/realtime/error")
        {
            logger.LogError("Codex realtime error: {Message}",
                parameters.TryGetProperty("message", out var error) ? error.GetString() : "unknown error");
            Duck(true);
            _assistantResponseDoneAt = null;
            await SpeakErrorAsync(cancellationToken);
        }
    }

    private async Task OnTranscriptDeltaAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var role = parameters.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
        var delta = parameters.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.String
            ? deltaElement.GetString()
            : null;
        var partial = parameters.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()
            : null;
        if (role == "user")
        {
            partial = partial ?? delta;
            if (!string.IsNullOrWhiteSpace(partial))
                await PublishCaptionAsync("user", partial, false, cancellationToken);
            var now = Now();
            if (_barge.Consider(partial ?? "", _assistantTranscript, _phase == "speaking", now))
            {
                Duck(true);
                await SetPhaseAsync("listening");
            }
        }
        else if (role == "assistant")
        {
            if (delta is not null) _assistantTranscript += delta;
            else if (partial is not null)
                _assistantTranscript = partial.StartsWith(_assistantTranscript, StringComparison.Ordinal)
                    ? partial
                    : _assistantTranscript + partial;
            if (_assistantTranscript.Length > 0)
            {
                await PublishCaptionAsync("assistant", _assistantTranscript, false, cancellationToken);
                await SetPhaseAsync("speaking");
            }
        }
    }

    private async Task OnTranscriptDoneAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var role = parameters.TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
        var text = parameters.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()?.Trim()
            : null;
        if (role == "assistant")
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                _assistantTranscript = text;
                await PublishCaptionAsync("assistant", text, true, cancellationToken);
                _ = PersistAsync("assistant", text);
            }

            _assistantResponseDoneAt = Now();
            return;
        }

        if (role != "user" || string.IsNullOrWhiteSpace(text)) return;
        await PublishCaptionAsync("user", text, true, cancellationToken);
        if (_phase == "speaking" && VoiceSpeech.IsEcho(text, _assistantTranscript)) return;
        _ = PersistAsync("user", text);
        _assistantTranscript = "";
        _assistantResponseDoneAt = null;
        _lastAudioFrameAt = null;
        _audioEndsAt = null;
        _barge.Reset();
        await SetPhaseAsync("thinking");
    }

    private async Task OnOutputAudioAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var generation = _playback.AcceptFrame();
        if (generation is null) return;
        if (!parameters.TryGetProperty("audio", out var audio) || audio.ValueKind != JsonValueKind.Object) return;
        try
        {
            var sampleRate = audio.TryGetProperty("sampleRate", out var rate) && rate.TryGetInt32(out var parsedRate)
                ? parsedRate
                : VoicePcm.Rate;
            var channels = audio.TryGetProperty("numChannels", out var channelElement) &&
                           channelElement.TryGetInt32(out var parsedChannels)
                ? parsedChannels
                : 1;
            if (sampleRate != VoicePcm.Rate || channels != 1) return;
            var encoded = audio.GetProperty("data").GetString() ?? "";
            var data = Convert.FromBase64String(encoded);
            var samples = audio.TryGetProperty("samplesPerChannel", out var sampleElement) &&
                          sampleElement.TryGetInt32(out var parsedSamples)
                ? parsedSamples
                : data.Length / 2;
            var now = Now();
            if (_audioEndsAt is null || _audioEndsAt < now) _audioEndsAt = now;
            _audioEndsAt += samples / (double)sampleRate;
            _lastAudioFrameAt = now;
            await SetPhaseAsync("speaking");
            if (generation == _playback.Generation)
                _audioOut?.Enqueue(new AudioFrame(data, sampleRate, channels, samples));
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Codex voice returned an invalid audio chunk.");
        }
    }

    private async Task SpeakErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            _playback.BeginTurn();
            _playback.AllowRealtimeOutput();
            await SetPhaseAsync("speaking");
            if (_codex is not null)
                await _codex.SpeakAsync("I could not complete that. Check the Jarvis app.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not speak the voice error.");
        }
    }

    private async Task MonitorPlaybackPhaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
                var now = Now();
                if (_phase == "speaking" && _lastAudioFrameAt is { } last && _audioEndsAt is { } ends &&
                    now >= ends + 0.12 && now - last >= 0.25)
                    await SetPhaseAsync("listening");
                else if (_phase == "speaking" && _assistantResponseDoneAt is { } done && _lastAudioFrameAt is null &&
                         now - done >= 1)
                    await SetPhaseAsync("listening");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Duck(bool suppressInflight)
    {
        _playback.Duck(suppressInflight);
        _audioOut?.Clear();
        _barge.Reset();
    }

    private Task PersistAsync(string role, string text) =>
        backend.PersistUtteranceAsync(ownerId, conversationId, role, text, CancellationToken.None);

    private async Task PublishCaptionAsync(string role, string text, bool final, CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text.Length > 2_000) text = text[..2_000];
        try
        {
            await hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
                .SendAsync("voice.caption", new { conversationId, role, text, final }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not publish a voice caption.");
        }
    }

    private async Task SetPhaseAsync(string value)
    {
        if (_phase == value) return;
        _phase = value;
        await SetAttributeAsync("jarvis.voice.phase", value);
    }

    private async Task SetAttributeAsync(string key, string value)
    {
        try
        {
            if (_room.LocalParticipant is { } local)
                await local.SetAttributesAsync(new Dictionary<string, string> { [key] = value });
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not publish voice attribute {Key}.", key);
        }
    }

    private int _disposed;

    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Duck(false);
        await _lifetime.CancelAsync();
        foreach (var task in _audioTasks)
            try { await task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* cancelled */ }
        if (_audioOut is not null) await _audioOut.DisposeAsync();
        if (_codex is not null) await _codex.DisposeAsync();
        _outputSource?.Dispose();
        try { await _room.DisconnectAsync(); } catch { /* already gone */ }
        _room.Dispose();
        _lifetime.Dispose();
    }
}

internal sealed class OrderedAudioOutput : IAsyncDisposable
{
    private readonly AudioSource _source;
    private readonly Channel<AudioFrame?> _queue = Channel.CreateUnbounded<AudioFrame?>();
    private readonly Task _play;

    public OrderedAudioOutput(AudioSource source)
    {
        _source = source;
        _play = PlayAsync();
    }

    public void Enqueue(AudioFrame frame) => _queue.Writer.TryWrite(frame);

    public void Clear()
    {
        while (_queue.Reader.TryRead(out _)) { }
        _source.ClearQueue();
    }

    private async Task PlayAsync()
    {
        await foreach (var frame in _queue.Reader.ReadAllAsync())
        {
            if (frame is null) return;
            try { await _source.CaptureFrameAsync(frame); }
            catch { /* a dropped frame is preferable to tearing down playback */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Clear();
        _queue.Writer.TryWrite(null);
        try { await _play.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { /* abandoned */ }
    }
}
