from __future__ import annotations

import unittest

from playback_gate import VoicePlaybackGate


class VoicePlaybackGateTests(unittest.TestCase):
    def test_barge_in_drops_late_frames_until_next_speak(self) -> None:
        gate = VoicePlaybackGate()
        self.assertTrue(gate.try_start_speak())
        self.assertEqual(gate.accept_frame(), 0)

        gate.duck(suppress_inflight=True)
        self.assertIsNone(gate.accept_frame())
        self.assertFalse(gate.try_start_speak())
        self.assertIsNone(gate.accept_frame())

        gate.begin_turn()
        self.assertTrue(gate.try_start_speak())
        self.assertEqual(gate.accept_frame(), 1)

    def test_in_flight_speak_cannot_undo_barge_in(self) -> None:
        gate = VoicePlaybackGate()
        self.assertTrue(gate.try_start_speak())
        gate.duck(suppress_inflight=True)
        gate.finish_speak()
        self.assertFalse(gate.output_allowed)
        self.assertFalse(gate.try_start_speak())

    def test_replacement_duck_still_allows_same_turn_to_speak(self) -> None:
        gate = VoicePlaybackGate()
        gate.begin_turn()
        self.assertTrue(gate.try_start_speak())
        generation = gate.generation
        gate.duck()
        self.assertGreater(gate.generation, generation)
        self.assertIsNone(gate.accept_frame())
        self.assertTrue(gate.try_start_speak())
        self.assertEqual(gate.accept_frame(), gate.generation)

    def test_queued_generation_is_invalid_after_duck(self) -> None:
        gate = VoicePlaybackGate()
        self.assertTrue(gate.try_start_speak())
        stamped = gate.accept_frame()
        assert stamped is not None
        gate.duck(suppress_inflight=True)
        self.assertNotEqual(stamped, gate.generation)
        self.assertIsNone(gate.accept_frame())

    def test_successful_speak_keeps_output_open_for_late_frames(self) -> None:
        gate = VoicePlaybackGate()
        self.assertTrue(gate.try_start_speak())
        gate.finish_speak()
        self.assertEqual(gate.accept_frame(), 0)


if __name__ == "__main__":
    unittest.main()
