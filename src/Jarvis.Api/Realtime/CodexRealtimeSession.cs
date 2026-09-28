using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Jarvis.Agents;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace Jarvis.Api.Realtime;

internal sealed class CodexRealtimeSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] DisabledFeatures =
    [
        "shell_tool", "shell_snapshot", "code_mode_host", "computer_use", "browser_use",
        "browser_use_external", "in_app_browser", "apps", "plugins", "skill_search",
        "image_generation", "multi_agent", "multi_agent_v2"
    ];

    private readonly Process _process;
    private readonly string _scratch;
    private readonly RTCPeerConnection _peer;
    private readonly AudioEncoder _encoder = new(true, true);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<JsonElement> _notifications = Channel.CreateUnbounded<JsonElement>();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<short> _microphone = [];
    private readonly object _microphoneGate = new();
    private AudioFormat? _audioFormat;
    private int _nextId;
    private Task? _readTask;
    private Task? _stderrTask;
    private Task? _handlerTask;
    private Task? _sendTask;
    private string? _threadId;

    private CodexRealtimeSession(Process process, string scratch, RTCPeerConnection peer)
    {
        _process = process;
        _scratch = scratch;
        _peer = peer;
    }

    public string ThreadId => _threadId ?? throw new InvalidOperationException("Codex realtime session has not started.");

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    internal static readonly string[] LiveWebSearchConfigOverrides =
    [
        "web_search=\"live\"",
        "features.web_search_request=true"
    ];

    public static ProcessStartInfo CreateAppServerStart(string executable, string workingDirectory, string mcpConfig)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "HOME", "CODEX_HOME", "TMPDIR", "SSL_CERT_FILE", "SSL_CERT_DIR", "LANG", "LC_ALL" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[key] = value;
        }

        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--stdio");
        start.ArgumentList.Add("--enable");
        start.ArgumentList.Add("realtime_conversation");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(mcpConfig);
        foreach (var feature in DisabledFeatures)
        {
            start.ArgumentList.Add("--disable");
            start.ArgumentList.Add(feature);
        }

        start.ArgumentList.Add("--enable");
        start.ArgumentList.Add("standalone_web_search");
        foreach (var overridePair in LiveWebSearchConfigOverrides)
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(overridePair);
        }

        return start;
    }

    public static async Task<CodexRealtimeSession> StartAsync(string executable, string? voice, string? model,
        string mcpConfig, string developerInstructions, CancellationToken cancellationToken)
    {
        var scratch = Directory.CreateTempSubdirectory("jarvis-codex-voice-").FullName;
        var start = CreateAppServerStart(executable, scratch, mcpConfig);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            try { Directory.Delete(scratch, true); } catch (IOException) { }
            throw new InvalidOperationException("Could not start the Codex CLI app-server.");
        }

        var peer = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = [],
            X_ICEIncludeAllInterfaceAddresses = true
        });
        var session = new CodexRealtimeSession(process, scratch, peer);
        session._readTask = session.ReadStdoutAsync();
        session._stderrTask = session.ReadStderrAsync();
        try
        {
            await session.InitializeAsync(voice, model, developerInstructions, cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public void AppendMicrophone(ReadOnlySpan<short> pcm24Mono)
    {
        lock (_microphoneGate)
        {
            _microphone.AddRange(pcm24Mono.ToArray());
            var limit = VoicePcm.Rate * 8;
            if (_microphone.Count > limit)
                _microphone.RemoveRange(0, _microphone.Count - limit);
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await CallAsync("thread/realtime/appendSpeech", new { threadId = ThreadId, text = text.Length > 16_000 ? text[..16_000] : text },
            cancellationToken);
    }

    public void StartNotifications(Func<JsonElement, CancellationToken, Task> handler)
    {
        _handlerTask = HandleNotificationsAsync(handler);
        _sendTask = SendMicrophoneAsync();
    }

    private async Task InitializeAsync(string? voice, string? model, string developerInstructions,
        CancellationToken cancellationToken)
    {
        var opus = _encoder.SupportedFormats
            .Where(format => string.Equals(format.FormatName, "OPUS", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (opus.Count == 0)
            throw new InvalidOperationException("SIPSorcery did not expose an OPUS encoder for Codex realtime.");
        _peer.addTrack(new MediaStreamTrack(opus, MediaStreamStatusEnum.SendRecv));
        await _peer.createDataChannel("oai-events", null);
        _peer.OnAudioFormatsNegotiated += formats => _audioFormat = formats.FirstOrDefault();
        _peer.OnAudioFrameReceived += frame =>
        {
            if (_threadId is null) return;
            try
            {
                var decoded = _encoder.DecodeAudio(frame.EncodedAudio, frame.AudioFormat);
                var pcm = VoicePcm.FromOpusPcm(decoded);
                if (pcm.Length == 0) return;
                var notification = JsonSerializer.SerializeToElement(new
                {
                    method = "thread/realtime/outputAudio/delta",
                    @params = new
                    {
                        threadId = _threadId,
                        audio = new
                        {
                            data = Convert.ToBase64String(VoicePcm.ToBytes(pcm)),
                            sampleRate = VoicePcm.Rate,
                            numChannels = 1,
                            samplesPerChannel = pcm.Length
                        }
                    }
                }, JsonOptions);
                _notifications.Writer.TryWrite(notification);
            }
            catch
            {
                // Invalid Opus frames are dropped; the JSON-RPC error path still surfaces transport failure.
            }
        };

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _peer.onconnectionstatechange += state =>
        {
            if (state == RTCPeerConnectionState.connected) connected.TrySetResult();
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                connected.TrySetException(new InvalidOperationException("Codex WebRTC connection " + state + "."));
        };

        await CallAsync("initialize", new
        {
            clientInfo = new { name = "jarvis-livekit-voice", version = "1.0.0" },
            capabilities = new { experimentalApi = true }
        }, cancellationToken);
        await NotifyAsync("initialized", new { }, cancellationToken);

        var threadParams = new Dictionary<string, object?>
        {
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["cwd"] = _scratch,
            ["ephemeral"] = true,
            ["developerInstructions"] = developerInstructions
        };
        if (!string.IsNullOrWhiteSpace(model)) threadParams["model"] = model;
        var started = await CallAsync("thread/start", threadParams, cancellationToken);
        _threadId = started.GetProperty("thread").GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Codex CLI app-server returned no thread identifier.");

        var ice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_peer.iceGatheringState == RTCIceGatheringState.complete) ice.TrySetResult();
        _peer.onicegatheringstatechange += state =>
        {
            if (state == RTCIceGatheringState.complete) ice.TrySetResult();
        };
        var offer = _peer.createOffer(null);
        await _peer.setLocalDescription(offer);
        try { await ice.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken); }
        catch (TimeoutException) { }

        var startParams = new Dictionary<string, object?>
        {
            ["threadId"] = _threadId,
            ["transport"] = new { type = "webrtc", sdp = _peer.localDescription.sdp.ToString() },
            ["outputModality"] = "audio",
            ["version"] = "v3",
            ["clientManagedHandoffs"] = false,
            ["codexResponsesAsItems"] = false,
            ["includeStartupContext"] = false,
            ["delegationAckFiller"] = false,
            ["prompt"] = VoiceSpeech.Prompt
        };
        if (!string.IsNullOrWhiteSpace(voice)) startParams["voice"] = voice;
        await CallAsync("thread/realtime/start", startParams, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var evt = await _notifications.Reader.ReadAsync(timeout.Token);
            if (!SameThread(evt)) continue;
            var method = Method(evt);
            if (method == "thread/realtime/error")
                throw new InvalidOperationException(ParamString(evt, "message") ?? "Codex realtime startup failed.");
            if (method != "thread/realtime/sdp") continue;
            var sdp = ParamString(evt, "sdp") ?? throw new InvalidOperationException("Codex realtime returned no SDP answer.");
            var result = _peer.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = sdp
            });
            if (result != SetDescriptionResultEnum.OK)
                throw new InvalidOperationException("Codex WebRTC remote description was rejected: " + result);
            break;
        }

        await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private async Task SendMicrophoneAsync()
    {
        var token = _lifetime.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(20, token);
                short[] frame;
                lock (_microphoneGate)
                {
                    var take = Math.Min(_microphone.Count, VoicePcm.Rate / 50);
                    if (take <= 0) continue;
                    frame = _microphone.GetRange(0, take).ToArray();
                    _microphone.RemoveRange(0, take);
                }

                if (_audioFormat is not { } format) continue;
                var encoded = _encoder.EncodeAudio(VoicePcm.ToOpusPcm(frame), format);
                if (encoded.Length == 0) continue;
                _peer.SendAudio((uint)(frame.Length * 2), encoded);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Keep the send loop alive across a single encode failure.
            }
        }
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            await WriteAsync(JsonSerializer.Serialize(new { method, id, @params = parameters }, JsonOptions),
                cancellationToken);
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteAsync(JsonSerializer.Serialize(new { method, @params = parameters }, JsonOptions), cancellationToken);

    private async Task WriteAsync(string payload, CancellationToken cancellationToken)
    {
        await _write.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _write.Release();
        }
    }

    private async Task ReadStdoutAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                JsonElement message;
                try
                {
                    message = JsonDocument.Parse(line).RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message.TryGetProperty("id", out var id) && id.TryGetInt32(out var requestId) &&
                    _pending.TryGetValue(requestId, out var pending))
                {
                    if (message.TryGetProperty("error", out var error))
                        pending.TrySetException(new InvalidOperationException(
                            "Codex app-server request failed: " +
                            (error.TryGetProperty("message", out var reason) ? reason.GetString() : error.ToString())));
                    else
                        pending.TrySetResult(message.TryGetProperty("result", out var result)
                            ? result.Clone()
                            : message.Clone());
                }
                else
                    await _notifications.Writer.WriteAsync(message);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The process closed; pending calls fail below.
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new InvalidOperationException("Codex app-server closed its output."));
            if (_threadId is not null)
            {
                var error = JsonSerializer.SerializeToElement(new
                {
                    method = "thread/realtime/error",
                    @params = new { threadId = _threadId, message = "Codex app-server closed its output." }
                }, JsonOptions);
                _notifications.Writer.TryWrite(error);
            }

            _notifications.Writer.TryComplete();
        }
    }

    private async Task ReadStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync() is { } line)
                Debug.WriteLine("Codex app-server: " + line);
        }
        catch (Exception)
        {
            // stderr closed with the process.
        }
    }

    private async Task HandleNotificationsAsync(Func<JsonElement, CancellationToken, Task> handler)
    {
        try
        {
            await foreach (var message in _notifications.Reader.ReadAllAsync(_lifetime.Token))
            {
                if (!SameThread(message)) continue;
                try { await handler(message, _lifetime.Token); }
                catch (OperationCanceledException) { throw; }
                catch { /* keep the session alive across a single notification failure */ }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string? Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;

    private bool SameThread(JsonElement message)
    {
        if (_threadId is null) return true;
        if (!message.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            return true;
        if (!parameters.TryGetProperty("threadId", out var thread) || thread.ValueKind != JsonValueKind.String)
            return true;
        return thread.GetString() == _threadId;
    }

    private static string? ParamString(JsonElement message, string name)
    {
        if (!message.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            return null;
        return parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_threadId is not null && !_process.HasExited)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await CallAsync("thread/realtime/stop", new { threadId = _threadId }, stop.Token); }
                catch { /* the process is going away */ }
            }
        }
        finally
        {
            await _lifetime.CancelAsync();
            try { _peer.Close("done"); } catch { /* already closed */ }
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { /* abandoned */ }
            _process.Dispose();
            _encoder.Dispose();
            _write.Dispose();
            _lifetime.Dispose();
            try { Directory.Delete(_scratch, true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (_handlerTask is not null) try { await _handlerTask; } catch { /* cancelled */ }
            if (_sendTask is not null) try { await _sendTask; } catch { /* cancelled */ }
            if (_readTask is not null) try { await _readTask; } catch { /* closed */ }
            if (_stderrTask is not null) try { await _stderrTask; } catch { /* closed */ }
        }
    }
}
