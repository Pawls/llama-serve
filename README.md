# llama-serve

Local model serving for Pi (and anything else that speaks the OpenAI API) on this PC's two RTX 3090s.

## What "the router" is

`llama-server.exe` is llama.cpp's program that loads a model onto the GPUs and answers chat requests over HTTP. Normally one `llama-server` = one model, fixed at launch.

The **router** is `llama-server` started in a special mode. It doesn't load a model at startup. Instead it:

1. Reads `presets.ini`, a list of named models (`qwen3.8-27b`, `gemma4-31b`, ...), each with its file path and the exact settings that model needs.
2. Listens on `http://127.0.0.1:8410` and tells clients which models it offers.
3. When a request names a model, it **loads that model on demand** (a child `llama-server` process, ~15-60 s), then answers.
4. Only one model is loaded at a time (`--models-max 1`). Asking for a different model unloads the current one first.

So you never pick flags or load models by hand. You pick a model in Pi, and the router does the rest.

```text
Pi ──> router :8410 ──> presets.ini says how ──> child llama-server with that model on the GPUs
```

## Everyday use

### It starts by itself

A Windows Scheduled Task named `llama-router` starts the router when you log in. It runs hidden and uses no GPU memory until a model is requested.

### Using it from Pi

1. Run `pi`.
2. Type `/model` and choose any model under **llama.cpp** (for example `qwen3.8-27b`).
3. The first message loads it; a `[llama.cpp] Loading...` line appears under the prompt. After that, replies are fast.

The footer shows the context size, e.g. `0.0%/262k`. On models that can think, the thinking level appears once the model is selected or after the first message. `Shift+Tab` cycles thinking on and off.

### Start, stop, restart

Run these from PowerShell.

| To | Run |
|---|---|
| Start (in the background, as at login) | `Start-ScheduledTask llama-router` |
| Stop (frees both GPUs) | `F:\AI\llama-serve\stop-router.ps1` |
| Restart (after editing `presets.ini`) | stop, then start |
| Start in a visible window, to watch the log live | `F:\AI\llama-serve\start-router.ps1` (Ctrl+C stops it) |
| Is it running? | `Invoke-RestMethod http://127.0.0.1:8410/models \| % data \| select id, @{n='status';e={$_.status.value}}` |

**Stop the router before gaming, ComfyUI, LM Studio or Unsloth Studio** if a model is loaded. Nothing coordinates GPU memory between these programs.

To free the GPUs but keep the router running, unload just the model:

```powershell
Invoke-RestMethod http://127.0.0.1:8410/models/unload -Method Post -ContentType application/json -Body '{"model":"qwen3.8-27b"}'
```

## The models

Every model below was loaded and checked on 2026-09-14: correct answer, image test where it has vision, clean load log, and VRAM under the limit. Speeds are from one short coding reply each, so they're rough, not a benchmark.

| Pi name | What it is | Context | Images | Thinking by default | Speed | GPUs |
|---|---|---|---|---|---|---|
| `qwen3.8-27b` | Default. Best all-rounder, the most-validated config | 262k | yes | on | ~57 t/s | both |
| `qwen3.8-27b-dflash2` | Same model, DFlash2 speed-up drafter; faster than the default, most at long context | 262k | yes | on | ~55-59 t/s (~42 at 128k) | both |
| `swift-qwen3.8-27b-dflash2` | Swift: Qwen3.8 retrained to think less; about a quarter fewer thinking tokens, same answers in our tests | 262k | yes | on | ~72-87 t/s | both |
| `swift-qwen3.8-27b` | Swift with the built-in MTP drafter instead | 262k | yes | on | ~64-71 t/s | both |
| `qwen3.8-27b-nopreserve` | Same as default, but earlier turns' thinking is dropped (BoxelBuilder) | 262k | yes | on | ~54 t/s | both |
| `qwen3.8-fable-q5` | Qwen3.8 fine-tune tuned for coding (uncensored) | 262k | yes | on | ~59 t/s | both |
| `qwen3.6-35b-a3b` | Older Qwen, very fast (only 3B active) | 262k | yes | off | ~112 t/s | both |
| `qwen3.6-35b-a3b-dflash` | Same, text-only with a DFlash drafter: much faster on code, slower on prose | 262k | no | off | ~156 code / ~63 prose | both |
| `qwen3-coder-30b` | Coding-only, never thinks | 262k | no | n/a | ~135 t/s | both |
| `qwen3-coder-30b-dflash` | Same, with a DFlash drafter: much faster on code, much slower on prose | 262k | no | n/a | ~190 code / ~56 prose | both |
| `ornith-35b` | Fast MoE with a built-in speed-up drafter | 262k | yes | off | ~176 t/s | both |
| `gemma4-31b` | Google Gemma 4, dense; context kept short | 65k | yes | off | ~36 t/s | both |
| `gemma4-31b-mtp` | Same, with an MTP drafter; faster on everything | 65k | yes | off | ~55 code / ~42 prose | both |
| `gemma4-26b-a4b` | Google Gemma 4 MoE; the long-context Gemma | 262k | yes | off | ~100 t/s | both |
| `gemma4-26b-a4b-dflash` | Same, with a DFlash drafter: much faster on code, slower on prose | 262k | yes | off | ~190 code / ~65 prose | both |
| `muse-glimmer-30b` | Strong reasoner with a DFlash drafter | 262k | yes | always reasons | ~90 t/s | GPU1 only |

Names ending in `-dflash`, `-dflash2` or `-mtp` are the same model plus a small "drafter" that guesses tokens ahead for the big model to check. The output is the same model's; only speed changes. Speeds for these come from the 2026-09-14 benchmark (5 runs each, coding and essay prompts). A drafter that guesses code well can guess prose badly, so the `-dflash` coder and 26B lanes are for coding sessions.

"Thinking by default" is the preset's setting. **Pi overrides it:** when you pick a model that supports thinking, Pi switches thinking on (`medium`), and Pi's choice wins. Press `Shift+Tab` to turn it off for fast answers. `qwen3-coder-30b` has no thinking mode, and Muse-Glimmer always reasons.

## Changing things

### Add a model

1. Download the GGUF into `F:\AI\models\llm\<publisher>\<repo>\` (LM Studio does this, or `hf download`).
2. Add a `[name]` section to `presets.ini`. Copy the closest existing section and change `model`, `mmproj` and the sampler lines to the model card's values. `F:\AI\models\llm\HANDOFF.md` §3 explains every key.
3. Restart the router.
4. Run `F:\AI\llama-serve\test-tool-calls.ps1 -Model <name>` to confirm it can use tools (see below).

### Check that tool calls work

Pi's agent works by calling tools, and each model writes tool calls in its own format that llama.cpp has to recognize. `test-tool-calls.ps1` checks every preset (or `-Model <name>`, `-Runs <n>`): the model must call a `read_file` tool with valid JSON arguments, with no raw tool markup leaking into the reply, and then answer using the tool's result. Run it after editing `presets.ini` or upgrading the llama.cpp build. It is a one-off check, not something that runs on normal requests. Results go to `logs\tool-calls-*.json`; exit code 1 means some run failed.

On 2026-09-14 (build b10951) all 16 presets passed 3/3, including the thinking and drafter lanes. If a model fails, the usual fix is a corrected chat template for that preset (`chat-template-file = <path to .jinja>`), or a newer llama.cpp build.

Keys are llama-server's command-line flags without the leading `--`. Settings under `[*]` apply to every model. **Never put sampler settings (`temp`, `presence-penalty`, ...) in `[*]`**: thinking and non-thinking modes need different values.

### Models downloaded by Unsloth Studio

Unsloth Studio stores downloads in `F:\AI\models\llm\hub`. Run `F:\AI\llama-serve\link-hub-models.ps1` to preview, then add `-Apply`, to hardlink them into the normal library. Hardlinks use no extra disk.

## When something is wrong

| Symptom | Check |
|---|---|
| Pi lists no llama.cpp models | The router isn't running: start it. |
| A model is suddenly very slow (several times slower than usual) | It didn't fit in VRAM and Windows spilled into system RAM. Run `nvidia-smi`: a card near 24,576 MiB used is the culprit. Close other GPU programs, or lower `ctx-size` for that model. |
| Speeds or memory look off after changing a preset | `Select-String -Path F:\AI\llama-serve\logs\server.log -Pattern 'retrying without pipeline'`. Any hit means the model loaded in a degraded mode; the preset needs less context or a different `tensor-split`. |
| A model fails to load | Read the end of `F:\AI\llama-serve\logs\server.log`. The previous boot's log is `server.prev.log`. |
| Port 8410 is busy | Another router is already running (for example one started in a window). Stop it first. |

## Files

| File | Purpose |
|---|---|
| `presets.ini` | The model list and each model's settings. |
| `start-router.ps1` | Starts the router: pinned llama.cpp build, port 8410, logs to `logs\server.log`. |
| `stop-router.ps1` | Stops the router and any loaded model. |
| `test-tool-calls.ps1` | Checks that every model's tool calls parse and round-trip. |
| `link-hub-models.ps1` | Hardlinks Unsloth Studio downloads into the library. |

Why port 8410: 8080 is taken by signal-cli in WSL, and the club-3090 WSL stack uses 8020-8213. Pi finds the router through the user environment variable `LLAMA_BASE_URL=http://127.0.0.1:8410/v1`.

The llama.cpp build lives in `F:\AI\llama.cpp\b10951\`. To upgrade, unzip a new build next to it and change `$Build` in `start-router.ps1`, so rolling back is a one-line change.
