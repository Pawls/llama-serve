# Inkling 2 Mini on Strata 0.2.20

> **Why:** Strata 0.2.20 runs Inkling 2 Mini from a lossless 18 GiB pack (SQ-20: KL 0.00000 against the original,
> where ninfer's UD-IQ4_XS is lossy) and can keep the full-length KV cache in host RAM, so its own planner fits 1M
> of context on this PC where ninfer stops at 256K on one card and 768K on two. Its server forces reasoning effort
> `none` and drops the thinking (Niko1221/Strata#6612), so it runs through
> [strata_overrides/inkling_thinking.py](../strata_overrides/inkling_thinking.py), started by
> [start-strata.ps1](../start-strata.ps1) for any config with `"arch": "inkling2_loop"`. This plan measures whether
> it beats the ninfer setup ([README.md](../README.md#inkling-2-mini-measured-2026-11-21)).
> **Sign-off:** every slice's boxes checked with the logs named; numbers in README.md carry date, engine, file and
> the undervolt; Paul decides from S2's table whether Strata's Inkling stays.
> **Review focus:** a request with no effort from a client that sends none (VS Code, opencode) gets `low`, not
> `none` (S1); bench-depth's thinking-off request really decodes no thinking under the override (S2); the tray and
> `stop-strata.ps1` see `strata-inkling.exe`, which has not run here yet (S1 stop box, S3).
> **Next:** S1 — install engine 0.1.66 and the Inkling SQ-20 config, check it starts and thinks
> **Branch:** not cut yet
> **Flow:** lean

- [S1 — Engine 0.1.66 and the Inkling SQ-20 config](#s1)
- [S2 — Bench against ninfer](#s2)
- [S3 — Keep it: tray, clients, docs](#s3)
- [S4 — Retire the override when upstream ships its own](#s4)

## Traps

- **GPUs:** every slice but S4 loads models on both cards. Paul runs this at night, after he is done with the PC.
  Run `nvidia-smi` first; stop ninfer from the tray (it stops the router too) and leave the router stopped while
  Strata runs.
- **The engine's test start** (installing 0.1.66) loads an installed model and generates one token: it needs the
  GPUs free like any start.
- **Ports:** setup writes `run-*.bat` with port 8080 (signal-cli's in WSL) and may add `--open`; change it to
  `--port 8411` and drop `--open`.
- **`setup.py --update` rewrites UD-Q4_K_XL's `--prefill 2048` to `auto`** (AGENTS.md "To update"); check it after
  any setup run.
- **Files already in place:** `F:\AI\Strata-data\models\inkling-2-mini` holds `Inkling-2-Mini-SQ-20.strata` and
  `Inkling-2-Mini-Vision.strata` (SHA-256 checked against the repo's `SHA256SUMS.txt` on 2026-11-23) and the
  tokenizer files. Point setup at them; do not download again.
- **Only `max_tokens` stops a runaway think.** Upstream reports that `reasoning_budget_tokens` and `answer_room` do
  nothing for Inkling (PR #6869, open), and stock Strata has thought silently for ~100k tokens in one agent reply.
  Send a `max_tokens` with every bench and probe request, and give the Inkling model a `maxTokens` in Pi before a
  long session. Not yet seen on this PC; S1 checks it.
- **The override fails loudly** if Strata's run script stops naming `terminal\serve\server.py` (start-strata.ps1
  throws), and it patches nothing once `serve.inkling_dialect` has `server_effort` (upstream PR #6955) or no longer
  turns `high` into `none`; its `[inkling_thinking]` line in `logs\strata.log` says which.

### S1 — Engine 0.1.66 and the Inkling SQ-20 config · Opus 5.5 / medium {#s1}

**Status:** not started. Override and its tests landed 2026-11-23 (9/9 against Strata 0.2.20's dialect).
**Touches:** `F:\AI\strata\terminal` (engine, new config and run script; outside this repo), AGENTS.md
**After:** none
**Seam:** `start-strata.ps1 -Model <inkling id>` and the OpenAI API on :8411

- [ ] Engine 0.1.66: the `engine_from_feed` snippet in AGENTS.md "To update"; `terminal\engine\BUILD.json` says
      0.1.66 and `engine\.previous` holds 0.1.59.
- [ ] Swift still starts on 0.1.66: `.\start-strata.ps1`, then the `VRAM free with everything loaded` lines in
      `F:\AI\strata\terminal\strata-swift-iq3_s.log` (raise `--vram-reserve-later-mib` if one says LOW) and
      `.\test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411` passes. Stop it.
- [ ] Inkling config: `setup.py --family inkling-2-mini --model SQ-20 --gpus 0,1 --no-start` from
      `F:\AI\strata\terminal` with the Strata venv, using the files above (`--models-dir F:\AI\Strata-data\models`,
      or `--gguf-dir` if setup asks for a folder). Record the config and run-script names it wrote; fix the port.
      Hand-add `before_load`, `sampling` (`temperature 1.0`, as the card says) and `idle_unload_s` like the Swift
      config, and `--max-context` as setup's largest offer.
- [ ] Start it: `.\start-strata.ps1 -Model <its id>`; `logs\strata.log` shows
      `[inkling_thinking] patched serve.inkling_dialect`; `nvidia-smi` shows no card near 24,576 MiB; the start log's
      VRAM and host-KV lines are recorded.
- [ ] Thinking works: a request with `reasoning_effort: "high"` returns non-empty `reasoning_content` and an
      answer; one with `"none"` returns no reasoning; one with no effort thinks (default `low`).
- [ ] `.\test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411 -Model <model id>` passes; images (a red test picture
      named red) and JSON mode work.
- [ ] Thinking cap: a request at `high` with `reasoning_budget_tokens: 500` and `max_tokens: 4000` on a hard
      problem; record whether the thinking stops near 500 or runs to `max_tokens` (PR #6869 says the latter).
- [ ] `.\stop-strata.ps1` stops it: no `strata-inkling` process left, both cards back near idle.
- [ ] Prompt reuse: two Pi turns in one conversation (Pi's `strata` provider replays `reasoning_content`); the
      second turn's `cached_tokens` covers the first prompt.

Cancel: if SQ-20 does not start on 0.1.66 after one config fix, record the error in README and stop the plan.

### S2 — Bench against ninfer · Opus 5.5 / medium {#s2}

**Status:** not started.
**Touches:** README.md (`## Inkling 2 Mini`), `logs\`
**After:** S1
**Seam:** `bench-depth.ps1 -BaseUrl`, `test-tool-calls.ps1`

- [ ] bench-depth's thinking-off request decodes no reasoning under the override (check one reply's
      `reasoning_content` is empty); if not, send effort `none` for this bench.
- [ ] `bench-depth.ps1 -BaseUrl http://127.0.0.1:8411 -Model <model id>` and the same on ninfer
      `inkling2-mini` (`-BaseUrl http://127.0.0.1:8412 -Model inkling-2-mini`): 2 rounds each, interleaved,
      settle before each, code and essay at 2k / 52k. Table of net tok/s and prompt reads in README.
- [ ] Long context: a code hidden in a ~500k-token prompt (beyond ninfer's one-card 256K) is found; record read
      time, free RAM during it and whether the host KV pinned (start log).
- [ ] One thinking sample at `high` on the same short coding prompt on both engines: tokens, time, answer correct.
      (The 50-task quality run is A3 of [context-and-quality-benches.md](./context-and-quality-benches.md).)
- [ ] README states the result and a recommendation for Paul; he picks keep or drop.

### S3 — Keep it: tray, clients, docs · Sonnet 5.5 / medium {#s3}

**Status:** not started; only if Paul keeps it after S2.
**Touches:** `tray/LlamaTray.cs` (`StrataSizes`, `StrataEngineRunning`), `~\.pi\agent\models.json`,
`%APPDATA%\Code\User\chatLanguageModels.json`, Hermes `config.yaml`, `sync-opencode.ps1` run, AGENTS.md, README.md
**After:** S2
**Seam:** the tray menu and `install-tray.ps1`; each client's model list

- [ ] `StrataSizes` row for the Inkling id; confirm `StrataEngineRunning` sees the running `strata-inkling.exe`
      (it matches any path under `F:\AI\strata\`; narrow it to the folder S1 found); `install-tray.ps1`, then switch router → Strata Inkling → router once each
      way with the router model unloaded each time (AGENTS.md "Only one engine may hold the GPUs").
- [ ] Clients list the model id with its context and reasoning on: Pi (`strata` provider), VS Code (Strata group),
      Hermes, opencode (`.\sync-opencode.ps1`).
- [ ] AGENTS.md "Strata state" bullet for the Inkling config and the override.

### S4 — Retire the override when upstream ships its own · Sonnet 5.5 / low {#s4}

**Status:** waiting on upstream: PR #6955 (opt-in `STRATA_INKLING_EFFORT`) or any merged fix for #6612. Check with
`gh pr view 6955 -R Niko1221/Strata --json state` and after each `UPDATE.bat`.
**Touches:** `start-strata.ps1` (the Inkling block), `strata_overrides\`, the Inkling config's `env`
**After:** none
**Seam:** `start-strata.ps1 -Model <inkling id>`

- [ ] After the update, `logs\strata.log` says `upstream has its own switch; not patched`.
- [ ] Set the server's effort the upstream way (for #6955: `STRATA_INKLING_EFFORT=low` in the server's
      environment; check where that release reads it) and repeat S1's thinking box.
- [ ] Delete the Inkling block in `start-strata.ps1` and `strata_overrides\`; drop the override from README and
      this plan's header.

Cancel: if upstream's fix thinks but drops `reasoning_content` or forces `none` by default with no switch, keep the
override and record why.
