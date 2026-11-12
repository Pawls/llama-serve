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
| Loading | On demand, one model at a time (`--models-max 1`), 15-60 s | One model, ~30-60 s, ~40 GB RAM pinned |
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
  `~\.pi\agent\models.json`, which also pins `reasoning` for every preset (Pi otherwise marks only models it saw
  loaded); a new preset needs an entry there. Strata is the `strata` provider in the same file. That file holds an API
  key field; do not print it.
- **Hermes**: `hermes --provider llama-router` (the router; effort via `/reasoning low|medium|xhigh`, other values
  are an HTTP 500 unless the `pawl-patches` plugin clamps them) and `hermes --provider strata`.
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
       -> start-ninfer.ps1 -> F:\AI\ninfer-3090\<build>\run.bat -> ninfer-serve :8412, GPU 1 (user starts it by hand)
```

**Only one engine may hold the GPUs at a time.** For ninfer the scripts enforce it: `start-ninfer.ps1` stops Strata
and the router (the router itself, since any client request would load a two-GPU preset onto GPU 1), and
`start-router.ps1` and `start-strata.ps1` run `stop-ninfer.ps1` first. The tray starts the router again when ninfer
stops, if the tray had stopped it for ninfer. Between the router and Strata, two hooks enforce it:

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
- **Reserve GPU 0.** The tray toggle sets `ReserveGpu0` in `tray-settings.json`; `start-router.ps1` then writes
  `logs\presets.run.ini` with only `[*]` and the presets whose `device` and `spec-draft-device` lines are all `CUDA1`
  (and `start-strata.ps1` refuses to run). A new single-GPU preset needs `device = CUDA1` to show up, plus entries in
  Pi's `models.json` and VS Code's `chatLanguageModels.json` like any preset. Measured 2026-10-31 on GPU 1 alone: `gemma4-26b-a4b-single` holds
  the full 262k (peak 21.1 GiB); `qwen3.8-27b-single` (Q4_K_XL) holds 131k at q8 KV (peak 21.7 GiB). Qwen at 262k and
  the Q3_K_XL, ISTA IQ3_S and Hauhau Q3_K_P files are untried. The toggle itself was checked through
  `tray-settings.json` and a router restart; the tray menu click was not driven.
- **Memory saver.** [memory-saver.ps1](./memory-saver.ps1) closes and reopens background apps to free RAM for Strata.
  Since `--resident-experts` (2026-11-01) Swift and IQ3_XXS leave 21-28 GB free with the usual apps closed by hand, so
  it matters mainly for UD-Q4_K_XL (~4 GB free that way, ~3 GB with SonarLint running). Ticks live in `memory-saver.choices.json` (untracked); what it
  closed is in `logs\memory-saver.json`.
- **The tray** is one C# file, [tray/LlamaTray.cs](./tray/LlamaTray.cs), compiled with the C# compiler built into
  Windows (no SDK) by [install-tray.ps1](./install-tray.ps1) into `tray\bin\` (untracked). After editing it, re-run
  `install-tray.ps1`; that restarts the user's tray, so say so.

## Strata state and updating it

- Installed sizes: **Swift 1.5 IQ3_S (default since 2026-11-01**, Paul's pick for its shorter thinking), IQ3_XXS
  (default 2026-10-19 to 2026-11-01) and Unsloth UD-Q4_K_XL (bullets below). IQ2_XS was deleted on
  2026-11-01 (Paul's call, for disk space); its shard 2 was a hardlink of IQ3_XXS's, so only shard 1 and the pack went.
  The four-arm bench that chose the default is in
  [README.md](./README.md#swift-15-resident-experts-and-ud-q4_k_xl-measured-2026-11-01).
  `F:\AI\strata\strata-iq3_xxs.json` runs both GPUs with `layer_split: auto`, 262k context, int8 KV, images on, and
  has `--vram-reserve-later-mib 1310` and `--vram-reserve-mib 1344` (2026-10-19, each the value its start log asked
  for; numbers in [README.md](./README.md#engine-0145-iq3_xxs-against-iq2_xs-measured-2026-10-19)). It had
  `--pipeline-windows 2` until 2026-11-01, when engine 0.1.55 made it worthless (see "Engine 0.1.55" below), and has
  `--conversation-cache-mib 0` and `--resident-experts` since 2026-11-01 (see the resident-experts bullet at the end).
- **Swift 1.5 IQ3_S** (84 GB in `F:\AI\Strata-data\models\swift-IQ3_S`, pack `packs\swift-iq3_s`) was installed on
  2026-11-01 by `setup.py --family swift --model IQ3_S --gpus 0,1 --no-start`; config `strata-swift-iq3_s.json`,
  model id `swift-1.5-iq3_s`. Its per-layer embedding table is in shard 1 (setup's `--ple-gguf` is right; ISTA's is
  in shard 2), so its shard 2 cannot be a hardlink of ISTA's. Hand-added to setup's config: `--vram-reserve-mib 1344`
  (setup wrote 700), `--vram-reserve-later-mib 1310`, `--resident-experts` (required: setup warned a full copy needs
  ~78 GB of RAM at 262k), `--conversation-cache-mib 0`, `--adapt-async 1` (2026-11-02), `"env": {"STRATA_PF_FUSED":
  "1"}` (2026-11-02), `--prompt-cache-spread` and `"expert_profile_save": "expert-profile-swift-learned.bin"`
  (2026-11-02), `idle_unload_s`, `sampling` and `before_load`. Without `STRATA_PF_FUSED` Windows refuses to pin its
  24.59 GiB RAM copy (`page-locking refused (out of memory)`, locked with `VirtualLock` instead) and it reads prompts at
  a third to a half of IQ3_XXS's speed; with it the copy is 23.15 GiB, fully page-locked, and prompts read 9-22% faster
  ([README.md](./README.md#swift-15-cpu-and-kernel-switches-measured-2026-11-02)). GPU caches were pre-filled
  from `F:\AI\strata\data\expert-profile.bin`, built on the original model (decode hit rate 95-97% against ~99%);
  since 2026-11-02 from the learned `F:\AI\strata\expert-profile-swift-learned.bin` once it exists.
- **Unsloth UD-Q4_K_XL** (4-bit, 111 GB in `F:\AI\Strata-data\models\unsloth-UD-Q4_K_XL`) was installed on
  2026-11-01 by `setup.py --family unsloth --model UD-Q4_K_XL --gpus 0,1 --no-start`; it left the two configs
  installed then (IQ2_XS and IQ3_XXS) byte-identical. Start it from the tray or with `.\start-strata.ps1 -Model unsloth-ud-q4_k_xl` (config
  `strata-unsloth-ud-q4_k_xl.json`). Since engine 0.1.40 a RAM budget works on a layer
  split: each card caches its own layers' experts and `--resident-budget-gib 36` holds the hottest of the rest, so
  only ~6 GiB of the 71.7 GiB is read from the SSD. Hand-added to setup's config: `--vram-reserve-mib 1344` (setup
  wrote 700), `--vram-reserve-later-mib 1728` (the start log asked for 1468, then 1728), `--conversation-cache-mib 0`,
  `--adapt-async 1` and `--prefill 2048` (was `auto`; both 2026-11-02,
  [README.md](./README.md#ud-q4_k_xl-with---adapt-async-1-and-strata_mmq_kq1-measured-2026-11-02)), `idle_unload_s`,
  `sampling` and `before_load`. Free RAM while serving is ~4 GB at load and 1.0-1.5 GB during a bench (memory saver
  off), so run it with the memory saver.
  Untried levers: `STRATA_ROUTE_TAIL_SKIP=0` in its `env` (upstream: the default tail skip raises its KL from 0.053 to
  0.115 in budget mode) and a CPU picture reader to free ~1.3 GB of VRAM for experts.
- Strata is not started at login because loading locks 17-36 GiB and can leave the PC unresponsive for up to a minute.
  **Do not start it, load a router model, or stop the router unprompted**: it takes the GPUs from whatever Paul is doing.
- Installed engine version: `"version"` in `F:\AI\strata\engine\BUILD.json`. The `session is up (engine X)` line in
  `F:\AI\strata\strata-<size>.log` shows the engine that last ran, so it lags until the next start. Latest upstream:
  `gh api repos/Niko1221/Strata/releases/latest --jq .tag_name`.
- **Engine 0.1.44.1 was installed on 2026-10-16 (from 0.1.43) and benched on 2026-10-18; 0.1.43 never was.** The
  installed IQ2_XS config decoded 52 tok/s on code at every depth (133 / 130 / 117 on 2026-10-04) because the auto
  split left the desktop card (Strata's CUDA1) with 160 MiB free and the start log said so ("CUDA1: 160 MiB of VRAM
  free ... LOW", under WDDM a full card pages). `--vram-reserve-later-mib 1308`, the value that log line suggests,
  restored it to 170 / 158 / 152, and `--pipeline-windows 2` on top gave 188 / 168 / 162. Table and method in
  [README.md](./README.md#engine-01441-measured-2026-10-18). **After any engine update or `SETUP.bat`, read the
  `VRAM free with everything loaded` lines in the start log; a "LOW" there means the desktop card needs a larger
  reserve.** The engine an update replaced is kept in `F:\AI\strata\engine\.previous` (0.1.45 since 2026-11-01);
  `python setup.py --rollback-engine` restores it. Upstream says GSQ-RCO answers (IQ2_XS, IQ3_XXS) are byte-identical to 0.1.43.1, which differs from 0.1.43
  only when a message quotes `</tool_call>`. What 0.1.43.1-0.1.44.1 changed for this PC: Windows watchdog fixes for
  long prompts and conversation parking, that `</tool_call>` fix, fenced-JSON tool calls recovered, and the auto
  split's slow-link penalty applied only from a 4x link gap (GPU 1's x4 against GPU 0's x16 is exactly 4x).
  - **A failed `before_load` hook still loads.** It answers 503 only with `min_free_vram_mib` (and too little free
    VRAM) or `"before_load_strict": true`; neither config sets them, so if `unload-router-model.ps1` fails, Strata
    loads beside the router model and WDDM spills.
  - **The 8 GiB pin cap on the two-GPU split** is `STRATA_ARENA_PIN_GIB` (the start log says so). The new opt-in
    `STRATA_PIN_BY_NEED=1` is documented for the arena on one GPU.
  - 0.1.40-0.1.43 added the CUDA route tail skip, an sm_86 Q3_K kernel, a Windows layer-split stall fix and parallel
    GGUF expert reads. **The tail skip is on here** (corrected 2026-11-01: the start logs of Swift and IQ3_XXS print
    `route tail skip: N missed experts skipped`, tens of thousands per bench session; its 90% line counts experts held
    in VRAM, not the routed share). The prefill CPU share is off on a layer split.
- **Forks checked 2026-10-16, none worth installing.** Hardin22/Strata-DualGPU (0.1.38-based) and architectds/Strata
  (0.1.40.2-based) both went upstream as opt-in flags: `--adapt-async 1`, `--pipeline-windows 2`,
  `STRATA_PREFILL_CPU_SHARE=auto` (upstream issue #1352, PR #1414). `F:\AI\strata\docs\MULTI_GPU.md` has a 2x3090
  x16+x4 table from one user (#1447: Ryzen 7 9800X3D, 32 GB RAM, 250 W limits, IQ3_S, temperature 0.6, mostly one run
  per arm, OS not stated, not repeated upstream): 137 tok/s decode by default, 145 with `--adapt-async 1`, prompt
  1,798 -> 2,097 tok/s with `--pipeline-windows 2`. Measured here 2026-10-18 on `layer_split: auto`: dropping
  `--remote-expert-opt` changes nothing (inert, as upstream says), and `--pipeline-windows 2` adds 6-11% decode on code
  and nothing on prose or prefill (on 0.1.44.1; on 0.1.55 it adds nothing). `--adapt-async 1` is on Swift since 2026-11-02 (see "Untried levers" below).
- **ninfer** (Neroued/ninfer) is Linux and RTX 5090 only. Its fork Don-Chad/ninfer-3090 (v0.16.1, in
  `F:\AI\ninfer-3090`) has Windows sm_86 builds and its own `.ninfer` weights (not GGUF), one GPU per model. Measured
  2026-10-18 (one round each, not interleaved, so an estimate): Qwen3.8-27B on GPU 1 decodes 204 / 171 / 146 tok/s on code at 2k / 52k / 128k against 81 / 63 / 52 for the
  router's `qwen3.8-27b-dflash2`, on a ~4-bit conversion (17.2 GiB) against UD-Q5_K_XL; quality was not compared.
  Flash-Next does not start on this PC (it must pin 34.7 GiB of host memory, which Windows maps into a GPU address
  space with 18.9 GiB free). Details in [README.md](./README.md#ninfer-3090-measured-2026-10-18). Start it
  from the tray or `start-ninfer.ps1` (see "How the pieces fit" for how it shares the GPUs); a hand launch must stop
  the router itself and set `NINFER_PORT` (the default 8080 is signal-cli's) and `NINFER_GRAFTS=off` (the downloader
  fetches a private graft when a Hugging Face login exists), as the script does. Verified 2026-11-02 by driving the
  tray menu: start, stop with the router restarted, Start router stopping ninfer, ninfer killed (router back in
  2 s), and a tray restart adopting a running ninfer. Strata starting over a running ninfer was not run. Cleaned up 2026-11-01: the Flash-Next conversion, its `.ngram` file, a partial
  download and `_download\ninfer.zip` are deleted; only the engine folder and `models\qwen3_8_27b.ninfer` (17.7 GB)
  remain. **ashalliants/ninfer-3090 is a GitHub fork of Don-Chad's repo, but the work now happens there**: Don-Chad's
  v0.16.1 is ashalliants's code at `931bd49b` (2026-10-15), copied back whole, plus Windows build fixes. ashalliants
  ships Windows zips its author builds but never runs on a card. **v0.18.0 (2026-11-12) runs here** since 2026-11-12
  (`F:\AI\ninfer-3090\ninfer-rtx3090-windows-x64-0.18.0-rtx3090`, `$Build` in `start-ninfer.ps1`; v0.17.1 and
  Don-Chad's v0.16.1 stay beside it for rollback): same model file and profile, tool calls 5/5, JSON mode 3/3. It
  has the out-of-vocabulary crash fix we asked for in #410 (PR #412: the request fails, the server stays up) and the
  `run.bat` `FINDSTR` fix (#434). New in v0.18.0: without `--api-key` the server refuses a foreign `Origin` (403) and
  Host names other than an IP or its own (`localhost` passes); add others with `NINFER_ALLOWED_HOST`. Upstream
  status, known bugs and which build has which fix are in the README section.
- **To update:** `cd F:\AI\strata; cmd /c ".\UPDATE.bat < nul"` (the `< nul` skips the final "press any key"). It runs
  `git pull --ff-only`, then `setup.py --update`: Python packages, a new engine from Strata's signed update feed when
  the installed one is older than setup's `MIN_ENGINE`, and config upgrades. It leaves models and `strata-*.json`
  untouched (verified 2026-10-11, 2026-10-19 and 2026-11-01: configs and `run-*.bat` byte-identical). Strata must not
  be running. Record a baseline first ([bench-depth.ps1](./bench-depth.ps1), or upstream `BENCH.bat` on 0.1.43+).
  On 2026-11-01 it went from code 0.2.0-beta / engine 0.1.45 to v0.2.9 / engine 0.1.55 with no workaround (the
  GitHub release zip 404'd and it fell back to the feed by itself); it leaves an untracked `engine-feed\` in the clone.
  The `STRATA_PREBUILT_URL` workaround used for 0.1.45 on 2026-10-19 (0.2.0-beta's `UPDATE.bat` looked only for
  the dropped `strata-windows-x64.zip`) is no longer needed.
  **`SETUP.bat` rewrote `strata-*.json` up to 0.2.0.** The 0.2.9 `setup.py` keeps every key it does not write
  (`idle_unload_s`, `before_load`, `sampling`) and carries hand-added `args` into `user_args` (`SETUP_KEYS`,
  `carry_user_args`); read in the code, not yet run here. After a setup run, check those keys and the `args` flags
  listed under "Installed sizes" above and in the Swift and UD-Q4_K_XL bullets, and re-add any that dropped.
- **Engine 0.1.55 (installed 2026-11-01, benched the same night; table in
  [README.md](./README.md#engine-0155-strata-029-measured-2026-11-01)).** Decode is 12-16% faster than 0.1.45 on both
  sizes; not one variable (engine and code changed together). What changed for this PC:
  - **A conversation cache is on by default**, sized from free RAM at start (2,048 MiB on IQ3_XXS, 3,584 MiB on
    IQ2_XS). Copying the conversation in after each reply costs ~0.4-0.5 s at 52k and ~1 s at 128k on IQ2_XS, so
    net t/s at depth trails decode by up to 16%. `--conversation-cache-mib 0` in `args` turns it off (measured: net
    rises to decode, 1.6-2.9 GB more RAM free) but loses prompt reuse when requests alternate between conversations.
    Turned off in every config on 2026-11-01 (Paul runs one conversation at a time); the bench that night
    confirmed it (net tracks decode at depth, no conversation-cache line in the start logs).
  - **`--pipeline-windows 2` was removed** from both configs: within 3% on IQ3_XXS, 6% slower on IQ2_XS 2k code.
  - **Layer-major prefill (0.2.7) never runs here** (`no VRAM for a super-chunk`); prompt reads are unchanged.
  - Stage order is GPU 1 then GPU 0; the desktop card runs the head and the draft layer and has 758 MiB free with
    everything loaded on IQ3_XXS, no LOW. Tool calls 3/3 on both sizes.
- **`start-strata.ps1` straight after `stop-strata.ps1` can fail without starting Strata** (2 of 4 back-to-back
  restarts on 2026-10-18; no engine process appeared and nothing was logged). The likely cause is the old launcher still
  holding `logs\strata.log` when `Move-Item` runs; not confirmed. After a pause (wait for the `strata` process to
  exit, then about 15 s) 9 of 9 restarts worked. The scripts now handle it themselves: `stop-strata.ps1` returns only
  after the old `start-strata.ps1` launcher has exited, and `start-strata.ps1` waits up to 30 s for a held log, then
  logs to `logs\strata.<timestamp>.log` instead. No caller needs its own pause. Measured 2026-10-19 (IQ3_XXS,
  stop then start at once, each start reaching `/health` loaded in 28 s): 8 of 8 restarts, stops 0.5-1.8 s, the log
  rotated every time and no fallback log was written. The first 4 found an engine still exiting after the stop
  returned (an exiting process has no `Path`, so the path filter skipped it); counted as running now, none was left
  in the last 4. Since 2026-11-02 `stop-strata.ps1` unloads a loaded model before the kill (so the engine saves
  `expert_profile_save`): ~5 s on Swift, up to ~15 s more while a reply is still running, then a kill as before.
- **Upstream rewrote its git history** between 2026-10-04 and 2026-10-11 (the clone had no common ancestor with
  `origin/main`, though the trees were identical). `git pull --ff-only` then fails. When the clone is clean and
  `git diff HEAD <upstream commit with the same subject>` is empty, `git reset <that commit>` (mixed; the working tree is
  not touched) lets the pull fast-forward. Back up `strata-*.json` and `run-*.bat` first.
- **Sampling:** without a `sampling` block, a request that sends no temperature decodes greedy (temperature 0), and
  greedy decoding loops on tool calls. Both Strata configs now carry
  `"sampling": {"temperature": 1.0, "top_p": 0.95, "top_k": 20, "min_p": 0.0}` (set 2026-10-12, the router's
  `qwen3.8-27b` values), so every client gets that by default; a request's own fields always win. Pi's `models.json` also sends
  these. Strata's validator accepted the block; no request has been run against it yet. `SETUP.bat` up to 0.2.0
  dropped this block along with `idle_unload_s` and `before_load` (see "To update" for 0.2.9).
- **Which size starts:** [start-strata.ps1](./start-strata.ps1) defaults to `-Model swift-iq3_s`. The tray has one
  start item per size (`StrataSizes` in [tray/LlamaTray.cs](./tray/LlamaTray.cs), the default first; a new row gets a
  menu item by itself) and passes `-Model`.
  The router has no size choice; it serves only `presets.ini` models. Strata answers with whichever size is loaded,
  whatever `model` a request names (checked 2026-10-19: an `iq2_xs` request to IQ3_XXS was answered as `iq3_xxs`), so
  each client lists every id (`swift-1.5-iq3_s`, `qwen3.8-flash-next-iq3_xxs`, `qwen3.8-flash-next-unsloth-ud-q4_k_xl`;
  Pi's `models.json`, Hermes's `config.yaml` with Swift as its default, VS Code's `chatLanguageModels.json`) and any
  works.
- **RAM sizing** (Strata's own docs, `F:\AI\strata\docs\MODELS.md`): IQ2_XS needs ~39 GB RAM+VRAM, IQ3_XXS ~47 GB,
  IQ3_S ~55 GB. The Unsloth files (UD-IQ4_XS, UD-Q4_K_XL) need 48 GB or more; on this PC's two cards the GPU caches
  hold ~29 GiB of experts beside the RAM budget, so UD-IQ4_XS (55.4 GiB of experts) should need no SSD reads at all
  (an estimate; not installed) and UD-Q4_K_XL reads ~6 GiB of cold experts from the SSD. The KV cache
  held in RAM costs ~13.7 KB per context token (~3.6 GB at 262k, ~1.8 GB at 131k) because `--kv-resident 32768`
  keeps only a fixed window in VRAM. With the memory saver on and the other apps closed, Paul measured ~53 GB used and
  ~11 GB free while serving IQ2_XS.
- **`--resident-experts` is on for IQ3_XXS and Swift since 2026-11-01.** The default mode copies all 40 GiB of
  IQ3_XXS's experts into RAM (start log: `expert arena ... 42916118528 B`) although the GPU caches already hold ~30 GiB
  of them. On a layer split `--resident-experts` leaves every card's cache out of the RAM copy
  (`F:\AI\strata\docs\MULTI_GPU.md`, "The resident RAM mode works on a split"; `docs\DETAILS.md` "Low-RAM modes"):
  17.05 GiB for IQ3_XXS (pinned), 24.59 GiB for Swift IQ3_S. Measured 2026-11-01, three interleaved rounds: free RAM
  7.5 -> 28 GB on IQ3_XXS, decode within 1-4%, prompt reads 12-16% faster, load ~45 -> ~110 s.
- **Untried levers from `F:\AI\strata\docs\DETAILS.md`** (read in full 2026-11-01; upstream figures, none measured
  here unless marked). What the start logs show about this PC: 9 CPU workers on logical processors 2,4,...,12,13,14,15
  (P-cores and E-cores mixed; i5-12600K is 6P+4E), host thread on 0, `large pages refused`.
  - **Measured on Swift 2026-11-02** ([README.md](./README.md#swift-15-cpu-and-kernel-switches-measured-2026-11-02)):
    `--adapt-async 1` +6-9% code decode and `STRATA_PF_FUSED=1` +9-22% prompt reads (the RAM copy then pins), both
    installed on Swift; `--pool-affinity p-cores` 10-17% slower (5 P-core workers against 9 mixed);
    `--host-core last` no change (host stays on 0).
  - **Measured on IQ3_XXS 2026-11-02** ([README.md](./README.md#iq3_xxs-with---adapt-async-1-and-strata_pf_fused1-measured-2026-11-02)):
    both Swift switches together leave decode unchanged and read long prompts 7-9% slower (its copy already pinned);
    not installed.
  - **Measured on UD-Q4_K_XL 2026-11-02** (same README section as the UD-Q4_K_XL bullet above): `--adapt-async 1`
    +20-27% decode at every depth, installed; `STRATA_MMQ_KQ=1` (`STRATA_PF_FUSED` does not cover K-quants on CUDA)
    +2-7% prompt reads, not installed; `--prefill 2048` +14-24% code prompt reads at depth, installed; 4096 is slower.
  - **Installed on Swift 2026-11-02, not benched:** `--prompt-cache-spread` and `"expert_profile_save":
    "expert-profile-swift-learned.bin"` (saved on unload and every 10 min; `stop-strata.ps1` tries an unload first, so a
    stop saves it unless a reply is still running after 15 s; to go back, stop Strata, remove the key, then delete the file).
  - **Measured on Swift 2026-11-02:** `STRATA_EXCHANGE_ROTATE=1` (upstream 164 -> 188 tok/s on an RTX 5090) is within
    the spread on the layer split and costs ~1 GB RAM; not installed
    ([README.md](./README.md#swift-15-with-strata_exchange_rotate1-measured-2026-11-02)).
  - Hit rate: `--vision cpu` with a Q8_0 mmproj frees ~1.4 GB of VRAM (~1,000 experts, 2-8% decode upstream) at
    ~3 s per picture (not approved by Paul). `--draft-vocab en`: ~110 MiB VRAM, English 1-2% faster, no CJK drafts.
  - Pinning: granting "Lock pages in memory" (`F:\AI\strata\tools\windows\enable_large_pages.ps1` as admin, then log
    off and on) is what large pages need; whether it helps Swift's refused pin is unknown.
  - Quality: `STRATA_ROUTE_TAIL_SKIP=0` (see the 0.1.40-0.1.43 bullet above; upstream KL 0.003-0.006 on one GPU,
    0.053 -> 0.115 on UD-Q4_K_XL's budget mode).
  - Safety: NVIDIA Control Panel's `CUDA - Sysmem Fallback Policy` = `Prefer No Sysmem Fallback` for `strata.exe`
    turns a silent WDDM spill into an error.

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
8 GiB of a full-copy expert arena on a multi-GPU split; Linux pins all of it). Since 2026-11-01 the installed configs
run `--resident-experts`: IQ3_XXS's 17 GiB copy pins fully on Windows, and since 2026-11-02 (`STRATA_PF_FUSED=1`) so
does Swift's 23.15 GiB copy, so pinning alone no longer argues for Linux; the full-copy mode is the remaining case. Slices U1-U5; U1 is Paul's hands-on
install and nothing is started. Windows stays the main OS and the four 2 TB drives must not be touched. After editing the
plan or guide, rebuild the phone copy with `python plans/build-ubuntu-page.py <scratch>/ubuntu-dual-boot.html` and
republish it to the artifact URL named in the plan.

## Git

Remote `Pawls/llama-serve` (public), default branch `main`. Work goes through a pull request into `main` (PR #1, the
memory saver, is the precedent). The global `~/.claude/CLAUDE.md` rules apply: no whole-file reverts, inspect
`git diff --stat` before destructive operations, never merge a PR yourself.
