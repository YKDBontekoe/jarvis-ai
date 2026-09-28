"""Small, owner-scoped MCP bridge from Codex voice handoffs to Jarvis turns."""

from __future__ import annotations

import asyncio
import json
import os
import sys
from collections.abc import Mapping
from typing import Any

import httpx

TOOL_NAME = "jarvis_voice_turn"
MAX_TRANSCRIPT_LENGTH = 32_000


async def request_jarvis_turn(
    transcript: str,
    *,
    transport: httpx.AsyncBaseTransport | None = None,
    environ: Mapping[str, str] | None = None,
) -> str:
    """Run a spoken turn through Jarvis and return the persisted assistant answer."""
    env = os.environ if environ is None else environ
    transcript = transcript.strip()
    if not transcript or len(transcript) > MAX_TRANSCRIPT_LENGTH:
        raise ValueError("The spoken request must contain 1 to 32,000 characters.")

    api_url = env.get("JARVIS_INTERNAL_API_URL", "").rstrip("/")
    secret = env.get("VOICE_WORKER_SECRET", "")
    conversation_id = env.get("JARVIS_VOICE_CONVERSATION_ID", "")
    owner_id = env.get("JARVIS_VOICE_OWNER_ID", "")
    if not all((api_url, secret, conversation_id, owner_id)):
        raise RuntimeError("The Jarvis voice tool is missing its server configuration.")

    url = f"{api_url}/api/v1/voice/internal/{conversation_id}/transcript"
    headers = {"X-Jarvis-Voice-Secret": secret}
    payload = {"ownerId": owner_id, "transcript": transcript}
    chunks: list[str] = []
    final_text: str | None = None
    async with httpx.AsyncClient(
        timeout=httpx.Timeout(1200, connect=5), transport=transport
    ) as client:
        async with client.stream("POST", url, headers=headers, json=payload) as response:
            response.raise_for_status()
            async for line in response.aiter_lines():
                if not line:
                    continue
                event = json.loads(line)
                if event.get("type") == "delta":
                    delta = event.get("text")
                    if isinstance(delta, str):
                        chunks.append(delta)
                elif event.get("type") == "done":
                    text = event.get("responseText")
                    if isinstance(text, str):
                        final_text = text

    answer = final_text if final_text and final_text.strip() else "".join(chunks).strip()
    if not answer:
        raise RuntimeError("Jarvis returned no answer for the spoken request.")
    return answer


def _tool_result(text: str, *, is_error: bool = False) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": text}], "isError": is_error}


async def _handle(message: dict[str, Any]) -> tuple[dict[str, Any] | None, bool]:
    request_id = message.get("id")
    method = message.get("method")
    if not isinstance(method, str):
        return None, False
    if request_id is None:
        return None, method == "exit"

    if method == "initialize":
        result: dict[str, Any] = {
            "protocolVersion": "2024-11-05",
            "capabilities": {"tools": {}},
            "serverInfo": {"name": "jarvis-voice", "version": "1.0.0"},
        }
    elif method == "ping":
        result = {}
    elif method == "tools/list":
        result = {
            "tools": [{
                "name": TOOL_NAME,
                "description": (
                    "Process one exact voice transcript through the authenticated Jarvis "
                    "conversation, including its memory, tools, and approval rules."
                ),
                "inputSchema": {
                    "type": "object",
                    "properties": {"transcript": {"type": "string", "minLength": 1}},
                    "required": ["transcript"],
                    "additionalProperties": False,
                },
            }]
        }
    elif method == "tools/call":
        params = message.get("params")
        params = params if isinstance(params, dict) else {}
        arguments = params.get("arguments")
        arguments = arguments if isinstance(arguments, dict) else {}
        transcript = arguments.get("transcript")
        if params.get("name") != TOOL_NAME or not isinstance(transcript, str):
            result = _tool_result("This Jarvis voice tool call is invalid.", is_error=True)
        else:
            try:
                answer = await request_jarvis_turn(transcript)
                result = _tool_result(answer)
            except Exception:
                # The API owns the detailed failure logs. Never return credentials or raw errors to the model.
                result = _tool_result("Jarvis could not complete this request. Check the Jarvis app.", is_error=True)
    elif method == "shutdown":
        result = {}
    else:
        return ({"jsonrpc": "2.0", "id": request_id,
                 "error": {"code": -32601, "message": "Method not found"}}, False)

    return ({"jsonrpc": "2.0", "id": request_id, "result": result}, method == "shutdown")


async def serve() -> None:
    while True:
        line = await asyncio.to_thread(sys.stdin.readline)
        if not line:
            return
        try:
            message = json.loads(line)
            if not isinstance(message, dict):
                continue
            response, stop = await _handle(message)
            if response is not None:
                sys.stdout.write(json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n")
                sys.stdout.flush()
            if stop:
                return
        except Exception:
            request_id = locals().get("message", {}).get("id") if isinstance(locals().get("message"), dict) else None
            if request_id is not None:
                sys.stdout.write(json.dumps({
                    "jsonrpc": "2.0", "id": request_id,
                    "error": {"code": -32603, "message": "Internal error"},
                }) + "\n")
                sys.stdout.flush()


if __name__ == "__main__":
    asyncio.run(serve())
