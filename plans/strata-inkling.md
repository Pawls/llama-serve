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
> `stop-strata.ps1` see `strata-inkling.exe`, which runs from `%LOCALAPPDATA%\Strata\components\shapes\inkling2\`
> (S1 stop box, S3).
> **Update 2026-11-24:** Paul kept Inkling on Strata and moved to 0.2.21 (code and engines 0.1.67). Its server now
> handles effort itself, so the override is deleted (S4 done), and the installed config runs on GPU 1 alone, where
> the DSpark drafter works (S5 re-benches; the 0.2.20 numbers in S2 and the README no longer hold).
> **Next:** S6 (safe context ceiling without offload, target 368K), then S5 (Paul's night bench), then S3 (tray switch test)
> **Branch:** feat/strata-inkling-s1
> **Flow:** lean

- ~~[S1 — Engine 0.1.66 and the Inkling SQ-20 config](#s1)~~
- ~~[S2 — Bench against ninfer](#s2)~~
- [S3 — Keep it: tray, clients, docs](#s3)
- ~~[S4 — Retire the override when upstream ships its own](#s4)~~ (done 2026-11-24 with the 0.2.21 update)
- [S6 — Safe context ceiling without KV offload](#s6)
- [S5 — Re-bench Inkling on Strata 0.2.21](#s5)

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
  long session. Confirmed here 2026-11-24 (S1 findings): a 500-token budget let it think ~8,000 characters.
- **The override fails loudly** if Strata's run script stops naming `terminal\serve\server.py` (start-strata.ps1
  throws), and it patches nothing once `serve.inkling_dialect` has `server_effort` (upstream PR #6955) or no longer
  turns `high` into `none`; its `[inkling_thinking]` line in `logs\strata.log` says which.

### ~~S1 — Engine 0.1.66 and the Inkling SQ-20 config~~ · Opus 5.5 / medium {#s1}

**Status:** done 2026-11-24. Override and its tests landed 2026-11-23 (9/9 against Strata 0.2.20's dialect).
**Touches:** `F:\AI\strata\terminal` (engine, new config and run script; outside this repo), AGENTS.md
**After:** none
**Seam:** `start-strata.ps1 -Model <inkling id>` and the OpenAI API on :8411

- [x] Engine 0.1.66: the `engine_from_feed` snippet in AGENTS.md "To update"; `terminal\engine\BUILD.json` says
      0.1.66 and `engine\.previous` holds 0.1.59.
- [x] Swift still starts on 0.1.66: `.\start-strata.ps1`, then the `VRAM free with everything loaded` lines in
      `F:\AI\strata\terminal\strata-swift-iq3_s.log` (raise `--vram-reserve-later-mib` if one says LOW) and
      `.\test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411` passes. Stop it.
- [x] Inkling config: `setup.py --family inkling-2-mini --model SQ-20 --gpus 0,1 --no-start` from
      `F:\AI\strata\terminal` with the Strata venv, using the files above (`--models-dir F:\AI\Strata-data\models`,
      or `--gguf-dir` if setup asks for a folder). Record the config and run-script names it wrote; fix the port.
      Hand-add `before_load`, `sampling` (`temperature 1.0`, as the card says) and `idle_unload_s` like the Swift
      config, and `--max-context` as setup's largest offer.
- [x] Start it: `.\start-strata.ps1 -Model <its id>`; `logs\strata.log` shows
      `[inkling_thinking] patched serve.inkling_dialect`; `nvidia-smi` shows no card near 24,576 MiB; the start log's
      VRAM and host-KV lines are recorded.
- [x] Thinking works: a request with `reasoning_effort: "high"` returns non-empty `reasoning_content` and an
      answer; one with `"none"` returns no reasoning; one with no effort thinks (default `low`).
- [x] `.\test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411 -Model <model id>` passes; images (a red test picture
      named red) and JSON mode work.
- [x] Thinking cap: a request at `high` with `reasoning_budget_tokens: 500` and `max_tokens: 4000` on a hard
      problem; record whether the thinking stops near 500 or runs to `max_tokens` (PR #6869 says the latter).
- [x] `.\stop-strata.ps1` stops it: no `strata-inkling` process left, both cards back near idle.
- [x] Prompt reuse: two Pi turns in one conversation (Pi's `strata` provider replays `reasoning_content`); the
      second turn's `cached_tokens` covers the first prompt.

Cancel: if SQ-20 does not start on 0.1.66 after one config fix, record the error in README and stop the plan.

Findings (2026-11-24, engine 0.1.66, both cards undervolted as in AGENTS.md):

- Engine: 0.1.66 from the feed; `engine\.previous` holds 0.1.59. Swift: tool calls 3/3, no LOW (1,698 / 1,361
  MiB free), and its 21.53 GiB RAM copy pinned again.
- Setup wrote `strata-inkling-2-mini-sq-20.json` and `run-inkling-2-mini-sq-20.bat` (id `inkling-2-mini-sq-20`,
  `--gpus 2 --layer-split auto`, DSpark drafter on, port 8411 from `--port`, no `--open`). Its menu offers up to
  256K; `--context 1048576` was taken as asked. It fetched the `strata-inkling` engine (0.1.66, 417 MB) into
  `%LOCALAPPDATA%\Strata\components\shapes\inkling2\engine\`, outside the clone, so `stop-strata.ps1` and the
  tray now match that folder.
- Start at 1M: `[inkling_thinking] patched serve.inkling_dialect`; 11.8 / 11.2 GB used on GPU 0 / 1 (GPU 1 holds
  layers 0-10, GPU 0 layers 11-23, all experts in VRAM); 43.7 GB RAM free after load. The engine log prints no
  host-KV line, so whether the 1M cache is reserved at start or grows on use is for S2's long-context box.
- Thinking: `high` 141 chars of reasoning, `none` none, no effort 113 chars (default `low`); all answered 391.
  Red picture: "Red". JSON mode parsed. Tool calls 3/3 (median 4.8 s).
- Cap: `high` with `reasoning_budget_tokens: 500`, `max_tokens: 4000`, on x^2 + y^2 = 2025: ~8,000 characters of
  reasoning (far past 500 tokens), 3,927 tokens in all, finish `stop`, correct answer (27, 36). The budget does
  nothing, as PR #6869 says; `max_tokens` is the only cap.
- Stop: 1.5 s, no `strata-inkling` left, cards back to 908 / 0 MiB.
- Prompt reuse: Pi's second turn read 637 of 650 prompt tokens from cache (first prompt 571).
- **Ruling:** `sampling` carries only `temperature: 1.0` (the plan names no other value).
- **Ruling:** S3's `StrataSizes` row and the tray's engine folder shipped with S1 so Paul can try it before the
  bench; the tray-driven switch test and the clients stay in S3.

### ~~S2 — Bench against ninfer~~ · Opus 5.5 / medium {#s2}

**Status:** done 2026-11-24 (night); Paul's pick is open.
**Touches:** README.md (`## Inkling 2 Mini`), `logs\`
**After:** S1
**Seam:** `bench-depth.ps1 -BaseUrl`, `test-tool-calls.ps1`

- [x] bench-depth's thinking-off request decodes no reasoning under the override (check one reply's
      `reasoning_content` is empty); if not, send effort `none` for this bench.
- [x] `bench-depth.ps1 -BaseUrl http://127.0.0.1:8411 -Model <model id>` and the same on ninfer
      `inkling2-mini` (`-BaseUrl http://127.0.0.1:8412 -Model inkling-2-mini`): 2 rounds each, interleaved,
      settle before each, code and essay at 2k / 52k. Table of net tok/s and prompt reads in README.
- [x] Long context: a code hidden in a ~500k-token prompt (beyond ninfer's one-card 256K) is found; record read
      time, free RAM during it and whether the host KV pinned (start log).
- [x] One thinking sample at `high` on the same short coding prompt on both engines: tokens, time, answer correct.
      (The 50-task quality run is A3 of [context-and-quality-benches.md](./context-and-quality-benches.md).)
- [ ] README states the result and a recommendation for Paul; he picks keep or drop.

Findings (2026-11-24; numbers, method and logs in README.md § Inkling 2 Mini on Strata):

- Thinking-off box: `chat_template_kwargs.enable_thinking: false` gives empty `reasoning_content` (the override
  maps it to `none`). Strata ignores `ignore_eos`.
- Strata on both GPUs decodes ~56 / 48 tok/s at 2k / 51k against ninfer's ~116 / ~100, and reads prompts at
  1,150-1,300 against 2,020-2,190; the layer split is the main cost (engine bench: 78.6 on one GPU, 55.1 split).
- 1M with the cache in VRAM crashes at ~400k; `--kv-offload on` reaches 641k (needle found, 3,536 s) at 32 GB
  of RAM and 38-39 tok/s decode.
- **Ruling:** the installed config is now 256K without `--kv-offload` (`args_written` still records setup's 1M),
  and the tray label says 256K, so the tray item cannot crash on a long prompt.
- **Ruling:** the GPU 1-only config used for one arm was deleted after its round; README says how it was built.
- Recommendation in README: drop it for daily use and keep ninfer; Strata's edge is lossless weights and
  768K-1M context. If Paul drops it, S3 becomes: remove the `StrataSizes` row, the config, run script and
  `strata_overrides` (or keep the override until S4), and the files in `F:\AI\Strata-data\models\inkling-2-mini`.

### S3 — Keep it: tray, clients, docs · Sonnet 5.5 / medium {#s3}

**Status:** not started; only if Paul keeps it after S2.
**Touches:** `tray/LlamaTray.cs` (`StrataSizes`, `StrataEngineRunning`), `~\.pi\agent\models.json`,
`%APPDATA%\Code\User\chatLanguageModels.json`, Hermes `config.yaml`, `sync-opencode.ps1` run, AGENTS.md, README.md
**After:** S2
**Seam:** the tray menu and `install-tray.ps1`; each client's model list

- [ ] `StrataSizes` row for the Inkling id (row, folder and `install-tray.ps1` done 2026-11-24 with S1; the switch
      test below is not); confirm `StrataEngineRunning` sees the running `strata-inkling.exe`
      (it matches any path under `F:\AI\strata\`; narrow it to the folder S1 found); `install-tray.ps1`, then switch router → Strata Inkling → router once each
      way with the router model unloaded each time (AGENTS.md "Only one engine may hold the GPUs").
- [x] (done 2026-11-24, all four) Clients list the model id with its context and reasoning on: Pi (`strata` provider), VS Code (Strata group),
      Hermes, opencode (`.\sync-opencode.ps1`).
- [ ] AGENTS.md "Strata state" bullet for the Inkling config and the override.

### S6 — Find the safe context ceiling without `--kv-offload` · Sonnet 5.5 / medium {#s6}

**Status:** not started. Goal (Paul, 2026-11-24): the largest `--max-context` Strata can run Inkling at without
`--kv-offload on` and without crashing, searched between 256K and the known-bad ~400k; **368K is the target** (ninfer
ran 368K-384K, README.md § Inkling 2 Mini, "384K is near the ceiling").
**Known:** 0.2.20 on both GPUs at `--max-context 1048576` died at 401,920 tokens (`VRAM exhausted growing elastic
global KV cache`). The cache costs ~31 KB per token. On GPU 1 alone the start log already auto-offloads at 256K
(56k tokens in VRAM, ~205k mapped to host RAM, "Auto Host KV Offload"), so "no offload" there means ~56k: the
search is for the two-GPU split (drafter off, ~58 tok/s sample) and, secondarily, whether GPU 1 alone can be forced
to keep a larger window in VRAM. 0.2.21's long-conversation crash fix (#6905) and K/V reserve change may have
moved the ceiling; none of it is measured.
**Needs:** both GPUs idle (Paul stops Strata and the router model); method: needle prompt as in README.md
§ Inkling 2 Mini ("MAGENTA-7193-OTTER"), effort low, `max_tokens` set.
- [ ] Config copy `strata-inkling-2-mini-sq-20-split.json` with `--gpus 2 --layer-split auto` and no `"gpu"`;
      `--max-context` 262144, 327680, 368640, then bisect toward 400k.
- [ ] At each size: start log's `VRAM free with everything loaded` (LOW means too tight), a prompt filling ~90% of
      the context with the needle found, `nvidia-smi` peak per card, no spill (a card near 24,576 MiB).
- [ ] Record the largest size that passes twice, and the first that fails and how (README.md, dated, with engine).
- [ ] Set the installed config's `--max-context` only if Paul wants it; say which GPU layout it belongs to.

### S5 — Re-bench Inkling on Strata 0.2.21 · Sonnet 5.5 / medium {#s5}

**Status:** not started; Paul runs it at night. Installed 2026-11-24: code 0.2.21, engines 0.1.67 (Qwen engine and
`strata-inkling`), config on GPU 1 alone (`"gpu": 1`, no `--gpus`/`--layer-split`), 256K, DSpark on.
**Known before the bench (one quick sample each, greedy code prompt, 500 tokens, not a bench):** both GPUs 58 tok/s
with `draft_n` 0 (the drafter does nothing on the split; "DSpark took 0 MiB"); GPU 1 alone 104-107 tok/s, 369
drafted / 174 accepted, GPU 1 at 22.6 GiB with ~0.9 GiB free and 205k tokens of KV mapped in host RAM.
- [ ] `bench-depth.ps1` against ninfer's `inkling-2-mini` at 2k / 51k, 2 rounds interleaved, as in S2.
- [ ] Compare the split config against GPU 1 alone, and one slot against the app's higher counts only if a client
      sends parallel requests.
- [ ] Update README § Inkling 2 Mini on Strata and the keep/drop table.

### ~~S4 — Retire the override when upstream ships its own~~ · Sonnet 5.5 / low {#s4}

**Done 2026-11-24:** Strata 0.2.21's `serve/inkling_dialect.py` passes the request's effort through (default `low`,
`STRATA_INKLING_EFFORT`), returns `reasoning_content`, and has default thinking budgets, so the override and its
`start-strata.ps1` block were deleted. Checked on 0.2.21: `none` gives no thinking, `low` and `high` return
`reasoning_content` with `reasoning_tokens` counted.

(The slice as written:)

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
