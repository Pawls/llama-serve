# llama-serve

Local model serving for Pi (and anything else that speaks the OpenAI API) on this PC's two RTX 3090s.

## What "the router" is

`llama-server.exe` is llama.cpp's program that loads a model onto the GPUs and answers chat requests over HTTP. Normally one `llama-server` = one model, fixed at launch.

The **router** is `llama-server` started in a special mode. It doesn't load a model at startup. Instead it:

1. Reads `presets.ini`, a list of named models (`qwen3.8-27b`, `gemma4-31b`, ...), each with its file path and the exact settings that model needs.
2. Listens on `http://127.0.0.1:8410` and tells clients which models it offers.
3. When a request names a model, it **loads that model on demand** (a child `llama-server` process, ~15-60 s), then answers.
4. Only one model is loaded at a time (`--models-max 1`). Asking for a different model unloads the current one first.
5. Keeps a model loaded until told otherwise, unless you pick an idle time in the tray. Then `start-router.ps1` writes `logs\presets.run.ini` (a copy of `presets.ini` plus `sleep-idle-seconds`) and starts from it. An idle model goes to `sleeping` in `/models`: its VRAM is free, the process stays, and the next request wakes it. Tested 2026-10-11 with `gemma4-26b-a4b` at 15 s: VRAM fell to 0 on GPU 1 and the next request answered.

So you never pick flags or load models by hand. You pick a model in Pi, and the router does the rest.

```text
Pi ──> router :8410 ──> presets.ini says how ──> child llama-server with that model on the GPUs
```

## Everyday use

### It starts by itself, in the system tray

A Windows Scheduled Task named `llama-router` starts the **Llama Router** tray app when you log in, and the tray app starts the router without a window. The router uses no GPU memory until a model is requested. If you quit the tray, start it again from the Start menu (**Llama Router**).

The llama icon's color shows the state: grey stopped, blue ready with no model, amber loading or stopping, green model loaded. Click it (left or right) for the menu:

| Menu item | Does |
|---|---|
| Status line, GPU memory | What's loaded, and each card's VRAM use when the menu opens |
| Load model | Loads a preset, replacing the current one; a notification says when it's ready or if it failed |
| Unload model | Frees the GPUs, keeps the router running |
| Open chat in browser | llama.cpp's built-in chat page at `http://127.0.0.1:8410/` |
| Open server log, Edit presets.ini, Open llama-serve folder | Opens them |
| Stop / Start router, Restart router | Restart after editing `presets.ini` |
| Strata status, Start / Stop Strata, Unload Strata model, Open Strata app / log | Qwen3.8-Flash-Next on its own server; see [Strata](#strata-qwen38-flash-next-125b-moe) |
| Memory saver | Closes the background apps you tick to give Strata more RAM; click again to reopen them. See [Memory saver](#memory-saver) |
| Router models: free GPUs when idle, Strata: unload when idle | How long a model sits unused before it frees the GPUs: never, 15 / 30 minutes, 1 / 2 / 4 hours. Defaults: router never, Strata 15 minutes. Saved in `tray-settings.json` and used at the next router or Strata start |
| Start with Windows | Turns the logon task on or off |
| Quit | Stops the router and closes the tray |

If the router is already running when the tray starts (for example in a window), the tray uses it instead of starting another.

### Using it from Pi

1. Run `pi`. It starts on `swift-qwen3.8-27b-dflash2` at `xhigh` thinking (`defaultModel` and `defaultThinkingLevel` in `~\.pi\agent\settings.json`).
2. To switch, type `/model` and choose any model under **llama.cpp**. `/llama` is Pi's router manager: it shows what is loaded and can load and unload models. Don't use its **Download model…**; downloads land in llama.cpp's cache, not under `F:\AI\models\llm\`, and need a preset before the router serves them properly.
3. The first message loads the model (15-60 s). After that, replies are fast.

The footer shows the context size, e.g. `0.0%/262k`, and the thinking level. Pi talks to the router through its built-in `llama.cpp` provider (it reads `LLAMA_BASE_URL`); the older `huggingface/pi-llama` package is not installed, since it registers a second copy of every model and resets the overrides below after the first request.

On its own, Pi treats llama.cpp thinking as on/off and never sends an effort, so the preset's `xhigh` always applied. The Qwen3.8 router models get `modelOverrides` in `~\.pi\agent\models.json` (provider `llama.cpp`) that map Pi's thinking level onto the template's `reasoning_effort`, so `Shift+Tab` cycles off / low / medium / xhigh and the footer shows the effort the model really gets. Pi sends `preserve_thinking: true` for all of them except `qwen3.8-27b-nopreserve`. Each entry pins `"reasoning": true`, because Pi marks a model as able to think only if it was loaded when Pi last read the router's model list; without the pin an unloaded model gets no thinking controls and the preset's `xhigh` applies. The presets with thinking off (`qwen3.6-*`, `ornith-35b`, `gemma4-*`) are pinned `"reasoning": false` for the same reason, so Pi never turns their thinking on. Checked 2026-10-19 by capturing Pi's request bodies at each level, on a loaded model and on unloaded ones. A new preset needs its own entry there.

### Using it from VS Code chat

VS Code's own chat lists every router preset (group **llama.cpp Router**) and Strata (group **Strata**) in its model picker. They come from VS Code's custom endpoint provider, configured in `%APPDATA%\Code\User\chatLanguageModels.json`. That file was generated from `presets.ini` on 2026-10-04: image support and context sizes come from each preset, and tool calling is on for all. After adding a preset, add its entry there too, or regenerate the file.

### Start, stop, restart

The tray menu covers these. From PowerShell:

| To | Run |
|---|---|
| Start the tray (and the router), as at login | `Start-ScheduledTask llama-router` |
| Stop (frees both GPUs; the tray stays and shows it stopped) | `F:\AI\llama-serve\stop-router.ps1` |
| Restart (after editing `presets.ini`) | stop, then start from the tray menu |
| Start in a visible window, to watch the log live | `F:\AI\llama-serve\start-router.ps1` (Ctrl+C stops it) |
| Is it running? | `Invoke-RestMethod http://127.0.0.1:8410/models \| % data \| select id, @{n='status';e={$_.status.value}}` |

**Stop the router before gaming, ComfyUI, LM Studio or Unsloth Studio** if a model is loaded. Nothing coordinates GPU memory between these programs.

To free the GPUs but keep the router running, unload just the model:

```powershell
Invoke-RestMethod http://127.0.0.1:8410/models/unload -Method Post -ContentType application/json -Body '{"model":"qwen3.8-27b"}'
```

## The models

Every model below was loaded and checked on 2026-09-14: correct answer, image test where it has vision, clean load log, and VRAM under the limit. Speeds are from one short coding reply each, so they're rough, not a benchmark. The two Swift rows were re-checked the same way on 2026-09-28, after the swap to Swift 1.5.

| Pi name | What it is | Context | Images | Thinking by default | Speed | GPUs |
|---|---|---|---|---|---|---|
| `qwen3.8-27b` | Default. Best all-rounder, the most-validated config | 262k | yes | on | ~57 t/s | both |
| `qwen3.8-27b-dflash2` | Same model, DFlash2 speed-up drafter; faster than the default, most at long context | 262k | yes | on | ~55-59 t/s (~42 at 128k) | both |
| `swift-qwen3.8-27b-dflash2` | Swift 1.5: Qwen3.8 retrained to think less (1.0 used about a quarter fewer thinking tokens, same answers in our tests) | 262k | yes | on | ~76 code / ~48 prose (~52 / ~32 at 128k) | both |
| `swift-qwen3.8-27b` | Swift 1.5 with the built-in MTP drafter; only faster for short prose chats | 262k | yes | on | ~67 code / ~53 prose (~38 / ~31 at 128k) | both |
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

"Thinking by default" is the preset's setting. **Pi overrides it** on the Qwen3.8 models: Pi sends its own thinking level, and Pi's choice wins. Press `Shift+Tab` to change it (off for fast answers); that also sets the effort (low / medium / xhigh). On the other presets Pi sends nothing and the preset decides (see the pins above). In Hermes, `hermes --provider llama-router` with `/reasoning low|medium|xhigh` sends a top-level `reasoning_effort`, which the router passes to the template like the kwarg (checked 2026-10-19 with `/apply-template`; the Hermes request itself was read from its code, not captured). The template rejects any other value with an HTTP 500; the local `pawl-patches` Hermes plugin lowers other levels to the nearest one the provider declares (`high` becomes `medium`). `qwen3-coder-30b` has no thinking mode, and Muse-Glimmer always reasons.

## Strata: Qwen3.8-Flash-Next (125B MoE)

[Strata](https://github.com/Niko1221/Strata) is a separate engine, installed in `F:\AI\strata` (model files in `F:\AI\Strata-data`). It runs only Qwen3.8-Flash-Next and its variants: the Coder, Swift 1.5, and Unsloth's 4-bit builds. It keeps all 512 experts per layer in RAM and caches the busiest ones on both GPUs. The router can't host it, because every router model is a `llama-server` child, so Strata runs as its own server on `http://127.0.0.1:8411`.

**Start it** from the tray (**Start Strata**), or run `F:\AI\llama-serve\start-strata.ps1`. It is not started at login, because loading pins about 39 GB of RAM and can make the PC unresponsive for up to a minute. Once started, it unloads its model after 15 minutes idle, and the next request loads it again (about 45 s). **In Pi** it is `qwen3.8-flash-next-iq2_xs` under the `strata` provider (`~\.pi\agent\models.json`). Thinking levels are off / low / medium / high. **In Hermes** it is the `strata` provider (`hermes --provider strata`). Its first turn takes ~20 s, because Hermes sends a ~32k-token system prompt.

**Only one of the two holds the GPUs at a time:**

- Before Strata loads its model, it unloads the router's model (`before_load` in `F:\AI\strata\strata-iq2_xs.json`, which runs `unload-router-model.ps1`).
- The router has no such hook. When the router starts loading a model while Strata's is loaded, the tray unloads Strata on its next poll (every 2 s), and loading a model from the tray menu unloads Strata first. This needs the tray running. On 2026-10-04 all 3 switches each way worked, with no out-of-memory errors in the router log.

Two sizes are installed. IQ2_XS is the default, and the only one the tray starts; `start-strata.ps1 -Model iq3_xxs` starts IQ3_XXS instead (its quantizer rates it ~99% of BF16, against ~96% for IQ2_XS). Measured 2026-10-04 and 05 (both GPUs, 262k context, images on):

| Check | IQ2_XS | IQ3_XXS |
|---|---|---|
| Code reply (`bench-depth.ps1`, 2k / 52k / 128k context) | 133 / 130 / 117 t/s net; `qwen3.8-27b-dflash2` gets 72 / 63 / 50 | 130 / 120 / 109 t/s |
| Essay reply, same depths | 100 / 93 / 82 t/s net; `qwen3.8-27b-dflash2` gets 46 / 38 / 31 | 92 / 89 / 83 t/s |
| Reading a long prompt | ~3,100-3,200 t/s on new tokens at 52k-128k; ~1,000 t/s on a 2k prompt | ~2,800-2,950 t/s at 52k-128k; ~830-900 t/s on a 2k prompt |
| Experts cached on the GPUs | 93% | 76% |
| Start or reload | 45-50 s; 22.0 / 20.6 GB VRAM, ~39 GB RAM in use, 81 GB committed | ~90 s first start, ~48 s reload; 22.6 / 23.5 GB VRAM, 38-43 GB RAM in use, 88-90 GB committed |
| Free RAM while serving | ~4.6 GB (median) | ~1.8 GB (median); 32 MB at the low point of the first start |
| Tool calls (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`) | 3/3 | 3/3 |
| Image | read "7319" and its color correctly | not tested |

The first IQ3_XXS start of the night, with VS Code and the usual background apps open, decoded at only 15-45 t/s for about 20 minutes while Windows paged memory out (`logs\bench-depth-20261004-2324.txt`). The IQ3_XXS speeds above are from a second run straight after it. A reload right after an unload ran at full speed from its first reply.

Thinking, from `bench-thinking.ps1 -Arms off -Effort medium` (20 HumanEval and 30 MATH tasks, 32,768-token cap):

| | IQ2_XS | IQ3_XXS | `qwen3.8-27b-dflash2` |
|---|---|---|---|
| Code passed | 19/20 | 20/20 | 20/20 |
| Code thinking tokens, median / mean | 346 / 453 | 429 / 435 | 418 / 512 |
| Math passed | 29/30 | 29/30 | 30/30 |
| Math thinking tokens, median / mean | 982 / 3,980 | 654 / 3,718 | 896 / 3,867 |
| Wall time, code + math | 1.8 + 18.0 min | 1.9 + 17.6 min | 3.1 + 34.6 min |

These sets are near the ceiling, so equal scores only show that nothing broke, not that the sizes are equally good. Each size's math miss was one task that thought until the cap (IQ3_XXS on algebra/686, IQ2_XS on precalculus/989). Run twice more, the same task was right both times, so neither failure repeats. Per task, the two sizes think about the same amount (IQ3_XXS / IQ2_XS median ratio 0.99 over 48 tasks).

**IQ2_XS stays the default.** IQ3_XXS scored the same here and is 3-8% slower, and it leaves under 2 GB of RAM free, so it can page and slow down when other apps hold memory, as on its first start. Use it when the PC is otherwise idle.

### Engine 0.1.44.1 (measured 2026-10-18)

The table above is from the older 0.1.39 engine. After the update to 0.1.44.1 (2026-10-16), the installed IQ2_XS config decoded at 52 t/s on code at every depth. The engine's auto layer split had left the desktop card (Strata's CUDA1) with 160 MiB of VRAM free, and its start log said so (`CUDA1: 160 MiB of VRAM free with everything loaded - LOW`; under WDDM a full card pages). The IQ2_XS config now carries two more flags in `args`, `--vram-reserve-later-mib 1308` (the value that log line suggested) and `--pipeline-windows 2`.

Both GPUs, 262k context, images on, `bench-depth.ps1` (5 measured runs per cell, thinking off), memory saver on, ~4 GB RAM free. Four configs, three rounds each, interleaved (12 `bench-depth.ps1` runs), with a Strata restart before every config in every round. Median net t/s over the three rounds:

| Config | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (2k / 52k / 128k) |
|---|---|---|---|
| A: as installed | 52 / 52 / 51 | 44 / 43 / 43 | 830 / 1,510 / 1,530 |
| B: A + `--vram-reserve-later-mib 1308` | 170 / 158 / 152 | 124 / 114 / 107 | 1,250 / 3,620 / 3,570 |
| C: B without `--remote-expert-opt` | 171 / 160 / 148 | 123 / 114 / 108 | 1,260 / 3,610 / 3,580 |
| D: B + `--pipeline-windows 2` (now installed) | 188 / 168 / 162 | 125 / 114 / 107 | 1,260 / 3,600 / 3,570 |

- **A to B is the fix**: 3.0-3.3x on code and 2.5-2.8x on prose. VRAM in use went from 23,988 / 21,217 MiB to 23,560 / 22,015 MiB (GPU 0 / GPU 1).
- **B and C are the same config**: `--remote-expert-opt` does nothing on a plain layer split, as upstream's docs say. The 1-3% gaps between them are the run-to-run noise.
- **D beats B on code only**: +11% / +6% / +7%. The slowest D round beat the fastest B round at every depth (180 vs 174, 167 vs 159, 161 vs 153). Prose and prompt reading are unchanged. Draft acceptance on code fell from ~0.80 to ~0.73 and decode still rose.
- **Compared with 2026-10-04** (133 / 130 / 117 code, 100 / 93 / 82 essay, ~3,100-3,200 prompt t/s at 52k-128k), D is 41% / 29% / 38% faster on code and 25% / 23% / 30% on prose (from the table's rounded values), on a newer engine and a different system state, so this is not one variable.
- Tool calls on D: 5/5 (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`). On 2k and 30k prompts, D decodes 184 / 167 on code and 125 / 116 on prose (one round).
- Not done: IQ3_XXS on 0.1.44.1 (its config has neither flag and probably needs the reserve too), and `--adapt-async 1`.
- Every start logs `Windows commit: 0.3-1.3 GiB left of a 118 GiB limit` with a warning that Strata can exit with no message at the limit (upstream #1607). The engine started 14 times that day and none of those runs died at the limit. Two more launches never reached the engine (the log has no engine-start line; see the restart note in [AGENTS.md](./AGENTS.md)), so they say nothing about the limit either way.

To change size, context or GPUs, run `F:\AI\strata\SETUP.bat`. It rewrites `strata-*.json`, so add `idle_unload_s`, `before_load`, `sampling` and the two `args` flags back afterwards.

**Sampling.** Both configs set `"sampling": {"temperature": 1.0, "top_p": 0.95, "top_k": 20, "min_p": 0.0}`, the values the router's Qwen3.8 presets use. Without that block Strata decodes greedy (temperature 0) when a client sends no temperature, and greedy decoding is reported to loop on tool calls. A client that sends its own values, such as Pi, still wins. `F:\AI\strata\UPDATE.bat` updates the engine.

### Memory saver

Strata leaves only ~4.6 GB of RAM free on IQ2_XS and ~1.8 GB on IQ3_XXS, so other apps' memory decides whether Windows starts paging. **Memory saver** in the tray measures what is running and shows a checklist with each app's private memory and what you lose while it is closed. Tick what to close; the ticks are saved in `memory-saver.choices.json` (not tracked) and preselected next time. Click the menu item again to reopen exactly what it closed. While it is on, **Memory saver: close apps again...** opens the same checklist, showing what runs now, so you can close apps you reopened in the meantime and change the ticks; the next reopen still starts each app once.

| Group | Rows | How it closes and reopens |
|---|---|---|
| Tested apps | WSL, Docker Desktop, Discord, OneDrive | WSL: `wsl --shutdown`, stopping Docker Desktop first if it runs and restarting it, with its running containers, on reopen. Docker: `docker desktop stop` / `start`, then restarts the containers that were running (`boxel-sonarqube` has no restart policy). Discord and OneDrive restart minimized. |
| Other apps | Anything in your session above 100 MB that is safe to close, such as Firefox or PowerToys, one row per install folder | Asked to close, then ended like Task Manager's End task after 10 s; reopened with the command line it was started with. Shells, interpreters, VS Code, Claude, this repo's servers, drivers' helpers, security software and Store apps are never offered. |
| VS Code extensions, services | Extensions' language servers above 100 MB; third-party services above 20 MB | Information only. Free an extension in VS Code; a window opened before you disabled it keeps it until **Developer: Reload Window**. Stopping a service needs admin. |

Measured 2026-10-10: WSL with Docker and SonarQube running freed 6.0 GB (27.4 → 33.4 GB free); Discord, OneDrive and Docker Desktop (VM included) freed 3.3 GB. NVIDIA App and NVIDIA Overlay are not offered: NVIDIA's services restart them within a minute. Turn the overlay off in NVIDIA App's settings instead.

From PowerShell: `.\memory-saver.ps1 -Mode Status` lists the candidates with the saved ticks, `-Mode On` closes the ticked apps (run again while on, it closes the ones reopened since), `-Mode Reapply` does only the second part and refuses when the saver is off, `-Mode Off` reopens them, and `-Mode List` prints the candidates as JSON.

## ninfer-3090 (measured 2026-10-18)

Don-Chad/ninfer-3090 v0.16.1 is a third engine: a Windows build of ninfer for RTX 3090s, installed in `F:\AI\ninfer-3090` with its own `.ninfer` weights (not GGUF) and one GPU per model. It is not wired into the tray, the router or any client. Nothing here starts it at login. To run Qwen3.8-27B on the headless card, first stop Strata and stop the router with `stop-router.ps1`. Unloading the router's model is not enough: any request from Pi or VS Code reloads a two-GPU preset onto GPU 1 beside ninfer, both slow down, and the tray unloads only Strata.

```powershell
$env:CUDA_DEVICE_ORDER='PCI_BUS_ID'; $env:CUDA_VISIBLE_DEVICES='1'; $env:NINFER_GRAFTS='off'
$env:NINFER_PORT='8412'; $env:NINFER_MODEL_DIR='F:\AI\ninfer-3090\models'
& 'F:\AI\ninfer-3090\ninfer-rtx3090-windows-x64-0.16.1-rtx3090\run.bat' qwen38-27b
# After ninfer stops: these variables persist in the shell, and CUDA_VISIBLE_DEVICES=1 would hide GPU 0 from a router or Strata started from it.
Remove-Item Env:CUDA_DEVICE_ORDER, Env:CUDA_VISIBLE_DEVICES, Env:NINFER_GRAFTS, Env:NINFER_PORT, Env:NINFER_MODEL_DIR
```

Set `NINFER_PORT` every time: the default, 8080, belongs to signal-cli. `NINFER_GRAFTS=off` stops the downloader from fetching a private graft through the Hugging Face login. The server is ready in about 10 s (`/health` answers 200), speaks the OpenAI API at `http://127.0.0.1:8412/v1`, and returns llama.cpp-style `timings`, so `bench-depth.ps1 -BaseUrl http://127.0.0.1:8412` works unchanged.

`bench-depth.ps1`, thinking off, 5 measured runs per cell, one round each, not interleaved (a second, swapped round was cut short when the PC was needed), so read it as an estimate. Net t/s:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k |
|---|---|---|
| Router `qwen3.8-27b-dflash2` | 81 / 63 / 52 | 50 / 41 / 33 |
| ninfer `qwen38-27b` | 204 / 171 / 146 | 100 / 89 / 76 |

The two rows are not the same model file:

| | Router | ninfer |
|---|---|---|
| Weights | UD-Q5_K_XL GGUF, 2 GPUs | ~4-bit conversion, 17.2 GiB, GPU 1 alone |
| KV cache | q8_0, 262k context | rk4v4 (4-bit keys), up to 188k context |
| Drafter | DFlash2, 4 draft tokens | DFlash2, 7 draft tokens, plus n-gram copying |
| Sampling | preset: temp 1.0, top-p 0.95, top-k 20 | the model's registered preset, with `run.bat`'s guard of min-p 0.03 and presence penalty 0.5 |

- **No quality comparison was run.** The speed gap is real, but part of it is a smaller quantization on one card.
- **Replies that stop on their own** (3 coding and explanation prompts, 2 measured runs each, ninfer only): median 153 t/s, range 129-202. `bench-depth.ps1` forces full-length replies, which can help n-gram drafting; the router was not rerun this way.
- **Tool calls:** `test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8412` passed 5/5, at 1-4 s per run. The router presets think by default, so this is not a like-for-like timing.
- **Flash-Next (`qwen38-flash-next`) does not start.** It must pin 34.7 GiB of host memory, which Windows maps into the GPU's address space, and the allocation fails with `cudaErrorMemoryAllocation` (it reported 18.9 GiB free) with `CUDA_VISIBLE_DEVICES` set to 1 and again to 0, with 47.6 GiB of RAM free. Its release notes say Flash-Next was not part of their benchmark.
- **Not tested:** images, long sessions, a client talking to it (Pi, Hermes, VS Code).

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

### Overthinking logit bias (tested 2026-09-28, not adopted)

A -2 logit bias on "wait", "maybe", "however" and similar words (arXiv 2606.00206) shortens thinking. `bench-thinking.ps1` measured it at medium effort on 30 math and 20 coding tasks. On `qwen3.8-27b-dflash2` the paper's 49-token list cut math thinking by 13% (median) and 42% (mean) and kept every answer; coding was unchanged. On the Swift lanes it saved nothing, and unbiased Swift 1.5 already thinks as little as the biased base model. For less thinking, use Swift.

It can't go in `presets.ini` on build b10951: a `logit-bias` value keeps only its first entry (`466-2,694-2` biases only 466), and a repeated key keeps only the last. A client can still send `logit_bias` per request (OpenAI format), which applies every entry.

To try it on another model, add its preset, then run `.\bench-thinking.ps1 -Model <name> -ListTokens` to see which of its tokens would be biased (the words are resolved to that model's own token ids), then `.\bench-thinking.ps1 -Model <name>` for the full A/B (about 1-2 hours for a 27B model).

## When something is wrong

| Symptom | Check |
|---|---|
| Pi lists no llama.cpp models | The router isn't running: start it. |
| A model is suddenly very slow (several times slower than usual) | It didn't fit in VRAM and Windows spilled into system RAM. Run `nvidia-smi`: a card near 24,576 MiB used is the culprit. Close other GPU programs, or lower `ctx-size` for that model. |
| Speeds or memory look off after changing a preset | `Select-String -Path F:\AI\llama-serve\logs\server.log -Pattern 'retrying without pipeline'`. Any hit means the model loaded in a degraded mode; the preset needs less context or a different `tensor-split`. |
| A model fails to load | Read the end of `F:\AI\llama-serve\logs\server.log`. The previous boot's log is `server.prev.log`. |
| A router model fails to load while Strata is running | The tray wasn't running to unload Strata. Use **Unload Strata model** in the tray, or `stop-strata.ps1`, and retry. Strata's own log: `logs\strata.log`; engine log: `F:\AI\strata\strata-iq2_xs.log`. |
| Port 8410 is busy | Another router is already running (for example one started in a window). Stop it first. |

## Files

| File | Purpose |
|---|---|
| `presets.ini` | The model list and each model's settings. |
| `start-router.ps1` | Starts the router: pinned llama.cpp build, port 8410, logs to `logs\server.log`. |
| `stop-router.ps1` | Stops the router and any loaded model. |
| `start-strata.ps1` / `stop-strata.ps1` | Start Strata on port 8411 (after unloading the router's model), logging to `logs\strata.log`, and stop it. |
| `memory-saver.ps1` | Lists, closes and reopens background apps for the tray's Memory saver. |
| `unload-router-model.ps1` | Unloads the router's model and waits until its VRAM is free. Strata runs it before every load. |
| `tray\LlamaTray.cs` | The tray app. It starts and stops the router and Strata through the scripts above, and unloads Strata when a router model loads. |
| `install-tray.ps1` | Builds the tray app into `tray\bin\` (with the C# compiler built into Windows, no SDK), adds the Start menu shortcut, points the logon task at it, and launches it. Re-run after editing `LlamaTray.cs`. |
| `bench-depth.ps1` | Speed bench at ~2k / 52k / 128k context, for choosing between drafter lanes. Results in `logs\bench-depth-*.txt`. |
| `bench-thinking.ps1` | Thinking-token and accuracy A/B of logit-bias arms (`bench\logit-bias-arms.json`) on math and code tasks (`bench\tasks-*.jsonl`). Results in `logs\bench-thinking-*.txt`. |
| `test-tool-calls.ps1` | Checks that every model's tool calls parse and round-trip. |
| `link-hub-models.ps1` | Hardlinks Unsloth Studio downloads into the library. |
| `docs/ubuntu-setup.md`, `plans/ubuntu-dual-boot.md` | Guide and plan for an Ubuntu 26.04 dual boot on the spare NVMe, to measure Strata on Linux. |

Why port 8410: 8080 is taken by signal-cli in WSL, and the club-3090 WSL stack uses 8020-8213. Pi finds the router through the user environment variable `LLAMA_BASE_URL=http://127.0.0.1:8410/v1`.

The llama.cpp build lives in `F:\AI\llama.cpp\b10951\`. To upgrade, unzip a new build next to it and change `$Build` in `start-router.ps1`, so rolling back is a one-line change.
