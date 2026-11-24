"""Lets Inkling 2 Mini think under Strata's server, then runs that server.

Strata 0.2.20's serve/inkling_dialect.py forces reasoning_effort "none" and drops the model's thinking span
(Niko1221/Strata#6612; README.md § Inkling 2 Mini on Strata). This patches that module in memory, so the Strata
clone stays untouched and UPDATE.bat keeps working:
  - a request's reasoning_effort reaches the chat template; a request without one gets $env:STRATA_INKLING_EFFORT,
    else "low";
  - the thinking streams back as reasoning_content (Pi replays it, so the next turn reuses the prompt cache);
  - above effort 0 the prompt ends at the bare turn opener and the model writes its own thinking tag.
The env var's name and meaning follow upstream PR #6955. Once the dialect has its own switch (server_effort), this
patches nothing and the server runs as shipped.

    <Strata venv python> inkling_thinking.py <server.py's arguments>
"""
from __future__ import annotations

import os
import runpy
import sys
from pathlib import Path

ENV = "STRATA_INKLING_EFFORT"
DEFAULT_EFFORT = "low"
STRATA = Path(r"F:\AI\strata\terminal")   # strata-dir.ps1's $StrataDir
WORDS = ("none", "minimal", "low", "medium", "high", "xhigh", "max")   # the keys of chat_template.jinja's effort_map
EFFORT_PREFIX = "<|message_system|><|content_text|>Thinking effort level: "


def add_strata_to_path() -> None:
    if str(STRATA) not in sys.path:
        sys.path.insert(0, str(STRATA))


def apply(dialect) -> bool:
    """Patch Strata's serve.inkling_dialect module in place. False when upstream already has its own switch."""
    if hasattr(dialect, "server_effort"):
        return False
    stock_finish_prompt = dialect.finish_prompt

    def server_default():
        level = os.environ.get(ENV, "").strip().lower()
        return level if level in WORDS else DEFAULT_EFFORT

    def normalize_effort(value=None):
        if value is None or value == "" or value is True:
            return server_default()
        if value is False:
            return "none"
        key = str(value).strip().lower()
        if key in dialect.EFFORT:
            return dialect.EFFORT[key]
        try:
            number = float(key)
        except ValueError:
            return server_default()
        return "none" if number <= 0.0 else min(number, 0.99)

    def template_kwargs(kwargs: dict) -> dict:
        out = dict(kwargs)
        out.pop("thinking_mode", None)
        wanted = "none" if out.get("enable_thinking") is False else out.get("reasoning_effort")
        effort = normalize_effort(wanted)
        out["reasoning_effort"] = effort
        out["enable_thinking"] = effort != "none"
        return out

    def finish_prompt(prompt: str, *, has_tools: bool = False, forced: bool = False) -> str:
        at = prompt.find(EFFORT_PREFIX) if prompt else -1
        thinking = at >= 0 and not prompt.startswith("0" + dialect.END_MSG, at + len(EFFORT_PREFIX))
        if not thinking or forced:
            return stock_finish_prompt(prompt, has_tools=has_tools, forced=forced)
        return prompt if prompt.endswith(dialect.MSG_MODEL) else prompt + dialect.MSG_MODEL

    # The dialect collects thinking in _think_acc and drops it when the channel closes; while one of these two methods
    # runs, _append_think sends it to that call's output list instead.
    cls = dialect.InklingDialect
    stock_emit, stock_handle, stock_append = cls._emit_typed, cls._handle_tag_typed, cls._append_think

    def streaming(stock):
        def method(self, arg, out):
            self._reasoning_out = out
            try:
                return stock(self, arg, out)
            finally:
                self._reasoning_out = None
        return method

    def append_think(self, chunk: str) -> None:
        out = getattr(self, "_reasoning_out", None)
        if out is None:
            stock_append(self, chunk)
        elif chunk:
            out.append(("reasoning", chunk))

    for word in WORDS[1:]:
        dialect.EFFORT.setdefault(word, word)
    dialect.normalize_effort = normalize_effort
    dialect.template_kwargs = template_kwargs
    dialect.finish_prompt = finish_prompt
    cls._emit_typed = streaming(stock_emit)
    cls._handle_tag_typed = streaming(stock_handle)
    cls._append_think = append_think
    return True


def main() -> None:
    add_strata_to_path()
    from serve import inkling_dialect
    patched = apply(inkling_dialect)
    print(f"[inkling_thinking] {'patched serve.inkling_dialect' if patched else 'upstream has its own switch; not patched'}",
          flush=True)
    server = STRATA / "serve" / "server.py"
    sys.argv = [str(server), *sys.argv[1:]]
    runpy.run_path(str(server), run_name="__main__")


if __name__ == "__main__":
    main()
