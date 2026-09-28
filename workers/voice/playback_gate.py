from __future__ import annotations


class VoicePlaybackGate:
    """Drop late Codex TTS until Jarvis starts the next utterance."""

    def __init__(self) -> None:
        self.generation = 0
        self.output_allowed = False
        self.suppress_speak = False

    def duck(self, *, suppress_inflight: bool = False) -> None:
        self.output_allowed = False
        self.generation += 1
        if suppress_inflight:
            self.suppress_speak = True

    def begin_turn(self) -> None:
        self.suppress_speak = False

    def allow_realtime_output(self) -> bool:
        if self.suppress_speak:
            return False
        self.output_allowed = True
        return True

    def try_start_speak(self) -> bool:
        if self.suppress_speak:
            return False
        self.output_allowed = True
        if self.suppress_speak:
            self.output_allowed = False
            return False
        return True

    def finish_speak(self) -> None:
        if self.suppress_speak:
            self.output_allowed = False

    def accept_frame(self) -> int | None:
        if not self.output_allowed:
            return None
        return self.generation
