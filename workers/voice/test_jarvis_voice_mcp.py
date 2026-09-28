from __future__ import annotations

import json
import unittest
from unittest.mock import AsyncMock, patch

import httpx

import jarvis_voice_mcp


class JarvisVoiceMcpTests(unittest.IsolatedAsyncioTestCase):
    async def test_voice_turn_returns_the_persisted_answer_and_uses_fixed_owner_scope(self) -> None:
        received: list[httpx.Request] = []

        async def handler(request: httpx.Request) -> httpx.Response:
            received.append(request)
            return httpx.Response(200, text='{"type":"delta","text":"Hello"}\n'
                '{"type":"delta","text":" there."}\n'
                '{"type":"done","responseText":"Hello there."}\n')

        answer = await jarvis_voice_mcp.request_jarvis_turn(
            "  Hi Jarvis  ",
            transport=httpx.MockTransport(handler),
            environ={
                "JARVIS_INTERNAL_API_URL": "http://jarvis-api:5082/",
                "VOICE_WORKER_SECRET": "test-secret",
                "JARVIS_VOICE_CONVERSATION_ID": "conversation-id",
                "JARVIS_VOICE_OWNER_ID": "owner-id",
            },
        )

        self.assertEqual(answer, "Hello there.")
        self.assertEqual(received[0].url.path, "/api/v1/voice/internal/conversation-id/transcript")
        self.assertEqual(received[0].headers["X-Jarvis-Voice-Secret"], "test-secret")
        self.assertEqual(json.loads(received[0].content), {
            "ownerId": "owner-id", "transcript": "Hi Jarvis",
        })

    async def test_voice_turn_falls_back_to_streamed_deltas_when_done_text_is_missing(self) -> None:
        async def handler(_request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, text='{"type":"delta","text":"First "}\n'
                '{"type":"delta","text":"sentence."}\n{"type":"done"}\n')

        answer = await jarvis_voice_mcp.request_jarvis_turn(
            "Question",
            transport=httpx.MockTransport(handler),
            environ={
                "JARVIS_INTERNAL_API_URL": "http://jarvis-api:5082",
                "VOICE_WORKER_SECRET": "test-secret",
                "JARVIS_VOICE_CONVERSATION_ID": "conversation-id",
                "JARVIS_VOICE_OWNER_ID": "owner-id",
            },
        )

        self.assertEqual(answer, "First sentence.")

    async def test_tool_call_returns_safe_error_without_exposing_configuration(self) -> None:
        with patch.object(
            jarvis_voice_mcp,
            "request_jarvis_turn",
            AsyncMock(side_effect=RuntimeError("secret=test-secret")),
        ):
            response, stop = await jarvis_voice_mcp._handle({
                "jsonrpc": "2.0",
                "id": 5,
                "method": "tools/call",
                "params": {"name": jarvis_voice_mcp.TOOL_NAME,
                           "arguments": {"transcript": "Question"}},
            })

        self.assertFalse(stop)
        self.assertTrue(response["result"]["isError"])
        self.assertNotIn("test-secret", response["result"]["content"][0]["text"])

    async def test_tool_discovery_exposes_only_jarvis_voice_turn(self) -> None:
        response, stop = await jarvis_voice_mcp._handle({
            "jsonrpc": "2.0", "id": 4, "method": "tools/list",
        })

        self.assertFalse(stop)
        self.assertEqual([tool["name"] for tool in response["result"]["tools"]], [
            "jarvis_voice_turn",
        ])

    async def test_final_user_transcript_runs_jarvis_then_speaks_the_answer(self) -> None:
        spoken: list[str] = []
        phases: list[str] = []
        ducked: list[bool] = []
        began: list[bool] = []
        allowed: list[bool] = []

        async def speak(text: str) -> None:
            spoken.append(text)

        async def set_phase(value: str) -> None:
            phases.append(value)

        async def request_turn(transcript: str, **_kwargs: object) -> str:
            self.assertEqual(transcript, "What is on my calendar?")
            self.assertEqual(phases, ["thinking"])
            return "You have lunch at noon."

        answer = await jarvis_voice_mcp.handle_final_user_transcript(
            "  What is on my calendar?  ",
            spoken="",
            phase="listening",
            environ={"JARVIS_INTERNAL_API_URL": "http://jarvis-api:5082"},
            speak=speak,
            set_phase=set_phase,
            duck=lambda **kwargs: ducked.append(kwargs.get("suppress_inflight", False)),
            begin_turn=lambda: began.append(True),
            allow_output=lambda: allowed.append(True),
            request_turn=request_turn,
        )

        self.assertEqual(answer, "You have lunch at noon.")
        self.assertEqual(spoken, ["You have lunch at noon."])
        self.assertEqual(phases, ["thinking", "speaking"])
        self.assertEqual(ducked, [True])
        self.assertEqual(began, [True])
        self.assertEqual(allowed, [True])

    async def test_echo_of_assistant_speech_does_not_start_a_jarvis_turn(self) -> None:
        called: list[str] = []

        async def request_turn(transcript: str, **_kwargs: object) -> str:
            called.append(transcript)
            return "should not run"

        async def unused(_value: str) -> None:
            return None

        answer = await jarvis_voice_mcp.handle_final_user_transcript(
            "finished the calendar check for tomorrow",
            spoken="I finished the calendar check for tomorrow morning.",
            phase="speaking",
            environ={},
            speak=unused,
            set_phase=unused,
            duck=lambda **_kwargs: None,
            begin_turn=lambda: None,
            allow_output=lambda: None,
            request_turn=request_turn,
        )

        self.assertIsNone(answer)
        self.assertEqual(called, [])

    async def test_empty_transcript_is_rejected_before_network_call(self) -> None:
        with self.assertRaises(ValueError):
            await jarvis_voice_mcp.request_jarvis_turn(
                "  ",
                environ={
                    "JARVIS_INTERNAL_API_URL": "http://jarvis-api:5082",
                    "VOICE_WORKER_SECRET": "test-secret",
                    "JARVIS_VOICE_CONVERSATION_ID": "conversation-id",
                    "JARVIS_VOICE_OWNER_ID": "owner-id",
                },
            )


if __name__ == "__main__":
    unittest.main()
