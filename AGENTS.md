# AGENTS.md

Read this first. It covers what this repo is, how the pieces fit, what to check before touching anything, and where
everything lives. [README.md](./README.md) is the user-facing manual (model tables, measured numbers, everyday use) and
is the source of truth for those; this file does not repeat them.

## What this repo is

`llama-serve` is the control layer for running local LLMs on Paul's PC and handing them to coding agents. It holds
configs, launch scripts and a tray app. It does not contain any inference engine or model. Two engines sit behind it:

| | The router | Strata |
|---|---|---|
| What | llama.cpp's `llama-server` in router mode, serving the models listed in [presets.ini](./presets.ini) | A separate engine for one model family, Qwen3.8-Flash-Next (125B MoE) and its Coder, Swift 1.5 and Unsloth 4-bit variants |
| Port | `http://127.0.0.1:8410` | `http://127.0.0.1:8411` |
| Where it lives | `F:\AI\llama.cpp\b10951\llama-server.exe` (pinned build, set by `$Build` in [start-router.ps1](./start-router.ps1)) | `F:\AI\strata` (engine and server), `F:\AI\Strata-data` (`models\`, `packs\`, `mtp\`) |
| Source | [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp) release zips | [github.com/Niko1221/Strata](https://github.com/Niko1221/Strata) |
| Loading | On demand, one model at a time (`--models-max 1`), 15-60 s | One model, ~45-90 s, ~39 GB RAM pinned |
| Autostart | Yes, through the tray | No, on purpose (see Strata below) |

**Strata is not this repo and not llama.cpp.** `F:\AI\strata` is a clone of the upstream repo (remote
`Niko1221/Strata`) with the user's generated configs beside it. Never commit there, and do not edit its source. This repo
only launches it ([start-strata.ps1](./start-strata.ps1), [stop-strata.ps1](./stop-strata.ps1)) and coordinates GPU use
with the router. Strata ships its own agent docs: `F:\AI\strata\AGENTS.md`, `docs\TROUBLESHOOTING.md`,
`docs\DETAILS.md` (engine internals, every setting), `docs\MULTI_GPU.md`, `docs\AI_SETUP.md`.

Clients that consume these servers (none of their configs are in this repo):

- **Pi** (`pi`): finds the router through the user environment variable `LLAMA_BASE_URL=http://127.0.0.1:8410/v1`,
  using Pi's built-in `llama.cpp` provider (not the `huggingface/pi-llama` package, which resets model overrides after
  the first request). Effort for the Qwen3.8 presets comes from `modelOverrides` under `llama.cpp` in
  `~\.pi\agent\models.json`; a new Qwen3.8 preset needs an entry there. Strata is the `strata` provider in the same
  file. That file holds an API key field; do not print it.
- **Hermes**: `hermes --provider strata`.
- **VS Code chat**: `%APPDATA%\Code\User\chatLanguageModels.json`, generated from `presets.ini`. Adding a preset means
  adding an entry there too.
- llama.cpp's own chat page at `http://127.0.0.1:8410/`.

## The machine

- 2x RTX 3090 (24 GB each), no NVLink (Paul declined it; do not suggest it), Intel i5-12600K, 64 GB RAM, Windows 11.
- **GPU 0 (`01:00.0`, Suprim X, PCIe x16) drives the Windows desktop.** Budget it at ~19-21.5 GiB usable. **GPU 1
  (`04:00.0`, Founders Edition, headless) is PCIe Gen4 x4.** Layer split is fine over x4; never use `-sm row`.
- `CUDA_DEVICE_ORDER=PCI_BUS_ID` is set in `start-router.ps1`. Without it two identical cards tie and `tensor-split`
  can land reversed.
- **WDDM spills VRAM into system RAM instead of failing.** A preset that does not fit loads and runs 5-10x slower.
  After every model load check `nvidia-smi`: a card near 24,576 MiB used is the culprit.
- **Other programs share these GPUs and nothing coordinates them:** ComfyUI (`F:\AI\ComfyUI_windows_portable_cu128_v092`),
  the club-3090 vLLM/llama.cpp stack in WSL (ports 8020-8213), LM Studio, Unsloth Studio, games. Before any benchmark
  or load test, run `nvidia-smi` and confirm the GPUs are idle.
- Ports: 8080 is taken by signal-cli in WSL, which is why the router is on 8410.
- Models live in `F:\AI\models\llm\<publisher>\<repo>\`. Unsloth Studio downloads land in `F:\AI\models\llm\hub` and
  are hardlinked in by [link-hub-models.ps1](./link-hub-models.ps1). That folder also holds `HANDOFF.md` (the rig and
  why each llama.cpp flag is set; read §1-2 before changing a preset), `MODEL_REFERENCE.md`, `OWNED_MODELS.md`,
  `CODING_BENCHMARK.md`, `FILE_TREE.md`.

## How the pieces fit

```text
Windows logon task "llama-router"
  -> tray\bin\LlamaTray.exe (single instance, polls every 2 s)
       -> start-router.ps1 -> llama-server router :8410 -> child llama-server per model (presets.ini)
       -> start-strata.ps1 -> F:\AI\strata\run-<size>.bat -> Strata server :8411 (user starts it by hand)
```

**Only one engine may hold the GPUs at a time.** Two hooks enforce it:

- Strata to router: `before_load` in `F:\AI\strata\strata-<size>.json` runs [unload-router-model.ps1](./unload-router-model.ps1)
  before Strata loads, and waits until the router child is gone.
- Router to Strata: the router has no such hook. The **tray** notices a router model loading while Strata is loaded
  and unloads Strata. With the tray not running, a router load fails or crawls while Strata is up.

Other moving parts:

- **Idle unload.** [tray-settings.ps1](./tray-settings.ps1) reads `tray-settings.json` (`RouterIdleMinutes`,
  `StrataIdleMinutes`; untracked, a missing key means never). `start-router.ps1` writes `logs\presets.run.ini`, a copy of
  `presets.ini` with `sleep-idle-seconds` added to `[*]`, because the router's own flag does not reach its children.
  `start-strata.ps1` writes `idle_unload_s` into the Strata config JSON (keeping a `.bak`). Both apply at the next
  start.
- **Memory saver.** [memory-saver.ps1](./memory-saver.ps1) closes and reopens background apps to free RAM for Strata
  (it leaves ~4.6 GB free on IQ2_XS, ~1.8 GB on IQ3_XXS). Ticks live in `memory-saver.choices.json` (untracked); what it
  closed is in `logs\memory-saver.json`.
- **The tray** is one C# file, [tray/LlamaTray.cs](./tray/LlamaTray.cs), compiled with the C# compiler built into
  Windows (no SDK) by [install-tray.ps1](./install-tray.ps1) into `tray\bin\` (untracked). After editing it, re-run
  `install-tray.ps1`; that restarts the user's tray, so say so.

## Strata state and updating it

- Installed sizes: IQ2_XS (default) and IQ3_XXS. Configs: `F:\AI\strata\strata-iq2_xs.json`,
  `strata-iq3_xxs.json`. Both run both GPUs with `layer_split: auto`, 262k context, int8 KV, images on.
  `strata-iq2_xs.json` also has `--vram-reserve-later-mib 1308` and `--pipeline-windows 2` in `args` (added
  2026-10-18, see below); `strata-iq3_xxs.json` has neither and has not been tested on 0.1.44.1.
- Strata is not started at login because loading pins ~39 GB and can leave the PC unresponsive for up to a minute.
  **Do not start it, load a router model, or stop the router unprompted**: it takes the GPUs from whatever Paul is doing.
- Installed engine version: `"version"` in `F:\AI\strata\engine\BUILD.json`. The `session is up (engine X)` line in
  `F:\AI\strata\strata-iq2_xs.log` shows the engine that last ran, so it lags until the next start. Latest upstream:
  `gh api repos/Niko1221/Strata/releases/latest --jq .tag_name`.
- **Engine 0.1.44.1 was installed on 2026-10-16 (from 0.1.43) and benched on 2026-10-18; 0.1.43 never was.** The
  installed IQ2_XS config decoded 52 tok/s on code at every depth (133 / 130 / 117 on 2026-10-04) because the auto
  split left the desktop card (Strata's CUDA1) with 160 MiB free and the start log said so ("CUDA1: 160 MiB of VRAM
  free ... LOW", under WDDM a full card pages). `--vram-reserve-later-mib 1308`, the value that log line suggests,
  restored it to 170 / 158 / 152, and `--pipeline-windows 2` on top gave 188 / 168 / 162. Table and method in
  [README.md](./README.md#engine-01441-measured-2026-10-18). **After any engine update or `SETUP.bat`, read the
  `VRAM free with everything loaded` lines in the start log; a "LOW" there means the desktop card needs a larger
  reserve.** The 0.1.43 engine is kept in `F:\AI\strata\engine\.previous`; `python setup.py --rollback-engine`
  restores it. Upstream says GSQ-RCO answers (IQ2_XS, IQ3_XXS) are byte-identical to 0.1.43.1, which differs from 0.1.43
  only when a message quotes `</tool_call>`. What 0.1.43.1-0.1.44.1 changed for this PC: Windows watchdog fixes for
  long prompts and conversation parking, that `</tool_call>` fix, fenced-JSON tool calls recovered, and the auto
  split's slow-link penalty applied only from a 4x link gap (GPU 1's x4 against GPU 0's x16 is exactly 4x).
  - **A failed `before_load` hook still loads.** It answers 503 only with `min_free_vram_mib` (and too little free
    VRAM) or `"before_load_strict": true`; neither config sets them, so if `unload-router-model.ps1` fails, Strata
    loads beside the router model and WDDM spills.
  - **The 8 GiB pin cap on the two-GPU split** is `STRATA_ARENA_PIN_GIB` (the start log says so). The new opt-in
    `STRATA_PIN_BY_NEED=1` is documented for the arena on one GPU.
  - 0.1.40-0.1.43 added the CUDA route tail skip, an sm_86 Q3_K kernel, a Windows layer-split stall fix and parallel
    GGUF expert reads. The tail skip is off on this config (the log: 99.2% of experts are in the GPU cache), and the
    prefill CPU share is off on a layer split, so of the three "0.1.41 answers" variables only
    `STRATA_PCIE_FRAC_DEFAULT=old` changes anything here.
- **Forks checked 2026-10-16, none worth installing.** Hardin22/Strata-DualGPU (0.1.38-based) and architectds/Strata
  (0.1.40.2-based) both went upstream as opt-in flags: `--adapt-async 1`, `--pipeline-windows 2`,
  `STRATA_PREFILL_CPU_SHARE=auto` (upstream issue #1352, PR #1414). `F:\AI\strata\docs\MULTI_GPU.md` has a 2x3090
  x16+x4 table from one user (#1447: Ryzen 7 9800X3D, 32 GB RAM, 250 W limits, IQ3_S, temperature 0.6, mostly one run
  per arm, OS not stated, not repeated upstream): 137 tok/s decode by default, 145 with `--adapt-async 1`, prompt
  1,798 -> 2,097 tok/s with `--pipeline-windows 2`. Measured here 2026-10-18 on `layer_split: auto`: dropping
  `--remote-expert-opt` changes nothing (inert, as upstream says), and `--pipeline-windows 2` adds 6-11% decode on code
  and nothing on prose or prefill. `--adapt-async 1` is untried (it needs `--resident-experts`).
- **ninfer** (Neroued/ninfer) is Linux and RTX 5090 only. Its fork Don-Chad/ninfer-3090 (v0.16.1, in
  `F:\AI\ninfer-3090`) has Windows sm_86 builds and its own `.ninfer` weights (not GGUF), one GPU per model. Measured
  2026-10-18 (one round each, not interleaved, so an estimate): Qwen3.8-27B on GPU 1 decodes 204 / 171 / 146 tok/s on code at 2k / 52k / 128k against 81 / 63 / 52 for the
  router's `qwen3.8-27b-dflash2`, on a ~4-bit conversion (17.2 GiB) against UD-Q5_K_XL; quality was not compared.
  Flash-Next does not start on this PC (it must pin 34.7 GiB of host memory, which Windows maps into a GPU address
  space with 18.9 GiB free). Details in [README.md](./README.md#ninfer-3090-measured-2026-10-18). Stop the
  router (not just unload its model) while ninfer runs, since a router load lands on GPU 1 beside it and nothing
  coordinates the two. Always set `NINFER_PORT` (the default 8080 is signal-cli's) and `NINFER_GRAFTS=off` (the downloader fetches a private graft
  when a Hugging Face login exists).
- **To update:** `cd F:\AI\strata; cmd /c ".\UPDATE.bat < nul"` (the `< nul` skips the final "press any key"). It pulls
  the code, downloads the engine zip with a checksum, and leaves models and `strata-*.json` untouched (verified
  2026-10-11: configs byte-identical, `idle_unload_s` and `before_load` kept). Strata must not be running. Record a
  baseline first ([bench-depth.ps1](./bench-depth.ps1), or upstream `BENCH.bat` on 0.1.43+).
  **`SETUP.bat` does rewrite `strata-*.json`, so re-add `idle_unload_s`, `before_load`, `sampling` and the two
  `args` flags above after it.**
- **`start-strata.ps1` straight after `stop-strata.ps1` can fail without starting Strata** (2 of 4 back-to-back
  restarts on 2026-10-18; no engine process appeared and nothing was logged). The likely cause is the old launcher still
  holding `logs\strata.log` when `Move-Item` runs; not confirmed. After a hardening pause (wait for the `strata`
  process to exit, then about 15 s) 9 of 9 restarts worked. Do that when restarting from a script.
- **Upstream rewrote its git history** between 2026-10-04 and 2026-10-11 (the clone had no common ancestor with
  `origin/main`, though the trees were identical). `git pull --ff-only` then fails. When the clone is clean and
  `git diff HEAD <upstream commit with the same subject>` is empty, `git reset <that commit>` (mixed; the working tree is
  not touched) lets the pull fast-forward. Back up `strata-*.json` and `run-*.bat` first.
- **Sampling:** without a `sampling` block, a request that sends no temperature decodes greedy (temperature 0), and
  greedy decoding loops on tool calls. Both Strata configs now carry
  `"sampling": {"temperature": 1.0, "top_p": 0.95, "top_k": 20, "min_p": 0.0}` (set 2026-10-12, the router's
  `qwen3.8-27b` values), so every client gets that by default; a request's own fields always win. Pi's `models.json` also sends
  these. Strata's validator accepted the block; no request has been run against it yet. `SETUP.bat` drops this block
  along with `idle_unload_s` and `before_load`.
- **Which size starts:** [start-strata.ps1](./start-strata.ps1) defaults to `-Model iq2_xs`, and the tray calls it with
  no arguments, so the tray always starts IQ2_XS. IQ3_XXS starts only with `.\start-strata.ps1 -Model iq3_xxs`.
  The router has no size choice; it serves only `presets.ini` models.
- **RAM sizing** (Strata's own docs, `F:\AI\strata\docs\MODELS.md`): IQ2_XS needs ~39 GB RAM+VRAM, IQ3_XXS ~47 GB,
  IQ3_S ~55 GB, UD-IQ4_XS needs 48 GB or more and reads part of its experts from the SSD on a 64 GB PC. The KV cache
  held in RAM costs ~13.7 KB per context token (~3.6 GB at 262k, ~1.8 GB at 131k) because `--kv-resident 32768`
  keeps only a fixed window in VRAM. With the memory saver on and the other apps closed, Paul measured ~53 GB used and
  ~11 GB free while serving IQ2_XS.

## Working in this repo

- **Shell:** PowerShell is primary. The Bash tool sees `F:\` as `/f/`. Scripts are PowerShell 7 (`pwsh`).
- **Script style** (match it): `$ErrorActionPreference = 'Stop'`, a header comment with usage and what it needs,
  paths from `$PSScriptRoot`, comments only for the non-obvious why. Read the header of a script before running it;
  several leave a model loaded or expect an idle GPU.
- **`presets.ini`:** keys are `llama-server` flags without the leading `--`. **Never put sampler settings (`temp`,
  `presence-penalty`, ...) in `[*]`**: thinking and non-thinking modes need different values. A `logit-bias` value
  keeps only its first entry on build b10951 and a repeated key keeps the last, so bias lists cannot go here
  (see README "Overthinking logit bias"). Restart the router after editing.
- **Upgrading llama.cpp:** unzip the new build next to the old one and change `$Build` in `start-router.ps1`; rollback
  is the same one-line change. Rerun the checks below.
- **README conventions:** measurements carry a date and what they were measured on ("Measured 2026-10-04, both GPUs,
  262k context"). Do not state a speed you did not measure; label estimates as estimates. Change one variable at a time,
  at least 3 runs per arm, interleaved, with a settle before each (a cold arm fakes a regression).
- **Untracked, do not commit:** `logs/` (all bench results and server logs live only here), `tray/bin/`,
  `tray-settings.json`, `memory-saver.choices.json`.

## Verifying changes

There is no unit-test suite. Verification is running the real thing:

| After | Do |
|---|---|
| Any router model load, any `presets.ini` or build change | `Select-String -Path logs\server.log -Pattern 'retrying without pipeline'`. **A hit means the model loaded degraded** and passes health checks while doing it, so every number from that boot is invalid. Then `nvidia-smi` for the spill check above. |
| Editing `presets.ini` or upgrading llama.cpp | `.\test-tool-calls.ps1 -Model <name>` (all presets if no `-Model`). Exit 1 means a run failed. Fixes: a corrected `chat-template-file`, or a newer build. For Strata: `-BaseUrl http://127.0.0.1:8411`. |
| Comparing drafter lanes or Strata sizes | `.\bench-depth.ps1 -Model a,b` (2k / 52k / 128k context). |
| Thinking-length or logit-bias questions | `.\bench-thinking.ps1` with arms in `bench\logit-bias-arms.json`, tasks in `bench\tasks-*.jsonl`. |
| Editing the tray or the switching logic | Run `install-tray.ps1`, then exercise it. Script-reachable parts first (status endpoints, the scripts the tray calls). The GPU handover between router and Strata was verified 2026-10-04 with 3 switches each way. |

Logs: `logs\server.log` (router; `server.prev.log` is the previous boot), `logs\strata.log` (the launcher's capture),
`F:\AI\strata\strata-<size>.log` (engine). Router status:
`Invoke-RestMethod http://127.0.0.1:8410/models | % data | select id, @{n='status';e={$_.status.value}}`.

## Plans in flight

[plans/ubuntu-dual-boot.md](./plans/ubuntu-dual-boot.md), with the how-to in [docs/ubuntu-setup.md](./docs/ubuntu-setup.md):
dual-boot Ubuntu 26.04 on the spare 477 GB NVMe (disk 3) to measure whether Strata gains on Linux (Windows pins only
8 GiB of its ~33 GiB expert arena on a multi-GPU split; Linux pins all of it). Slices U1-U5; U1 is Paul's hands-on
install and nothing is started. Windows stays the main OS and the four 2 TB drives must not be touched. After editing the
plan or guide, rebuild the phone copy with `python plans/build-ubuntu-page.py <scratch>/ubuntu-dual-boot.html` and
republish it to the artifact URL named in the plan.

## Git

Remote `Pawls/llama-serve` (public), default branch `main`. Work goes through a pull request into `main` (PR #1, the
memory saver, is the precedent). The global `~/.claude/CLAUDE.md` rules apply: no whole-file reverts, inspect
`git diff --stat` before destructive operations, never merge a PR yourself.
