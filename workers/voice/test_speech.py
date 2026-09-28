from __future__ import annotations

import unittest

from speech import BargeIn, SpeechSequencer, is_echo, resolve_voice


class VoiceSelectionTests(unittest.TestCase):
    def test_blank_voice_lets_the_installed_cli_choose(self) -> None:
        self.assertIsNone(resolve_voice(None, None))
        self.assertIsNone(resolve_voice("  ", "not a voice"))
        self.assertEqual(resolve_voice("Cove", None), "cove")
        self.assertEqual(resolve_voice(None, "Spruce"), "spruce")
        self.assertEqual(resolve_voice("arbor", "juniper"), "arbor")


class EchoAndBargeTests(unittest.TestCase):
    def test_playback_echo_matches_a_span_of_the_spoken_text(self) -> None:
        spoken = "I finished the calendar check for tomorrow morning."
        self.assertTrue(is_echo("finished the calendar check for tomorrow", spoken))
        self.assertFalse(is_echo("stop please", spoken))
        self.assertFalse(is_echo("hi", spoken))

    def test_barge_in_waits_and_ignores_echo(self) -> None:
        barge = BargeIn(hold_s=0.45)
        spoken = "I finished the calendar check for tomorrow morning."
        self.assertFalse(barge.consider("finished the calendar check", spoken, True, 0))
        self.assertFalse(barge.consider("What about the other meeting", spoken, True, 1))
        self.assertTrue(barge.consider("What about the other meeting tomorrow", spoken, True, 1.5))

    def test_barge_in_does_not_arm_while_idle(self) -> None:
        barge = BargeIn(hold_s=0.45)
        self.assertFalse(barge.consider("What about the other meeting", "", False, 2))


class SpeechSequencerTests(unittest.TestCase):
    def test_first_sentence_starts_and_the_rest_waits_for_audio(self) -> None:
        speech = SpeechSequencer()
        speech.begin()
        speech.add("I finished the calendar check for tomorrow morning. I can move the meeting.")
        first = speech.poll(0)
        self.assertEqual(first, "I finished the calendar check for tomorrow morning.")
        self.assertIsNone(speech.poll(0.2))
        speech.note_frame(24000, 0.2)
        self.assertIsNone(speech.poll(0.5))
        second = speech.poll(1.6)
        self.assertEqual(second, "I can move the meeting.")

    def test_a_burst_of_audio_does_not_release_the_next_utterance_early(self) -> None:
        speech = SpeechSequencer()
        speech.begin()
        speech.add("I finished the calendar check for tomorrow morning. Then I checked the rest.")
        self.assertIsNotNone(speech.poll(0))
        speech.note_frame(24000 * 4, 0.05)
        self.assertIsNone(speech.poll(1))
        self.assertIsNotNone(speech.poll(4.5))

    def test_final_text_is_spoken_even_without_punctuation(self) -> None:
        speech = SpeechSequencer()
        speech.begin()
        speech.add("On it")
        self.assertIsNone(speech.poll(0))
        speech.mark_final()
        self.assertEqual(speech.poll(0), "On it")

    def test_cancel_drops_unsent_speech(self) -> None:
        speech = SpeechSequencer()
        speech.begin()
        speech.add("I finished the calendar check for tomorrow morning. More follows after that.")
        self.assertIsNotNone(speech.poll(0))
        speech.cancel()
        self.assertTrue(speech.idle)
        self.assertIsNone(speech.poll(10))


if __name__ == "__main__":
    unittest.main()
