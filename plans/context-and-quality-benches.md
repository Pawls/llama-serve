# Plan: more context on three models, and Inkling 2 Mini's answer quality

> **Why:** on 2026-11-23 Inkling 2 Mini went from 128K to 256K on one card and 768K on two, because ninfer-all
> 2026-11-23.9 halved its KV cache (MXFP8) and the two-GPU split moved experts off the cache card
> ([README.md](../README.md#ninfer-all-2026-11-239-mxfp8-kv-cache-measured-2026-11-23)). This plan checks where
> the same idea gains context elsewhere, and answers a question nothing here has measured yet: is Inkling 2 Mini as
> good as the models it would replace? Each slice is one bench session that ends in a keep-or-drop call.
>
> **Done means:** every slice has its numbers in README.md (dated, with the hardware and the undervolt stated, per
> [AGENTS.md](../AGENTS.md#working-in-this-repo)), each adopted change is live in its launch file and in every
> client's context setting, and each dropped one says why.
>
> **Review focus:** a context that loads but spills under WDDM (A1, A2: peak `nvidia-smi` box) · client configs
> left at the old context (A1, A2: client box) · a KV format that passes the needle but degrades answers (A1, A2:
> quality box) · a task set too easy to separate two models (A3: saturation box) · an artifact download nobody
> approved (A4: ask box).
>
> **Next:** A1, `qwen3.8-27b-single` at 262K on GPU 1
>
> **Branch:** per slice, `bench/<slice id>-<topic>` off `main` (e.g. `bench/a1-qwen-single-262k`), one PR each

- [A1 — `qwen3.8-27b-single` at 262K on GPU 1](#a1)
- [A2 — Inkling 2 Mini with the `rk8v4` cache: more context in the same memory](#a2)
- [A3 — Inkling 2 Mini's answer quality on the 50-task set](#a3)
- [A4 — Qwen3.8-27B on ninfer-all](#a4)

## Traps

- **Paul starts each slice by hand.** AGENTS.md forbids starting Strata, loading a router model or stopping the
  router unprompted; a session running this plan has that prompt, but it must still run `nvidia-smi` first and
  stop if anything else (ComfyUI, WSL's club-3090 stack, a game) holds either GPU.
- **Every slice ties up the GPUs** (one lane): run them one at a time, in order.
- **A context change touches four places:** the launch file (`presets.ini` or `start-ninfer.ps1`), Pi's
  `~\.pi\agent\models.json` (`contextWindow`; the file holds an API key, never print it), VS Code's
  `%APPDATA%\Code\User\chatLanguageModels.json` (`maxInputTokens` = context − 32,768, as the other entries do),
  and Hermes's `%LOCALAPPDATA%\hermes\config.yaml` (`context_length`). The tray's labels in
  [tray/LlamaTray.cs](../tray/LlamaTray.cs) name the ninfer contexts; after editing, run `install-tray.ps1`
  (it restarts Paul's tray, so say so).
- **`bench-depth.ps1` takes arrays only through `pwsh -c "& ./bench-depth.ps1 ... -Depth 2000,51000"`**:
  through `pwsh -File` an array collapses into one number.
- **The needle script lived only in a session scratchpad.** A1 commits it as `bench-needle.py` (text below);
  later slices use that file.
- **The 2026-11-23 needle used ~24.9 tokens an entry on Inkling's tokenizer.** Qwen's differs: run once with a
  small count, read `prompt_tokens` and scale.

The needle script for A1 to commit (Python 3, standard library only):

```python
"""Hide a code in a long prompt and ask for it back. usage: bench-needle.py <base url> <entries> <model id>"""
import json, random, sys, time, urllib.request, urllib.error

url, n_entries, model = sys.argv[1], int(sys.argv[2]), sys.argv[3]
rng = random.Random(7)
words = ("river market signal copper lantern harvest orbit ledger meadow engine canyon velvet archive thunder "
         "pilgrim quartz harbor falcon garden mirror").split()
lines = [f"Entry {i}: " + " ".join(rng.choice(words) for _ in range(12)) + "." for i in range(n_entries)]
lines.insert(int(n_entries * 0.37), "Note: the vault access code is MAGENTA-7193-OTTER.")
prompt = "\n".join(lines) + "\n\nWhat is the vault access code mentioned in the notes above? Reply with the code only."
body = {"model": model, "max_tokens": 2048, "messages": [{"role": "user", "content": prompt}], "reasoning_effort": "low"}
req = urllib.request.Request(url + "/v1/chat/completions", json.dumps(body).encode(), {"Content-Type": "application/json"})
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))  # the system proxy hangs long requests
start = time.time()
try:
    reply = json.load(opener.open(req, timeout=3600))
except urllib.error.HTTPError as e:
    sys.exit(f"HTTP {e.code} {e.read()[:500]!r}")
content = reply["choices"][0]["message"].get("content") or ""
print("secs", round(time.time() - start, 1), "prompt_tokens", reply.get("usage", {}).get("prompt_tokens"))
print("found" if "MAGENTA-7193-OTTER" in content else "MISSED", repr(content[:200]))
sys.exit(0 if "MAGENTA-7193-OTTER" in content else 1)
```

### A1 — `qwen3.8-27b-single` at 262K on GPU 1 · Sonnet 5.5 / medium {#a1}

**Status:** not started. The preset runs 131,072 tokens at q8_0 KV with 2.9 GiB of GPU 1 left (measured
2026-10-31, peak 21,655 MiB); 262,144 was never tried. It matters for the Reserve GPU 0 toggle, which leaves only
`CUDA1` presets.

**Touches:** `presets.ini` (`[qwen3.8-27b-single]`), new `bench-needle.py`, README.md, the four client configs
(see Traps), GPU 1 · **After:** none · **Seam:** the router's `/v1/chat/completions` on :8410

- [ ] `nvidia-smi` shows both GPUs idle; `bench-needle.py` committed from the text in Traps.
- [ ] Arm 1: `ctx-size = 262144`, KV unchanged (q8_0). Restart the router, load the preset with one short
      request. Pass: `Select-String logs\server.log 'retrying without pipeline'` finds nothing, and GPU 1 is at or
      under 23,300 MiB after load.
- [ ] Only if arm 1 fails: arm 2, `cache-type-v = q4_0` in this preset (K stays q8_0: keys are the more
      sensitive half). Same pass test. If arm 2 also fails, stop: restore 131072, record both peaks in README.md,
      strike the slice as dropped.
- [ ] On the arm that passed: `bench-depth.ps1 -Model qwen3.8-27b-single -Depth 2000,52000,128000`; then
      `bench-needle.py` at the largest entry count whose `prompt_tokens` stays under 258,000. Pass: found, and peak
      GPU 1 at or under 23,500 MiB during both (read `nvidia-smi` while the needle runs).
- [ ] Quality, arm 2 only: `bench-thinking.ps1 -Model qwen3.8-27b-single -Arms off -Effort medium` on both
      sets, once at the passing arm and once at 131072 / q8_0. Keep arm 2 only if it solves no more than one task
      fewer in total.
- [ ] `test-tool-calls.ps1 -Model qwen3.8-27b-single` exits 0.
- [ ] Adopted: the preset's comment and the four client configs carry the new context; README.md's Reserve GPU 0
      numbers and AGENTS.md's "Reserve GPU 0" bullet (which quotes 131k) say 262k. Dropped: README.md says why.
- [ ] PR opened and its review answered.

**Strike when:** the context is adopted or dropped with its numbers in README.md.

### A2 — Inkling 2 Mini with the `rk8v4` cache: more context in the same memory · Sonnet 5.5 / medium {#a2}

**Status:** not started. ninfer-all's own table
([docs/inkling2.md § KV cache](https://github.com/iamwavecut/ninfer-all/blob/master/docs/inkling2.md)) measures
`rk8v4` at 12.75 KiB a token and mean NLL +0.013% over 67,977 WikiText tokens, against MXFP8's 16.5 KiB and
+0.024%: smaller and no worse there. MXFP8 stays the default because the release was trained with it, and WikiText
NLL says nothing about retrieval at depth, so the needle and the tool calls decide.

- **Note (from the 2026-11-23 session):** 1M on two GPUs with `rk8v4` and split 1,7 started with 282 MiB free,
  too tight. The current profiles start with 536 MiB free (one card, 256K) and 1.02 GiB free (two cards, 768K).
  Arithmetic, not measured: the memory 256K takes at MXFP8 holds ~330K at `rk8v4`, and 768K's holds ~990K.

**Touches:** `start-ninfer.ps1` (`$Inkling` table), [tray/LlamaTray.cs](../tray/LlamaTray.cs) labels, README.md,
the four client configs, GPU 1 and GPU 0 · **After:** A1 (for `bench-needle.py`) · **Seam:** ninfer-serve's
`/v1/chat/completions` on :8412

- [ ] One card: start by hand with `--kv-dtype rk8v4` at `--max-context` 344064, then 327680 if that fails
      (same flags as `start-ninfer.ps1`'s `inkling2-mini` profile otherwise). Take the largest that starts with
      at least 500 MiB free in the start log.
- [ ] Two cards (`--devices 1,0 --tensor-parallel --tensor-split 1,7`): 983040, then 917504. Take the largest
      that starts with at least 1.0 GiB free.
- [ ] For each passing size: `bench-needle.py` at the largest entry count under its context minus 4,000 tokens.
      Pass: found, and GPU 1 peaks at or under 23,500 MiB.
- [ ] Quality on each passing size: `test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8412 -Model <id>` 3/3, and
      `bench-depth.ps1 -BaseUrl http://127.0.0.1:8412 -Model <id> -Depth 2000,51000` within 5% of the README's
      MXFP8 rows (a slower cache format costs context its speed).
- [ ] Decision for Paul, as a hitl `choose` task with the numbers: adopt `rk8v4` for one profile, both, or
      neither. The cost is a cache format the model was not trained with.
- [ ] Adopted: `start-ninfer.ps1` passes `--kv-dtype rk8v4` for those profiles with the new contexts; tray labels
      and the four client configs updated; each profile restarted through `start-ninfer.ps1` and its tool calls
      rechecked. Dropped: README.md records the numbers and the reason.
- [ ] PR opened and its review answered.

**Strike when:** Paul's call is recorded and, if adopted, live.

### A3 — Inkling 2 Mini's answer quality on the 50-task set · Sonnet 5.5 / medium {#a3}

**Status:** not started. No quality comparison of Inkling has run here. Swift 1.5 IQ3_S set the bar on
2026-11-13: 90/90 math and 60/60 code over three rounds at xhigh, 948 s per 50-task round, median 347 thinking
tokens ([README.md](../README.md#swift-15-iq3_s-against-unsloth-31-ud-iq3_xxs-measured-2026-11-13)).

**Touches:** `bench-thinking.ps1` (only if the effort check below fails), README.md, GPU 1 · **After:** none (A2
changes only context; run A3 on the profile `start-ninfer.ps1` serves at the time and name it) ·
**Seam:** `bench-thinking.ps1` against :8412

- [ ] Effort reaches the model: `bench-thinking.ps1 -Model inkling-2-mini -BaseUrl http://127.0.0.1:8412 -Arms
      off -Set math -Limit 2` once at `-Effort low` and once at `xhigh`. Pass: `reasoning_content` is non-empty
      and the xhigh median thinking tokens are higher. The script sends effort only as
      `chat_template_kwargs.reasoning_effort` (line 104); if this fails, also send a top-level
      `reasoning_effort = $Effort` in the same body (ninfer-all reads that one), commit, and rerun the check.
- [ ] Three rounds of `bench-thinking.ps1 -Model inkling-2-mini -BaseUrl http://127.0.0.1:8412 -Arms off
      -Effort xhigh` (20 HumanEval, 30 MATH, 32,768-token cap), the server restarted before each round, then one
      short warm-up reply.
- [ ] README.md gets a row in Swift's table shape: solved math / code, wall s per task and per round, thinking
      tokens, with the GPU, profile, undervolt and date.
- [ ] Saturation: if Inkling also solves all 90 math and 60 code over the three rounds, README.md says the set cannot separate the two
      and that only time and thinking tokens compare. Proposing a harder set is a new plan, not this slice.
- [ ] PR opened (only if `bench-thinking.ps1` changed; otherwise commit README.md to `main`).

**Strike when:** the row is in README.md.

### A4 — Qwen3.8-27B on ninfer-all · Opus 5.5 / medium {#a4}

**Status:** not started. ninfer-3090 v0.18.0 serves it on GPU 1 at 188K with a 4-bit (`rk4v4`) cache, 22.1 GiB.
ninfer-all also lists Qwen3.8-27B; it is untested here. The open questions are whether ninfer-all loads the
existing `F:\AI\ninfer-3090\models\qwen3_8_27b.ninfer` (17.7 GB, a pre-v3 artifact), and whether its
`--tensor-parallel` works on a dense model at all (its help describes splitting routed experts).

**Touches:** `start-ninfer.ps1` (the default `qwen38-27b` profile), README.md, the four client configs, GPU 1 and
GPU 0 · **After:** A1 (for `bench-needle.py`) · **Seam:** ninfer-serve's `/v1/chat/completions` on :8412

- [ ] Read ninfer-all's docs for Qwen3.8-27B: artifact version, `--kv-dtype` choices, whether two GPUs apply.
      Record the answers under this slice's Status.
- [ ] If it needs a new artifact download: ask Paul first (hitl `choose`, with size and disk free on F:). If he
      declines, strike the slice as dropped.
- [ ] One card on ninfer-all: the largest `--max-context` up to 262144 with the cache format that ninfer-all's
      table rates best, starting with at least 500 MiB free. Then `bench-depth.ps1 -BaseUrl
      http://127.0.0.1:8412 -Depth 2000,52000,128000` against v0.18.0's README numbers (204 / 171 / 146 code on
      2026-10-18).
- [ ] Two cards, only if the docs say a dense model splits: 262144 with an 8-bit cache, same checks.
- [ ] For the arm kept: `bench-needle.py` near full context found, `test-tool-calls.ps1 -BaseUrl
      http://127.0.0.1:8412` 3/3, JSON mode parses.
- [ ] Keep v0.18.0 unless ninfer-all gives more context at no more than 5% lower decode; Paul decides (hitl
      `choose`). Adopted: `start-ninfer.ps1`, client configs and AGENTS.md's ninfer bullet updated, v0.18.0 kept
      for rollback. Dropped: README.md says why.
- [ ] PR opened and its review answered.

**Strike when:** the call is recorded and, if adopted, live.
