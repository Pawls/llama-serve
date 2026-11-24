"""Tests for inkling_thinking.py against Strata's own serve/inkling_dialect.py.

Run with Strata's venv (it has the serve package's dependencies):
    F:\\AI\\strata\\terminal\\.venv\\Scripts\\python.exe -m unittest discover -s strata_overrides
"""
import importlib
import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import inkling_thinking  # noqa: E402

inkling_thinking.add_strata_to_path()
from serve import inkling_dialect as stock  # noqa: E402

THINK = "<|message_model|><|content_thinking|>"
TEXT = "<|message_model|><|content_text|>"
END = "<|end_message|>"


def effort_line(level: str) -> str:
    return f"<|message_system|><|content_text|>Thinking effort level: {level}{END}"


class PatchedDialect(unittest.TestCase):
    def setUp(self):
        os.environ.pop(inkling_thinking.ENV, None)
        self.d = importlib.reload(stock)
        self.assertTrue(inkling_thinking.apply(self.d))

    def test_effort_passes_through(self):
        self.assertEqual(self.d.normalize_effort("high"), "high")
        self.assertEqual(self.d.normalize_effort("xhigh"), "xhigh")
        self.assertEqual(self.d.normalize_effort(False), "none")
        self.assertEqual(self.d.normalize_effort(0.5), 0.5)

    def test_default_effort_is_low_unless_the_environment_names_one(self):
        self.assertEqual(self.d.normalize_effort(None), "low")
        os.environ[inkling_thinking.ENV] = "medium"
        try:
            self.assertEqual(self.d.normalize_effort(None), "medium")
        finally:
            os.environ.pop(inkling_thinking.ENV)

    def test_template_kwargs(self):
        self.assertEqual(self.d.template_kwargs({"reasoning_effort": "high"}),
                         {"reasoning_effort": "high", "enable_thinking": True})
        self.assertEqual(self.d.template_kwargs({"enable_thinking": False})["reasoning_effort"], "none")

    def test_finish_prompt_opens_only_the_turn_when_thinking(self):
        prompt = effort_line("0.9") + "<|message_user|><|content_text|>hi" + END
        self.assertEqual(self.d.finish_prompt(prompt), prompt + "<|message_model|>")
        self.assertEqual(self.d.finish_prompt(prompt, has_tools=True), prompt + "<|message_model|>")

    def test_finish_prompt_at_effort_none_is_stock(self):
        prompt = effort_line("0") + "<|message_user|><|content_text|>hi" + END
        self.assertEqual(self.d.finish_prompt(prompt), prompt + TEXT)

    def test_thinking_streams_as_reasoning(self):
        out = self.d.InklingDialect().feed(f"{THINK}abc{END}{TEXT}Hi{END}", final=True)
        self.assertEqual(out, "<think>abc</think>Hi")

    def test_quoted_end_message_inside_thinking_is_kept(self):
        chunks = self.d.InklingDialect().feed_channels(f"{THINK}a{END}b{END}{TEXT}x", final=True)
        reasoning = "".join(c for kind, c in chunks if kind == "reasoning")
        self.assertEqual(reasoning, f"a{END}b")

    def test_truncated_thinking_is_not_sent_twice(self):
        chunks = self.d.InklingDialect().feed_channels(f"{THINK}abc", final=True, reason="length")
        self.assertEqual([c for kind, c in chunks if kind == "reasoning"], ["abc"])


class UpstreamSupport(unittest.TestCase):
    def test_does_nothing_once_upstream_has_its_own_switch(self):
        d = importlib.reload(stock)
        d.server_effort = lambda: "none"
        before = d.normalize_effort
        self.assertFalse(inkling_thinking.apply(d))
        self.assertIs(d.normalize_effort, before)


if __name__ == "__main__":
    unittest.main()
