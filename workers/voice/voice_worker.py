from __future__ import annotations

import asyncio
import base64
import json
import logging
import os
import shutil
import tempfile
from fractions import Fraction
from pathlib import Path
from typing import Any, Awaitable, Callable

import httpx
from aiortc import MediaStreamTrack, RTCConfiguration, RTCPeerConnection, RTCSessionDescription
from av import AudioFrame, AudioResampler
from livekit import agents, rtc

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger("jarvis.voice")


class CodexInputTrack(MediaStreamTrack):
    kind = "audio"

    def __init__(self) -> None:
        super().__init__()
        self.buffer = bytearray()
        self.samples_sent = 0
        self.started_at: float | None = None

    def append(self, frame: rtc.AudioFrame) -> None:
        if frame.sample_rate != 24000 or frame.num_channels != 1:
            raise ValueError("Codex microphone audio must be mono PCM at 24 kHz.")
        self.buffer.extend(frame.data.cast("B").tobytes())
        # Keep at most one second of microphone audio when a transport stalls.
        if len(self.buffer) > 48000:
            del self.buffer[:-48000]

    async def recv(self) -> AudioFrame:
        loop = asyncio.get_running_loop()
        if self.started_at is None:
            self.started_at = loop.time()
        await asyncio.sleep(max(0, self.started_at + self.samples_sent / 24000 - loop.time()))
        pcm = bytes(self.buffer[:960])
        del self.buffer[:960]
        frame = AudioFrame(format="s16", layout="mono", samples=480)
        frame.planes[0].update(pcm.ljust(frame.planes[0].buffer_size, b"\0"))
        frame.sample_rate = 24000
        frame.time_base = Fraction(1, 24000)
        frame.pts = self.samples_sent
        self.samples_sent += 480
        return frame


class CodexRealtimeSession:
    """Audio bridge to the signed-in Codex CLI app-server, with no provider API key."""

    def __init__(self, process: asyncio.subprocess.Process, scratch: str) -> None:
        self.process = process
        self.scratch = scratch
        self.next_id = 0
        self.write_lock = asyncio.Lock()
        self.pending: dict[int, asyncio.Future[dict[str, Any]]] = {}
        self.notifications: asyncio.Queue[dict[str, Any]] = asyncio.Queue()
        self.notification_task = asyncio.create_task(self._read_loop())
        self.handler_task: asyncio.Task[None] | None = None
        self.thread_id: str | None = None
        self.peer = RTCPeerConnection(RTCConfiguration(iceServers=[]))
        self.input_track = CodexInputTrack()
        self.peer.addTrack(self.input_track)
        self.peer.createDataChannel("oai-events")
        self.media_tasks: set[asyncio.Task[None]] = set()
        self.media_connected = asyncio.Event()

        @self.peer.on("connectionstatechange")
        async def connection_changed() -> None:
            if self.peer.connectionState == "connected":
                self.media_connected.set()

        @self.peer.on("track")
        def audio_track(track: MediaStreamTrack) -> None:
            if track.kind == "audio":
                task = asyncio.create_task(self._read_audio(track))
                self.media_tasks.add(task)
                task.add_done_callback(self.media_tasks.discard)

    async def _read_audio(self, track: MediaStreamTrack) -> None:
        resampler = AudioResampler(format="s16", layout="mono", rate=24000)
        try:
            while True:
                for frame in resampler.resample(await track.recv()):
                    pcm = bytes(frame.planes[0])[:frame.samples * 2]
                    await self.notifications.put({
                        "method": "thread/realtime/outputAudio/delta",
                        "params": {"threadId": self.thread_id, "audio": {
                            "data": base64.b64encode(pcm).decode("ascii"), "sampleRate": 24000,
                            "numChannels": 1, "samplesPerChannel": frame.samples,
                        }},
                    })
        except asyncio.CancelledError:
            raise
        except Exception:
            if self.peer.connectionState not in ("closed", "failed"):
                logger.exception("Codex WebRTC audio receiver failed.")

    @classmethod
    async def start(cls) -> CodexRealtimeSession:
        scratch = tempfile.mkdtemp(prefix="jarvis-codex-voice-")
        codex = os.getenv("CODEX_EXECUTABLE_PATH", "codex")
        codex_path = shutil.which(codex) or codex
        home = os.getenv("HOME", str(Path.home()))
        env = {
            "PATH": os.getenv("PATH", "/usr/local/bin:/usr/bin:/bin"),
            "HOME": home,
            "CODEX_HOME": os.getenv("CODEX_HOME", str(Path(home) / ".codex")),
        }
        args = [codex_path, "app-server", "--stdio", "--enable", "realtime_conversation", "-c", "mcp_servers={}"]
        for feature in (
            "shell_tool", "shell_snapshot", "code_mode_host", "computer_use", "browser_use",
            "browser_use_external", "in_app_browser", "apps", "plugins", "skill_search",
            "standalone_web_search", "image_generation", "multi_agent", "multi_agent_v2",
        ):
            args.extend(("--disable", feature))
        process = await asyncio.create_subprocess_exec(
            *args,
            cwd=scratch,
            env=env,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        session = cls(process, scratch)
        session.stderr_task = asyncio.create_task(session._read_stderr())
        try:
            await session.call("initialize", {
                "clientInfo": {"name": "jarvis-livekit-voice", "version": "1.0.0"},
                "capabilities": {"experimentalApi": True},
            })
            await session.notify("initialized", {})
            result = await session.call(
                "thread/start",
                {
                    "approvalPolicy": "never",
                    "sandbox": "read-only",
                    "cwd": scratch,
                    "ephemeral": True,
                    **({"model": os.environ["CODEX_MODEL"]} if os.getenv("CODEX_MODEL") else {}),
                },
            )
            session.thread_id = result["thread"]["id"]
            await session.peer.setLocalDescription(await session.peer.createOffer())
            await session.call(
                "thread/realtime/start",
                {
                    "threadId": session.thread_id,
                    "transport": {"type": "webrtc", "sdp": session.peer.localDescription.sdp},
                    "outputModality": "audio",
                    "voice": os.getenv("CODEX_VOICE", "juniper"),
                    "version": "v3",
                    "clientManagedHandoffs": True,
                    "codexResponsesAsItems": True,
                    "prompt": (
                        "You are the speech interface for Jarvis. Transcribe the user's speech and report it. "
                        "Do not answer the user or speak on your own. Jarvis will reason and use its approved tools. "
                        "Speak only text explicitly appended by Jarvis."
                    ),
                },
            )
            # The start RPC only acknowledges the request; protocol/transport failures arrive later.
            async with asyncio.timeout(30):
                while True:
                    event = await session.notifications.get()
                    if event.get("params", {}).get("threadId") != session.thread_id:
                        continue
                    if event.get("method") == "thread/realtime/error":
                        raise RuntimeError(event.get("params", {}).get("message", "Codex realtime startup failed."))
                    if event.get("method") == "thread/realtime/sdp":
                        await session.peer.setRemoteDescription(RTCSessionDescription(
                            sdp=event["params"]["sdp"], type="answer"))
                        break
            await asyncio.wait_for(session.media_connected.wait(), timeout=30)
            return session
        except BaseException:
            await session.close()
            raise

    async def call(self, method: str, params: dict[str, Any]) -> dict[str, Any]:
        if self.process.stdin is None:
            raise RuntimeError("Codex app-server stdin is unavailable.")
        self.next_id += 1
        request_id = self.next_id
        future = asyncio.get_running_loop().create_future()
        self.pending[request_id] = future
        try:
            async with self.write_lock:
                payload = json.dumps({"method": method, "id": request_id, "params": params}, separators=(",", ":"))
                self.process.stdin.write(payload.encode("utf-8") + b"\n")
                await self.process.stdin.drain()
            response = await asyncio.wait_for(future, timeout=30)
        finally:
            self.pending.pop(request_id, None)
        if "error" in response:
            error = response["error"]
            raise RuntimeError(f"Codex app-server {method} failed: {error.get('message', error)}")
        return response.get("result", {})

    async def notify(self, method: str, params: dict[str, Any]) -> None:
        if self.process.stdin is None:
            raise RuntimeError("Codex app-server stdin is unavailable.")
        async with self.write_lock:
            payload = json.dumps({"method": method, "params": params}, separators=(",", ":"))
            self.process.stdin.write(payload.encode("utf-8") + b"\n")
            await self.process.stdin.drain()

    async def append_audio(self, frame: rtc.AudioFrame) -> None:
        if self.thread_id is None:
            raise RuntimeError("Codex realtime session has not started.")
        self.input_track.append(frame)

    async def speak(self, text: str) -> None:
        if self.thread_id is None or not text.strip():
            return
        await self.call("thread/realtime/appendSpeech", {"threadId": self.thread_id, "text": text[:16000]})

    def start_notifications(self, handler: Callable[[dict[str, Any]], Awaitable[None]]) -> None:
        self.handler_task = asyncio.create_task(self._handle_notifications(handler))

    async def _read_loop(self) -> None:
        assert self.process.stdout is not None
        crashed = False
        try:
            while line := await self.process.stdout.readline():
                try:
                    message = json.loads(line)
                except json.JSONDecodeError:
                    logger.warning("Ignoring malformed Codex app-server message.")
                    continue
                if isinstance(message.get("id"), int):
                    future = self.pending.get(message["id"])
                    if future is not None and not future.done():
                        future.set_result(message)
                else:
                    await self.notifications.put(message)
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("Codex app-server reader failed.")
            crashed = True
        else:
            crashed = True
        finally:
            for future in self.pending.values():
                if not future.done():
                    future.set_exception(RuntimeError("Codex app-server closed its output."))
            if crashed and self.thread_id is not None:
                await self.notifications.put(
                    {
                        "method": "thread/realtime/error",
                        "params": {
                            "threadId": self.thread_id,
                            "message": "Codex app-server closed its output.",
                        },
                    }
                )

    async def _handle_notifications(self, handler: Callable[[dict[str, Any]], Awaitable[None]]) -> None:
        while True:
            message = await self.notifications.get()
            try:
                params = message.get("params", {})
                if params.get("threadId") != self.thread_id:
                    continue
                await handler(message)
            except asyncio.CancelledError:
                raise
            except Exception:
                logger.exception("Codex realtime notification handling failed.")

    async def _read_stderr(self) -> None:
        assert self.process.stderr is not None
        while line := await self.process.stderr.readline():
            logger.debug("Codex app-server: %s", line.decode(errors="replace").rstrip())

    async def close(self) -> None:
        if self.thread_id and self.process.returncode is None:
            try:
                await asyncio.wait_for(
                    self.call("thread/realtime/stop", {"threadId": self.thread_id}), timeout=3
                )
            except Exception:
                pass
        await self.peer.close()
        for task in self.media_tasks:
            task.cancel()
        if self.media_tasks:
            await asyncio.gather(*self.media_tasks, return_exceptions=True)
        tasks = [
            task
            for task in (self.handler_task, self.notification_task, getattr(self, "stderr_task", None))
            if task is not None
        ]
        for task in tasks:
            task.cancel()
        if self.process.stdin is not None:
            self.process.stdin.close()
            try:
                await self.process.stdin.wait_closed()
            except (BrokenPipeError, ConnectionResetError):
                pass
        if self.process.returncode is None:
            try:
                self.process.kill()
            except ProcessLookupError:
                pass
        try:
            await asyncio.wait_for(self.process.wait(), timeout=3)
        except TimeoutError:
            pass
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
        shutil.rmtree(self.scratch, ignore_errors=True)


async def _voice_entrypoint(ctx: agents.JobContext) -> None:
    metadata = json.loads(ctx.job.metadata or "{}")
    conversation_id = metadata.get("conversationId")
    owner_id = metadata.get("ownerId")
    if not isinstance(conversation_id, str) or not isinstance(owner_id, str):
        raise RuntimeError("Voice dispatch is missing its conversation and owner identifiers.")

    api_url = os.getenv("JARVIS_INTERNAL_API_URL", "http://localhost:5082").rstrip("/")
    worker_secret = os.getenv("VOICE_WORKER_SECRET", "")
    if not worker_secret:
        raise RuntimeError("VOICE_WORKER_SECRET is required by the Jarvis voice worker.")

    await ctx.connect()
    await ctx.room.local_participant.set_attributes({"jarvis.voice.status": "starting"})
    http = httpx.AsyncClient(timeout=httpx.Timeout(1200, connect=5))
    try:
        codex = await CodexRealtimeSession.start()
    except BaseException:
        await ctx.room.local_participant.set_attributes({"jarvis.voice.status": "unavailable"})
        await asyncio.sleep(2)
        await http.aclose()
        await ctx.room.disconnect()
        raise
    await ctx.room.local_participant.set_attributes({"jarvis.voice.status": "ready"})
    try:
        participant = await asyncio.wait_for(ctx.wait_for_participant(), timeout=60)
    except BaseException:
        await codex.close()
        await http.aclose()
        await ctx.room.disconnect()
        raise
    logger.info("Voice worker joined room %s for conversation %s.", ctx.room.name, conversation_id)

    output_source = rtc.AudioSource(sample_rate=24000, num_channels=1, queue_size_ms=1200)
    output_track = rtc.LocalAudioTrack.create_audio_track("jarvis-voice", output_source)
    await ctx.room.local_participant.publish_track(output_track)
    audio_tasks: set[asyncio.Task[None]] = set()
    turn_tasks: set[asyncio.Task[None]] = set()
    playback_tasks: set[asyncio.Task[None]] = set()
    playback_generation = 0
    turn_lock = asyncio.Lock()
    current_turn: asyncio.Task[None] | None = None

    def duck_output() -> None:
        nonlocal playback_generation
        playback_generation += 1
        output_source.clear_queue()
        for task in list(playback_tasks):
            task.cancel()

    async def play_output_frame(frame: rtc.AudioFrame, generation: int) -> None:
        if generation != playback_generation:
            return
        try:
            await output_source.capture_frame(frame)
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("Could not play a Codex voice audio frame.")

    async def speak_failure() -> None:
        try:
            await codex.speak("I could not complete that. Check the Jarvis app.")
        except Exception:
            logger.exception("Could not speak the voice error.")

    async def speak_error_turn() -> None:
        async with turn_lock:
            await speak_failure()

    async def process_transcript(transcript: str) -> None:
        try:
            async with turn_lock:
                streamed_text = ""
                speech_buffer = ""
                final_text: str | None = None
                async with http.stream(
                    "POST",
                    f"{api_url}/api/v1/voice/internal/{conversation_id}/transcript",
                    headers={"X-Jarvis-Voice-Secret": worker_secret},
                    json={"ownerId": owner_id, "transcript": transcript},
                ) as response:
                    response.raise_for_status()
                    async for line in response.aiter_lines():
                        if not line:
                            continue
                        event = json.loads(line)
                        if event.get("type") == "delta":
                            delta = event.get("text", "")
                            if not isinstance(delta, str) or not delta:
                                continue
                            streamed_text += delta
                            speech_buffer += delta
                            if len(speech_buffer) >= 160 or (
                                len(speech_buffer) >= 48 and speech_buffer.rstrip().endswith((".", "!", "?", "\n"))
                            ):
                                await codex.speak(speech_buffer)
                                speech_buffer = ""
                        elif event.get("type") == "done":
                            final_text = event.get("responseText")

                if isinstance(final_text, str) and final_text.strip():
                    if not streamed_text or final_text.startswith(streamed_text):
                        if not streamed_text:
                            speech_buffer = final_text
                        else:
                            speech_buffer += final_text[len(streamed_text):]
                    else:
                        duck_output()
                        speech_buffer = final_text
                if speech_buffer.strip():
                    await codex.speak(speech_buffer)
                elif not streamed_text.strip():
                    await speak_failure()
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("Voice response failed")
            await speak_error_turn()

    def finish_turn(task: asyncio.Task[None]) -> None:
        turn_tasks.discard(task)
        if not task.cancelled() and (failure := task.exception()) is not None:
            logger.error("Voice response failed: %s", failure)

    async def on_codex_notification(message: dict[str, Any]) -> None:
        nonlocal current_turn
        method = message.get("method")
        params = message.get("params", {})
        if method == "thread/realtime/transcript/delta" and params.get("role") == "user":
            duck_output()
        elif method == "thread/realtime/transcript/done" and params.get("role") == "user":
            transcript = params.get("text", "").strip()
            if not transcript:
                return
            duck_output()
            if current_turn is not None and not current_turn.done():
                current_turn.cancel()
            current_turn = asyncio.create_task(process_transcript(transcript))
            turn_tasks.add(current_turn)
            current_turn.add_done_callback(finish_turn)
        elif method == "thread/realtime/outputAudio/delta":
            audio = params.get("audio", {})
            try:
                sample_rate = int(audio.get("sampleRate", 24000))
                channels = int(audio.get("numChannels", 1))
                data = base64.b64decode(audio["data"], validate=True)
                samples = audio.get("samplesPerChannel") or len(data) // (2 * channels)
                frame = rtc.AudioFrame(data, sample_rate, channels, int(samples))
                if sample_rate != 24000 or channels != 1:
                    logger.warning("Codex voice audio format differs from the LiveKit output track.")
                    return
                generation = playback_generation
                task = asyncio.create_task(play_output_frame(frame, generation))
                playback_tasks.add(task)
                task.add_done_callback(playback_tasks.discard)
            except (KeyError, TypeError, ValueError):
                logger.exception("Codex voice returned an invalid audio chunk.")
        elif method == "thread/realtime/error":
            logger.error("Codex realtime error: %s", params.get("message", "unknown error"))
            duck_output()
            if current_turn is not None and not current_turn.done():
                current_turn.cancel()
            current_turn = asyncio.create_task(speak_error_turn())
            turn_tasks.add(current_turn)
            current_turn.add_done_callback(finish_turn)

    codex.start_notifications(on_codex_notification)

    async def forward_audio(track: rtc.RemoteAudioTrack) -> None:
        stream = rtc.AudioStream(track, sample_rate=24000, num_channels=1, frame_size_ms=100)
        try:
            async for event in stream:
                await codex.append_audio(event.frame)
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("Could not forward LiveKit microphone audio to Codex realtime.")

    @ctx.room.on("track_subscribed")
    def on_track_subscribed(track: rtc.Track, publication: rtc.RemoteTrackPublication, _participant: rtc.RemoteParticipant) -> None:
        if isinstance(track, rtc.RemoteAudioTrack):
            task = asyncio.create_task(forward_audio(track))
            audio_tasks.add(task)
            task.add_done_callback(audio_tasks.discard)

    participant_left = asyncio.Event()

    @ctx.room.on("participant_disconnected")
    def on_participant_disconnected(disconnected: rtc.RemoteParticipant) -> None:
        if disconnected.identity == participant.identity:
            participant_left.set()

    @ctx.room.on("disconnected")
    def on_room_disconnected(*_args: object) -> None:
        participant_left.set()

    for publication in participant.track_publications.values():
        track = publication.track
        if isinstance(track, rtc.RemoteAudioTrack):
            task = asyncio.create_task(forward_audio(track))
            audio_tasks.add(task)
            task.add_done_callback(audio_tasks.discard)

    process_exited = asyncio.create_task(codex.process.wait())
    left = asyncio.create_task(participant_left.wait())
    try:
        done, pending = await asyncio.wait(
            {process_exited, left}, return_when=asyncio.FIRST_COMPLETED
        )
        if process_exited in done:
            logger.error("Codex app-server exited while the voice session was still live.")
            await ctx.room.local_participant.set_attributes({"jarvis.voice.status": "unavailable"})
        for task in pending:
            task.cancel()
        if pending:
            await asyncio.gather(*pending, return_exceptions=True)
    finally:
        duck_output()
        for task in turn_tasks:
            task.cancel()
        if turn_tasks:
            await asyncio.gather(*turn_tasks, return_exceptions=True)
        for task in audio_tasks:
            task.cancel()
        if audio_tasks:
            await asyncio.gather(*audio_tasks, return_exceptions=True)
        if playback_tasks:
            await asyncio.gather(*playback_tasks, return_exceptions=True)
        await codex.close()
        await http.aclose()
        await output_source.aclose()
        await ctx.room.disconnect()


if __name__ == "__main__":
    agents.cli.run_app(
        agents.WorkerOptions(
            entrypoint_fnc=_voice_entrypoint,
            agent_name="jarvis-voice",
            ws_url=os.getenv("LIVEKIT_URL"),
            api_key=os.getenv("LIVEKIT_API_KEY"),
            api_secret=os.getenv("LIVEKIT_API_SECRET"),
        )
    )
