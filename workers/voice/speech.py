"""ChatGPT voice playback that does not cut an utterance off mid-sentence."""

from __future__ import annotations

import re

_VOICE_ID = re.compile(r"[a-z0-9_-]{1,32}")

VOICE_PROMPT = (
    "You are Jarvis's realtime voice interface. For each user turn, hand the request to Codex "
    "so it can use Jarvis's conversation, memory, and tools. Do not answer from your own "
    "knowledge while the handoff is running. Speak Codex's completed answer naturally and "
    "completely, without announcing the handoff or adding a second answer. Keep listening "
    "after each reply and allow the user to interrupt by speaking."
)

_TOKEN = re.compile(r"[a-z0-9]+")
_SENTENCE_END = re.compile(r"[.!?…](?:\s+|$)|\n+")


def sanitize_voice(value: str | None) -> str | None:
    """Keep a voice id the caller already chose. An empty value lets the Codex CLI use its own default."""
    if value is None:
        return None
    voice = value.strip().lower()
    if not voice or _VOICE_ID.fullmatch(voice) is None:
        return None
    return voice


def resolve_voice(metadata_voice: str | None, env_voice: str | None) -> str | None:
    return sanitize_voice(metadata_voice) or sanitize_voice(env_voice)


def _words(text: str) -> str:
    return " ".join(_TOKEN.findall(text.lower()))


def is_echo(heard: str, spoken: str) -> bool:
    """True when the microphone transcript is the voice model hearing itself."""
    heard_norm = _words(heard)
    spoken_norm = _words(spoken)
    if len(heard_norm) < 12 or not spoken_norm:
        return False
    return heard_norm in spoken_norm


class BargeIn:
    """Interrupt playback only after distinct speech has lasted a moment."""

    def __init__(self, hold_s: float = 0.45) -> None:
        self.hold_s = hold_s
        self._armed_at: float | None = None

    def reset(self) -> None:
        self._armed_at = None

    def consider(self, partial: str, spoken: str, assistant_busy: bool, now: float) -> bool:
        text = partial.strip()
        if not assistant_busy or len(text) < 12 or is_echo(text, spoken):
            self._armed_at = None
            return False
        if self._armed_at is None:
            self._armed_at = now
            return False
        return now - self._armed_at >= self.hold_s


def _split_index(text: str, *, min_chars: int, max_chars: int, first: bool) -> int | None:
    stripped = text.strip()
    if len(stripped) < min_chars:
        return None
    best: int | None = None
    for match in _SENTENCE_END.finditer(text):
        end = match.end()
        if len(text[:end].strip()) < min_chars:
            continue
        if end > max_chars:
            break
        best = end
        if first:
            return best
    if best is not None:
        return best
    if len(text) < max_chars:
        return None
    window = text[:max_chars]
    space = window.rfind(" ")
    if space >= min_chars:
        return space
    return max_chars


class SpeechSequencer:
    """Speak one ChatGPT utterance at a time, and only after the previous one finishes."""

    def __init__(self) -> None:
        self.buffer = ""
        self.final = False
        self.speaking = False
        self.spoken = ""
        self._audio_ends_at: float | None = None
        self._last_frame_at: float | None = None
        self._speak_started_at: float | None = None
        self._first = True

    @property
    def assistant_busy(self) -> bool:
        return self.speaking or bool(self.buffer.strip())

    @property
    def idle(self) -> bool:
        return not self.speaking and not self.buffer.strip()

    def begin(self) -> None:
        self.buffer = ""
        self.final = False
        self.speaking = False
        self.spoken = ""
        self._audio_ends_at = None
        self._last_frame_at = None
        self._speak_started_at = None
        self._first = True

    def cancel(self) -> None:
        self.buffer = ""
        self.final = True
        self.speaking = False
        self._audio_ends_at = None
        self._last_frame_at = None
        self._speak_started_at = None

    def add(self, text: str) -> None:
        if text:
            self.buffer += text

    def mark_final(self) -> None:
        self.final = True

    def note_frame(self, samples: int, now: float, sample_rate: int = 24000) -> None:
        if samples <= 0 or not self.speaking or sample_rate <= 0:
            return
        duration = samples / sample_rate
        if self._audio_ends_at is None or self._audio_ends_at < now:
            self._audio_ends_at = now
        self._audio_ends_at += duration
        self._last_frame_at = now

    def abandon_current(self) -> None:
        self.speaking = False
        self._audio_ends_at = None
        self._last_frame_at = None
        self._speak_started_at = None

    def poll(self, now: float) -> str | None:
        if self.speaking:
            if self._drained(now) or self._stalled(now):
                self.speaking = False
                self._audio_ends_at = None
                self._last_frame_at = None
                self._speak_started_at = None
            else:
                return None
        utterance = self._take()
        if not utterance:
            return None
        self.speaking = True
        self._speak_started_at = now
        self._audio_ends_at = None
        self._last_frame_at = None
        self.spoken = f"{self.spoken} {utterance}".strip()
        self._first = False
        return utterance

    def _stalled(self, now: float) -> bool:
        if self._speak_started_at is None or self._last_frame_at is not None:
            return False
        return now - self._speak_started_at >= 8

    def _drained(self, now: float) -> bool:
        if self._audio_ends_at is None or self._last_frame_at is None:
            return False
        return now >= self._audio_ends_at + 0.12 and now - self._last_frame_at >= 0.25

    def _take(self) -> str | None:
        text = self.buffer
        if not text.strip():
            return None
        if self.final:
            self.buffer = ""
            return text.strip()
        # The first utterance starts at a sentence. Later ones wait until playback is
        # idle, then take every complete sentence already buffered.
        index = _split_index(
            text,
            min_chars=24 if self._first else 1,
            max_chars=280 if self._first else 700,
            first=self._first,
        )
        if index is None:
            return None
        chunk = text[:index].strip()
        self.buffer = text[index:].lstrip()
        return chunk or None
