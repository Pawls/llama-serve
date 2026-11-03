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
| Reserve GPU 0 for ComfyUI | Serves only presets pinned to CUDA1, stops Strata and blocks it from starting; restarts the router. Saved as `ReserveGpu0` in `tray-settings.json`, which `start-router.ps1` reads into `logs\presets.run.ini` |
| Strata status, Start / Stop Strata, Unload Strata model, Open Strata app / log | Qwen3.8-Flash-Next on its own server; see [Strata](#strata-qwen38-flash-next-125b-moe) |
| Memory saver | Closes the background apps you tick to give Strata more RAM; click again to reopen them. See [Memory saver](#memory-saver) |
| ninfer status, Start / Stop ninfer, Open ninfer log | Qwen3.8-27B on GPU 1 alone through ninfer-3090; starting it stops the router and Strata, and stopping it starts the router again. See [ninfer-3090](#ninfer-3090-measured-2026-10-18) |
| Router models: free GPUs when idle, Strata: unload when idle | How long a model sits unused before it frees the GPUs: never, 15 / 30 minutes, 1 / 2 / 4 hours. Defaults: router never, Strata 15 minutes. Saved in `tray-settings.json` and used at the next router or Strata start |
| Start with Windows | Turns the logon task on or off |
| Quit | Stops the router, Strata and ninfer, and closes the tray |

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
| `qwen3.8-27b-single` | The default model's Q4_K_XL on one card, text only (measured 2026-10-31: peak 21.7 GiB of 24) | 131k | no | on | ~39 t/s (30 at 52k, 22 at 120k) | GPU1 only |
| `gemma4-26b-a4b-single` | `gemma4-26b-a4b` on one card, text only (measured 2026-10-31: peak 21.1 GiB of 24) | 262k | no | off | ~100 t/s (38 at 244k) | GPU1 only |

**Keeping GPU 0 for ComfyUI:** tick **Reserve GPU 0 for ComfyUI** in the tray. The router restarts and lists only the GPU1-only presets (those whose `device` and `spec-draft-device` lines all say `CUDA1` in `presets.ini`). With `muse-glimmer-30b` loaded and its image projector, GPU 0's memory did not change (4,289 to 4,276 MiB, 2026-10-31). Ticking it also stops Strata, and Strata cannot start (from the tray or `start-strata.ps1`) while it is ticked. Untick it to get the full list back. Clients that name a hidden preset get an error until then.

Names ending in `-dflash`, `-dflash2` or `-mtp` are the same model plus a small "drafter" that guesses tokens ahead for the big model to check. The output is the same model's; only speed changes. Speeds for these come from the 2026-09-14 benchmark (5 runs each, coding and essay prompts). A drafter that guesses code well can guess prose badly, so the `-dflash` coder and 26B lanes are for coding sessions.

"Thinking by default" is the preset's setting. **Pi overrides it** on the Qwen3.8 models: Pi sends its own thinking level, and Pi's choice wins. Press `Shift+Tab` to change it (off for fast answers); that also sets the effort (low / medium / xhigh). On the other presets Pi sends nothing and the preset decides (see the pins above). In Hermes, `hermes --provider llama-router` with `/reasoning low|medium|xhigh` sends a top-level `reasoning_effort`, which the router passes to the template like the kwarg (checked 2026-10-19 with `/apply-template`; the Hermes request itself was read from its code, not captured). The template rejects any other value with an HTTP 500; the local `pawl-patches` Hermes plugin lowers other levels to the nearest one the provider declares (`high` becomes `medium`). `qwen3-coder-30b` has no thinking mode, and Muse-Glimmer always reasons.

## Strata: Qwen3.8-Flash-Next (125B MoE)

[Strata](https://github.com/Niko1221/Strata) is a separate engine, installed in `F:\AI\strata` (model files in `F:\AI\Strata-data`). It runs only Qwen3.8-Flash-Next and its variants: the Coder, Swift 1.5, and Unsloth's 4-bit builds. It caches the busiest experts on both GPUs and keeps the rest in RAM (Swift and IQ3_XXS hold every other expert there; UD-Q4_K_XL holds a 36 GiB budget and reads the coldest from the SSD). The router can't host it, because every router model is a `llama-server` child, so Strata runs as its own server on `http://127.0.0.1:8411`.

**Start it** from the tray (**Start Strata: Swift 1.5 IQ3_S**, **Flash-Next IQ3_XXS** or **Flash-Next UD-Q4_K_XL**), or run `F:\AI\llama-serve\start-strata.ps1` (Swift by default; `-Model iq3_xxs` or `-Model unsloth-ud-q4_k_xl` for the others). It is not started at login, because loading locks 17-36 GiB of RAM and can make the PC unresponsive for up to a minute (UD-Q4_K_XL leaves ~4 GB free, so the tray warns when the memory saver is off). Once started, it unloads its model after 15 minutes idle, and the next request loads it again (about 1.5-2 minutes for Swift and IQ3_XXS, ~70 s for UD-Q4_K_XL). **In Pi** it is `swift-1.5-iq3_s`, `qwen3.8-flash-next-iq3_xxs` or `qwen3.8-flash-next-unsloth-ud-q4_k_xl` under the `strata` provider (`~\.pi\agent\models.json`); Strata answers with whichever size is loaded, whichever name you pick. Thinking levels are off / low / medium / high. **In Hermes** it is the `strata` provider (`hermes --provider strata`). Its first turn takes ~20 s, because Hermes sends a ~32k-token system prompt.

**Only one of the two holds the GPUs at a time:**

- Before Strata loads its model, it unloads the router's model (`before_load` in `F:\AI\strata\strata-<size>.json`, which runs `unload-router-model.ps1`).
- The router has no such hook. When the router starts loading a model while Strata's is loaded, the tray unloads Strata on its next poll (every 2 s), and loading a model from the tray menu unloads Strata first. This needs the tray running. On 2026-10-04 all 3 switches each way worked, with no out-of-memory errors in the router log.

Swift 1.5 IQ3_S, IQ3_XXS and Unsloth's UD-Q4_K_XL are installed, and the tray has a start item for each. **Swift 1.5 IQ3_S is the default since 2026-11-01**; the reasons and the numbers for all three are in [Swift 1.5, resident experts and UD-Q4_K_XL](#swift-15-resident-experts-and-ud-q4_k_xl-measured-2026-11-01). IQ2_XS was deleted on 2026-11-01 to free disk space; its numbers below are kept for comparison. IQ3_XXS was the default from 2026-10-19 (its quantizer rates it ~99% of BF16, against ~96% for IQ2_XS). Newer numbers for IQ3_XXS and IQ2_XS are in [Engine 0.1.45](#engine-0145-iq3_xxs-against-iq2_xs-measured-2026-10-19) and, for the installed engine, [Engine 0.1.55](#engine-0155-strata-029-measured-2026-11-01). Measured 2026-10-04 and 05 (both GPUs, 262k context, images on):

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

**On 2026-10-04 IQ2_XS stayed the default.** IQ3_XXS scored the same here and was 3-8% slower, and it left under 2 GB of RAM free, so it could page and slow down when other apps held memory, as on its first start. With engine 0.1.45 and a fixed page file it leaves more room, and it became the default on 2026-10-19 (see [Engine 0.1.45](#engine-0145-iq3_xxs-against-iq2_xs-measured-2026-10-19)).

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

### Engine 0.1.45: IQ3_XXS against IQ2_XS (measured 2026-10-19)

Both GPUs, 262k context, images on, `bench-depth.ps1` (5 measured runs per cell, thinking off), memory saver on. Three rounds, sizes interleaved and the order swapped each round, a Strata restart before every run. IQ3_XXS now carries the IQ2_XS fixes (`--vram-reserve-later-mib 1310`, `--pipeline-windows 2`); its start log asked for 1310, the same as IQ2_XS. Median net t/s over the rounds:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (2k / 52k / 128k, code) | Free RAM while serving, median (min) |
|---|---|---|---|---|
| IQ2_XS | 161 / 153 / 146 | 116 / 107 / 101 | 1,270 / 3,600 / 3,570 | 6.3-7.1 GB (5.5) |
| IQ3_XXS | 139 / 136 / 128 | 105 / 99 / 96 | 1,020 / 3,110 / 3,270 | 2.5-4.1 GB (1.1) |
| IQ3_XXS on 2026-10-04 (engine 0.1.39, neither flag) | 130 / 120 / 109 | 92 / 89 / 83 | | ~1.8 GB |

- **IQ3_XXS is 7-17% faster than on 2026-10-04** and 11-14% slower than IQ2_XS on code (5-10% on prose). Both sizes stay above 95 t/s at 128k.
- In these runs IQ3_XXS's start log still warned "only 404-546 MiB of VRAM free at the start of this request" on the desktop card. `--vram-reserve-mib` went from 700 to 1344, the value the log suggested. One more round with it decoded the same (140 / 135 / 130 code, 105 / 100 / 97 essay), with 1,258 MiB free at load and no warning. Tool calls 3/3.
- **IQ2_XS is slower than on 2026-10-18** (161 vs ~186 code at 2k, same config, same VRAM split, draft acceptance 0.71-0.76 vs 0.69-0.73). The engine went 0.1.44.1 -> 0.1.45 in between; the day's system state differs too, so the cause is not isolated. 0.1.44.1 is in `F:\AI\strata\engine\.previous`.
- **Windows commit is the real limit, not RAM.** Every run of both sizes had 0.2-0.5 GB of commit headroom at its low point, with the system-managed page file growing the limit to 129-135 GB. No engine died. The start log keeps warning about upstream #1607 (exit at the limit with no message).
- **Recheck with a fixed page file** (C:, 64-96 GB, set the same day). One IQ3_XXS start, memory saver off with the day's apps open: the start log said `Windows commit: 27.0 GiB left of a 127.8 GiB limit` and gave no warning; 1,258 MiB of VRAM free, 901 MiB on the desktop card. Sampled every 2 s through a tool-call test (3/3) and a 128k `bench-depth.ps1` run (2 measured runs: 120 code, 88 essay t/s, one round, so an estimate): free RAM 8.7 GB at the low point, commit headroom 24.7 GB at the low point.

**IQ3_XXS is the default from 2026-10-19.** It is the more accurate size, it stays above 95 t/s at 128k in the interleaved runs, and with the fixed page file the commit limit is no longer close. IQ2_XS stayed in the tray for when speed or free RAM mattered more, until it was deleted on 2026-11-01.

### Engine 0.1.55 (Strata 0.2.9, measured 2026-11-01)

`UPDATE.bat` took the install from engine 0.1.45 (code at 0.2.0-beta) to engine 0.1.55 (code at v0.2.9) on 2026-11-01. It fetched the engine from Strata's signed update feed with no workaround, and both `strata-*.json` files and both `run-*.bat` files came through byte-identical. 0.1.45 is in `F:\AI\strata\engine\.previous`.

Both GPUs, 262k context, images on, `bench-depth.ps1` (5 measured runs per cell, thinking off), memory saver off (about 48 GB of RAM free before the first start). Three rounds per arm, arms interleaved with the order rotated each round, a Strata restart before every run. The 0.1.45 rows were run the same night just before the update, because that night's system state was slower than 2026-10-19's (IQ3_XXS code at 2k: 131 against 139). Median net t/s over the rounds:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code, 2k / 52k / 128k) | Free RAM while serving, median (min) |
|---|---|---|---|---|
| IQ3_XXS, 0.1.45 | 131 / 121 / 118 | 95 / 89 / 85 | 980 / 2,950 / 3,100 | 5.3-7.7 GB (4.9) |
| IQ3_XXS, 0.1.55 | 144 / 127 / 135 | 106 / 98 / 100 | 920 / 3,000 / 3,100 | 5.9-6.0 GB (5.4) |
| IQ3_XXS, 0.1.55 without `--pipeline-windows 2` (installed) | 143 / 131 / 134 | 108 / 99 / 100 | 930 / 3,020 / 3,130 | 6.0-6.1 GB (5.5) |
| IQ2_XS, 0.1.45 | 142 / 133 / 127 | 102 / 94 / 91 | 1,200 / 3,430 / 3,400 | 11.0-11.4 GB (10.7) |
| IQ2_XS, 0.1.55 | 149 / 142 / 126 | 114 / 103 / 94 | 1,150 / 3,450 / 3,400 | 6.9-7.0 GB (6.4) |
| IQ2_XS, 0.1.55 without `--pipeline-windows 2` (installed) | 159 / 142 / 126 | 114 / 105 / 94 | 1,140 / 3,430 / 3,400 | 6.7-6.8 GB (6.0) |

- **Decode is 12-16% faster on 0.1.55** on both sizes and both kinds (IQ3_XXS code decode 132 / 123 / 119 -> 149 / 141 / 138). The new engine and the new code came together, so this is not one variable. The likeliest source (not isolated) is the RTX 30 decode table that 0.2.0 turned on by default (the start log lists its 9 switches; `STRATA_SM86_PERF=0` turns them off).
- **Net t/s gains less at 52k and 128k than decode does**, because the engine now keeps a conversation cache by default (next bullet).
- **The conversation cache costs 0.4-1 s per long reply.** 0.1.55 sizes a cache in RAM from what is free at start (2,048 MiB on IQ3_XXS, 3,584 MiB on IQ2_XS) and copies each conversation into it after the reply: ~0.4-0.5 s at 52k, and on IQ2_XS ~1 s at 128k (IQ3_XXS skips the copy at 128k because the snapshot exceeds its budget). That is why IQ2_XS lost 4 GB of free RAM. It pays off when requests alternate between conversations (sub-agents, two clients): a parked 52k conversation comes back without re-reading ~17 s of prompt. A separate A/B the same night, three interleaved rounds at 52k and 128k with `--pipeline-windows 2` still set, ran `--conversation-cache-mib 0` against the default:

  | | Code 52k / 128k | Essay 52k / 128k | Free RAM, median |
  |---|---|---|---|
  | IQ3_XXS, cache on (default) | 129 / 135 | 98 / 99 | 5.6 GB |
  | IQ3_XXS, `--conversation-cache-mib 0` | 143 / 136 | 103 / 98 | 7.2 GB |
  | IQ2_XS, cache on (default) | 141 / 126 | 104 / 93 | 6.8 GB |
  | IQ2_XS, `--conversation-cache-mib 0` | 149 / 144 | 110 / 105 | 9.7 GB |

  Both configs carry `--conversation-cache-mib 0` since 2026-11-01, for one conversation at a time. Remove it from `args` to get the cache back when sub-agents or two clients alternate.
- **`--pipeline-windows 2` no longer helps and was removed from both configs on 2026-11-01.** On 0.1.44.1 it added 6-11% on code; on 0.1.55 IQ3_XXS is within 3% either way, and IQ2_XS is 6% faster without it on 2k code (159 against 149, every round) and the same elsewhere. The 0.1.55 start log notes that the draft layer sits on the last card here, and that in that arrangement the pipeline lost to the serial loop on 2x R9700 before 0.1.44. Without it the expert cache gains 188 slots.
- **Cold prompt reading did not change.** 0.2.7's layer-major prefill (upstream's 32K-prompt first token went from 141.8 to 27.7 s on 2x A4000) never runs here: every long prompt logs `layer-major off for this prompt: no VRAM for a super-chunk (1100 MiB free)`. It saves expert reads from RAM, and on this PC ~99% of routed experts are already in the GPU caches, so the expected gain is small anyway.
- **GPU roles are as before.** The launcher logs `cards in stage order: GPU 1, then GPU 0`: the headless card runs layers 0-22, and the desktop card runs layers 23-47, the head and the draft layer. With everything loaded the start log reports 1,172 MiB of VRAM free and 758 MiB on the desktop card (installed config), with no LOW warning, so the reserves set on 2026-10-19 still fit.
- Tool calls 3/3 on both sizes (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`), and 3/3 again on IQ3_XXS without `--pipeline-windows 2`. No engine died. Windows commit headroom stayed at 16.9 GB or more (fixed page file), and the start log reports `Windows commit: 22.8-23.3 GiB left of a 127.8 GiB limit`.

### Unsloth UD-Q4_K_XL, quick check (2026-11-01)

Unsloth's 4-bit file of the same model: 71.7 GiB of experts against IQ3_XXS's 43 GB, and the same top token as Q8_0 93.1% of the time against 84.5% for IQ3_XXS (Strata's `docs\QUANTS.md`, measured upstream on AMD). It runs on this PC on both GPUs: the GPU caches hold 9,867 experts (~29 GiB, about 97% of the routed mass by the profile), a 36 GiB RAM budget the next hottest, and the last ~6 GiB come from the NVMe drive. Start: `.\start-strata.ps1 -Model unsloth-ud-q4_k_xl`, loaded in ~56 s.

Engine 0.1.55, both GPUs, 262k context, images on, conversation cache off, memory saver off. `bench-depth.ps1` with 1 warm and 3 measured runs per cell, one round, no interleaving, so treat the numbers as a first reading. IQ3_XXS figures are the median rows from the 0.1.55 table above, without `--pipeline-windows 2` (at 52k, the cache-off arm of the A/B):

| | Code 2k / 52k, net t/s | Essay 2k / 52k, net t/s | Cold prompt read, t/s (code, 2k / 52k) |
|---|---|---|---|
| IQ3_XXS | 143 / 143 | 108 / 103 | 930 / 3,020 |
| UD-Q4_K_XL | 53 / 53 | 57 / 58 | 190 / 910 |

- About 2.7x slower on code and 1.8-1.9x on prose; prompts read 3-5x slower. Draft acceptance is about the same as IQ3_XXS's, so the gap comes from where the experts sit: 97.6% of decode lookups hit the GPU caches (IQ3_XXS: 99.7%), and each reply reads a few thousand experts from the file.
- The start log warns that the prompt path's borrowed cache slots have no RAM copy, so their experts stream from the SSD on every prompt chunk. That explains the slow 2k prompt read.
- Free RAM while serving: ~3 GB (36 GiB budget locked). Windows commit headroom 18.4 GiB. SonarLint (two Java processes, ~2.7 GB working set when checked afterward) was running, so without it expect a few GB more.
- Tool calls 3/3 (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`).
- The desktop card first started with 0 MiB free (LOW); `--vram-reserve-mib 1344` plus `--vram-reserve-later-mib 1728` left 759 MiB free on it and 1,148 MiB on the other.

### Swift 1.5, resident experts and UD-Q4_K_XL (measured 2026-11-01)

Four arms on engine 0.1.55, both GPUs, 262k context, images on, conversation cache off, memory saver off with the usual apps closed and SonarLint off. `bench-depth.ps1` with 1 warm and 3 measured runs per cell, thinking off; three rounds with the arm order rotated, a Strata restart before every run (12 runs, 9 measured replies per cell). Free RAM was sampled every 10 s during each run. Median net t/s over all 9:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code, 2k / 52k / 128k) | Free RAM while serving, median (min) | Load |
|---|---|---|---|---|---|
| IQ3_XXS, full RAM copy (installed until 2026-11-01) | 146 / 140 / 138 | 107 / 102 / 99 | 1,000 / 3,030 / 3,180 | 7.4-7.6 GB (6.9) | 40-47 s |
| IQ3_XXS, `--resident-experts` (installed) | 142 / 135 / 135 | 106 / 103 / 98 | 1,160 / 3,500 / 3,580 | 27.8-28.6 GB (27.4) | 110-112 s |
| Swift 1.5 IQ3_S, `--resident-experts` (default) | 130 / 125 / 125 | 93 / 91 / 90 | 630 / 1,270 / 1,650 | 21.1-22.1 GB (20.7) | 86-117 s |
| UD-Q4_K_XL, 36 GiB RAM budget | 60 / 62 / 61 | 62 / 61 / 60 | 210 / 980 / 1,070 | 4.2-4.4 GB (4.0) | 67-70 s |

- **`--resident-experts` frees ~21 GB of RAM on IQ3_XXS at no decode cost.** The full copy keeps all 40 GiB of experts in RAM although the GPU caches already hold ~30 GiB of them; resident mode keeps only the other 17.05 GiB, all of it pinned (the full copy pins only 8 GiB under WDDM on a split). Decode is within 1-4% (inside the run-to-run spread), prompt reads are 12-16% faster, and a load takes ~110 s instead of ~45 s. Both configs carry it since 2026-11-01.
  - *Why the full copy bought nothing.* Decode reads the GPU's copy of a cached expert, so the RAM copy of those ~30 GiB sat idle. Its one use is that an expert the adaptive tier drops from a GPU is still in RAM; in resident mode the two swap places instead (the dropped one is copied back over PCIe into the incoming one's slot), which did not show in decode. The full copy was also worse for prompts: its start log said `locked 33352 MiB via working-set minimum + VirtualLock; cudaHostRegister limited to 8 GiB by the engine (multi-GPU under WDDM ...)`, so only 8 GiB of it could reach the GPU directly, while resident mode's copy is all `page-locked and mapped`.
  - *Windows could not take it back either.* `VirtualLock` and CUDA page-locking both keep pages in RAM; under pressure Windows pages out other programs instead, and Strata has no release-and-reload path for a locked copy. Only file-mapped experts (`--mmap-experts`, which a RAM budget uses for its cold tail) can be dropped by the OS and re-read from the SSD. The RAM comes back when the model unloads (idle unload, the tray, `stop-strata.ps1`).
  - *Why UD-Q4_K_XL could not gain the same.* Its RAM budget mode never kept a duplicate: the start log says `RAM budget 36.00 GiB: 12339 of the 43.37 GiB of experts the GPU cache does not hold`. Its experts are about twice the size (~3.0 MiB each in VRAM against ~1.5 for IQ3_XXS and ~1.8 for Swift), so the same ~28-30 GiB of GPU cache holds half as many, and 43 GiB is left for RAM against 17 and 23. Levers, none measured: a smaller `--resident-budget-gib` (each GiB less is a GiB more read from the SSD), or UD-IQ4_XS (55.4 GiB of experts, ~26 GiB left for RAM; an estimate, not installed).
- **Swift 1.5 IQ3_S** (84 GB, a fine-tune with shorter thinking) needs resident mode: setup warned that IQ3_S with a full copy needs ~78 GB of RAM at this context. Against IQ3_XXS in the same mode it decodes 7-9% slower on code and 8-12% on prose, because IQ3_S experts are larger (8,436 cache slots against 10,719) and its decode hit rate is 95-97% against ~99%. The GPU caches are pre-filled from `expert-profile.bin`, which was built on the original model, not Swift.
- **Swift reads prompts at a third to a half of IQ3_XXS's speed** (36-54%; a 128k prompt takes ~78 s instead of ~36 s). Its start log says `page-locking refused (out of memory)`: the 24.59 GiB RAM copy is locked with `VirtualLock` but not pinned, and IQ3_XXS's 17 GiB copy is pinned. Unpinned copies to the GPUs are slower. `STRATA_PF_FUSED=1` (installed since 2026-11-02) makes the copy pin and reads prompts 9-22% faster; see [Swift 1.5 CPU and kernel switches](#swift-15-cpu-and-kernel-switches-measured-2026-11-02).
- Thinking was off, so this bench does not show Swift's shorter thinking; it measures only speed per token. Quality was not compared here. Upstream's `docs\QUANTS.md` puts ISTA's IQ3_S at 87.7% same top token as Q8_0 against 84.5% for IQ3_XXS; no number for Swift.
- **UD-Q4_K_XL** confirms the quick check: ~60 t/s at every depth, about 2.3x slower than IQ3_XXS on code and 1.7x on prose, with ~4 GB of RAM free.
- Every arm passed tool calls 3/3 at its first start (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`), no run had an error, and no start log reported LOW VRAM. VRAM with everything loaded was ~23.5 / 22.6 GiB on every arm.
- With the cache off, net t/s now tracks decode at 52k and 128k (IQ3_XXS code: 140 net against 142 decode at 52k), and the start logs have no conversation-cache line.

**Swift 1.5 IQ3_S is the default from 2026-11-01** (Paul's choice for its shorter thinking). It holds 125 t/s on code and 90 on prose at 128k with 21 GB of RAM free. IQ3_XXS stays in the tray for long prompts and the most speed, and UD-Q4_K_XL for the most accurate quant.

To change size, context or GPUs, run `F:\AI\strata\SETUP.bat`. Up to 0.2.0 it rewrote `strata-*.json` and dropped what had been added by hand. The 0.2.9 setup code keeps every key it does not write (`idle_unload_s`, `before_load`, `sampling`) and carries hand-added `args` flags into a `user_args` list; that is from reading upstream `setup.py`, not yet seen on this PC. After a setup run, check that those keys and flags are still there: `--vram-reserve-mib`, `--vram-reserve-later-mib` and `--conversation-cache-mib 0` on every size, plus `--resident-experts` on IQ3_XXS and Swift, `--adapt-async 1` and `--prefill 2048` on UD-Q4_K_XL, and on Swift `--adapt-async 1`, `--prompt-cache-spread`, the `env` key `STRATA_PF_FUSED` and the `expert_profile_save` key (values in [AGENTS.md](./AGENTS.md)).

### Swift 1.5 CPU and kernel switches (measured 2026-11-02)

Four switches from `F:\AI\strata\docs\DETAILS.md`, tried on Swift 1.5 IQ3_S with engine 0.1.55, both GPUs, 262k context, images on, `--resident-experts`, conversation cache off, memory saver off, Firefox closed. `bench-depth.ps1` with 1 warm and 3 measured runs per cell, thinking off; three rounds with the arm order rotated and a Strata restart before every run. Two benches: the first set four arms against each other, the second set `STRATA_PF_FUSED=1` against the first bench's winner. Median net t/s over all 9 measured replies per cell:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code, 2k / 52k / 128k) |
|---|---|---|---|
| Baseline (installed until 2026-11-02) | 127 / 126 / 123 | 90 / 92 / 87 | 660 / 1,280 / 1,640 |
| `--pool-affinity p-cores` | 109 / 105 / 102 | 83 / 84 / 80 | 660 / 1,280 / 1,650 |
| `--host-core last` | 127 / 124 / 128 | 89 / 91 / 87 | 670 / 1,280 / 1,640 |
| `--adapt-async 1` | 137 / 136 / 131 | 91 / 93 / 89 | 660 / 1,290 / 1,650 |
| `--adapt-async 1`, second bench | 137 / 126 / 131 | 92 / 91 / 88 | 640 / 1,280 / 1,630 |
| `--adapt-async 1` + `STRATA_PF_FUSED=1` (installed) | 131 / 128 / 131 | 90 / 90 / 88 | 780 / 1,390 / 1,800 |

- **`--adapt-async 1` adds 6-9% on code** and 1-2% on prose; prompt reads are unchanged. The start log says `adaptive tier asynchronous (--adapt-async 1): up to 96 swaps per round, a round every 4 windows`. It is not bit-exact from run to run, which does not matter at temperature 1.0.
- **`STRATA_PF_FUSED=1` reads prompts 9-22% faster** (code 2k 642 -> 781 t/s, 128k 1,633 -> 1,796; prose 11-20%) at the same decode speed, within the spread except 2k code (131 against 137, ranges overlap). It also changes the RAM copy: 23.15 GiB, and Windows page-locks all of it (`page-locked and mapped`, in 28-30 s), where every start without it logged `page-locking refused (out of memory)` for the 24.59 GiB copy. That is probably most of the prompt gain. It changes rounding; tool calls passed 3/3.
- **`--pool-affinity p-cores` is 10-17% slower here.** On the i5-12600K it leaves 5 pool workers on the P-cores, against 9 by default on P- and E-cores together. Upstream's gain (148 -> 188 tok/s) was on a 13700K with 8 E-cores.
- **`--host-core last` does nothing here.** The start log still puts the host thread on logical processor 0, and the numbers are within the spread.
- The run-to-run spread is wide: the same `--adapt-async 1` arm gave 136 and then 126 on 52k code. Each arm's own 9 replies spanned 10-20 t/s.
- No run had an error and no start log reported LOW VRAM. Free RAM while serving was 21-24 GB on every arm, and loads took 116-137 s.

The installed Swift config carries `--adapt-async 1` in `args` and `"env": {"STRATA_PF_FUSED": "1"}` since 2026-11-02. With both, the desktop card has 709 MiB free with everything loaded. On IQ3_XXS they do not help; on UD-Q4_K_XL `--adapt-async 1` helps most (both in the sections below).

Two more Swift extras went in on 2026-11-02, checked by a start (both reach the engine command line, no LOW, 560-568 MiB free on the desktop card, tool calls 3/3) but not benched, since a bench does not show either:

- `--prompt-cache-spread` in `args`: when the cache drops a checkpoint it drops the one that leaves the smallest gap, so an agent that rewrites its history a few turns back resumes near the edit instead of at the system prompt. Upstream (Pi on a layer split): 10.2 s against 40.0 s for an 82k conversation. Same RAM.
- `"expert_profile_save": "expert-profile-swift-learned.bin"`: the engine saves what its GPU cache learned to `F:\AI\strata\expert-profile-swift-learned.bin` (196 KB) on a clean exit and every 10 minutes between requests, and the next start pre-fills from it instead of the profile built on the original model. A clean exit is an unload (tray, idle unload, or `stop-strata.ps1`, which now tries an unload before it kills the server); a bare kill skips the save, and so does a stop while a reply is still running after 15 s (the unload answers 409, the script says so and kills as before). The first start from it chose a slightly different split (CUDA0 layers 0-20 against 0-21). Upstream warns a profile learned from one kind of text can slow other kinds; this one learns from whatever Swift serves. To go back to the original-model profile, stop Strata, remove the `expert_profile_save` key from the config (otherwise the next save writes the file again), and delete the file.

### IQ3_XXS with `--adapt-async 1` and `STRATA_PF_FUSED=1` (measured 2026-11-02)

The two Swift switches together against the installed IQ3_XXS config (engine 0.1.55, both GPUs, 262k, images on, `--resident-experts`, conversation cache off). Same method as above: three rounds, arm order rotated, a restart before every run, median net t/s over 9 measured replies per cell.

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code 2k / 52k / 128k; essay 128k) | Free RAM while serving |
|---|---|---|---|---|
| Installed | 143 / 139 / 131 | 107 / 103 / 100 | 1,100 / 3,410 / 3,500; 2,070 | 24.0-24.7 GB |
| Both switches | 141 / 143 / 133 | 106 / 101 / 98 | 1,150 / 3,130 / 3,200; 1,910 | 25.8-26.1 GB |

- **Decode does not change** (every cell within 3%, inside the spread). `--adapt-async 1`'s 6-9% on Swift code does not show on IQ3_XXS.
- **Long prompts read 7-9% slower** with both, 2k prompts 4-5% faster. IQ3_XXS's RAM copy was already fully page-locked without `STRATA_PF_FUSED`, so it does not get the pinning gain Swift got; the fused layout only shrinks the copy (15.61 against 17.05 GiB, about 1.5 GB more free RAM). Why long reads get slower is not known.
- No errors, no LOW (626 MiB free on the desktop card with both), loads 86-126 s.

Not installed: `strata-iq3_xxs.json` is unchanged.

### UD-Q4_K_XL with `--adapt-async 1` and `STRATA_MMQ_KQ=1` (measured 2026-11-02)

`STRATA_PF_FUSED` does not cover this file's K-quant experts on CUDA (`F:\AI\strata\docs\QUANTS.md`), so the prompt lever tried instead was `STRATA_MMQ_KQ=1` (llama.cpp's MMQ kernels for the Q4_K / Q5_K experts of a prompt, instead of dequantizing to FP16; the start log says `K-quant prompt kernels from ...strata_kq.dll`). Engine 0.1.55, both GPUs, 262k, 36 GiB RAM budget, conversation cache off. Three arms, one variable each, three rounds with the order rotated and a restart before every run; median net t/s over 9 measured replies per cell:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code 2k / 52k / 128k; essay 128k) |
|---|---|---|---|
| Installed until 2026-11-02 | 59 / 62 / 59 | 60 / 60 / 59 | 215 / 973 / 1,058; 656 |
| `--adapt-async 1` (installed) | 75 / 77 / 73 | 74 / 73 / 70 | 214 / 954 / 1,037; 645 |
| `STRATA_MMQ_KQ=1` | 58 / 60 / 60 | 60 / 61 / 59 | 220 / 989 / 1,097; 696 |

- **`--adapt-async 1` decodes 20-27% faster at every depth**, on code and prose alike, three times its gain on Swift. Probably because this file has the most experts outside the GPU caches (71.7 GiB against ~29 GiB cached), so the adaptive tier swaps the most, and asynchronous swaps no longer hold up decode (not checked with `--stats`). Prompt reads are unchanged.
- **`STRATA_MMQ_KQ=1` reads prompts 2-7% faster** at the same decode speed, close to the spread, and it rounds differently from the FP16 path. Not installed.
- No errors, no LOW (904-923 MiB free on the desktop card), loads 66-71 s. **Free RAM while serving had a median of 1.0-1.5 GB** (minimum 0.7 GB, on the `--adapt-async` arm), with the memory saver off and 49.5 GB free before the first start.

**Prompt chunk size.** Every UD-Q4_K_XL start logs `WARNING: a prompt chunk of 8192 tokens lends 1269 cache slots to the prompt path, but only 0 of their experts fit in RAM: the others are read from the SSD on every chunk ... a smaller --prefill keeps them all in RAM`. Upstream's planner that picks a chunk from the host's memory runs on Linux only. A second bench, with `--adapt-async 1` in every arm, compared `--prefill auto` (8192 here) with 4096 and 2048: three rounds, one measured reply per cell, one cold prompt read per cell per round (t/s, round 1 / 2 / 3):

| Cold prompt read | Code 52k | Code 128k | Essay 52k | Essay 128k |
|---|---|---|---|---|
| `--prefill auto` (8192) | 956 / 972 / 973 | 1,044 / 1,057 / 1,054 | 268 / 267 / 268 | 651 / 656 / 655 |
| `--prefill 4096` | 638 / 637 / 638 | 638 / 635 / 639 | 266 / 271 / 267 | 479 / 478 / 478 |
| `--prefill 2048` (installed) | 1,074 / 1,216 / 1,208 | 1,107 / 1,210 / 1,204 | 725 / 203 / 206 | 547 / 841 / 835 |

- **2048 reads code 14-24% faster at depth** and prose at 128k 28% faster (rounds 2-3). Prose at 52k was 23% slower in 2 of 3 rounds; why is not known. 2k prompts are a single chunk and read the same (216-226 t/s) in every arm.
- **4096 is 35-40% slower than both**, so the chunk size is not "smaller is better"; the warning stays at every size (501 slots at 2048, 757 at 4096).
- Decode is the same in all three (70-79 t/s net). The essay at 52k reads far slower than at 128k in every arm, which suggests the bench's essay prompt at that depth is unusual, not the setting.

`strata-unsloth-ud-q4_k_xl.json` has `--adapt-async 1` and `--prefill 2048` since 2026-11-02 (it had `--prefill auto`); tool calls 3/3 with each.

### Swift 1.5 with `STRATA_EXCHANGE_ROTATE=1` (measured 2026-11-02)

An adaptive swap hands the buffer over instead of copying the dropped expert into the RAM copy (`F:\AI\strata\docs\EXCHANGE_ROTATION.md`; upstream 164 -> 188 tok/s on an RTX 5090, never measured on a layer split). Both arms were the installed Swift config with `expert_profile_save` removed and the learned profile loaded read-only, so the bench could not change it. Three rounds, order rotated, restart before every run, median net t/s over 9 replies:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k | Cold prompt read, t/s (code 2k / 52k / 128k) | Free RAM while serving |
|---|---|---|---|---|
| Installed | 131 / 130 / 130 | 93 / 90 / 89 | 845 / 1,388 / 1,789 | 20.1-20.3 GB |
| `STRATA_EXCHANGE_ROTATE=1` | 134 / 131 / 124 | 91 / 89 / 89 | 852 / 1,382 / 1,791 | 19.0-19.4 GB |

- **No gain:** every cell is within -5% to +3%, inside the spread. The start log confirms it ran (`exchange buffer rotation enabled: 192 buffers in 7 block sizes ... 1.51 GiB page-locked`), and it costs ~1 GB of free RAM. Not installed.
- The installed arm, now starting from the learned profile, matches the 2026-11-02 bench that started from the original-model profile (131 / 128 / 131 code, 90 / 90 / 88 prose); not a controlled comparison.
- No errors, no LOW (578 MiB free on the desktop card with rotation), loads 121-131 s.

**Sampling.** Every config sets `"sampling": {"temperature": 1.0, "top_p": 0.95, "top_k": 20, "min_p": 0.0}`, the values the router's Qwen3.8 presets use. Without that block Strata decodes greedy (temperature 0) when a client sends no temperature, and greedy decoding is reported to loop on tool calls. A client that sends its own values, such as Pi, still wins. `F:\AI\strata\UPDATE.bat` updates the engine.

### Memory saver

With `--resident-experts` (2026-11-01) Swift and IQ3_XXS leave 21-28 GB of RAM free with the usual apps closed by hand, so the memory saver matters mostly for UD-Q4_K_XL (~4 GB free that way, ~3 GB with SonarLint running). With those apps open, expect less on every size. Before that, Strata left 2.5-4.1 GB free on IQ3_XXS even with the saver on (median, 2026-10-19, engine 0.1.45), so other apps' memory decided whether Windows started paging. **Memory saver** in the tray measures what is running and shows a checklist with each app's private memory and what you lose while it is closed. Tick what to close; the ticks are saved in `memory-saver.choices.json` (not tracked) and preselected next time. Click the menu item again to reopen exactly what it closed. While it is on, **Memory saver: close apps again...** opens the same checklist, showing what runs now, so you can close apps you reopened in the meantime and change the ticks; the next reopen still starts each app once. A restart turns the saver off: Windows' startup has launched those apps again, so the tray shows it off and nothing is reopened twice.

| Group | Rows | How it closes and reopens |
|---|---|---|
| Tested apps | WSL, Docker Desktop, Discord, OneDrive | WSL: `wsl --shutdown`, stopping Docker Desktop first if it runs and restarting it, with its running containers, on reopen. Docker: `docker desktop stop` / `start`, then restarts the containers that were running (`boxel-sonarqube` has no restart policy). Discord and OneDrive restart minimized. |
| Other apps | Anything in your session above 100 MB that is safe to close, such as Firefox or PowerToys, one row per install folder | Asked to close, then ended like Task Manager's End task after 10 s; reopened with the command line it was started with. Shells, interpreters, VS Code, Claude, this repo's servers, drivers' helpers, security software and Store apps are never offered. |
| VS Code extensions, services | Extensions' language servers above 100 MB; third-party services above 20 MB | Information only. Free an extension in VS Code; a window opened before you disabled it keeps it until **Developer: Reload Window**. Stopping a service needs admin. |

Measured 2026-10-10: WSL with Docker and SonarQube running freed 6.0 GB (27.4 → 33.4 GB free); Discord, OneDrive and Docker Desktop (VM included) freed 3.3 GB. NVIDIA App and NVIDIA Overlay are not offered: NVIDIA's services restart them within a minute. Turn the overlay off in NVIDIA App's settings instead.

From PowerShell: `.\memory-saver.ps1 -Mode Status` lists the candidates with the saved ticks, `-Mode On` closes the ticked apps (run again while on, it closes the ones reopened since), `-Mode Reapply` does only the second part and refuses when the saver is off, `-Mode Off` reopens them, and `-Mode List` prints the candidates as JSON.

## ninfer-3090 (measured 2026-10-18)

ninfer-3090 is a third engine: a Windows build of ninfer for RTX 3090s, installed in `F:\AI\ninfer-3090` with its own `.ninfer` weights (not GGUF) and one GPU per model. Two builds sit side by side there: Don-Chad's v0.16.1 (measured below) and ashalliants's v0.17.1 (added 2026-11-02, [below](#ashalliants-v0171-on-windows-measured-2026-11-02)); `start-ninfer.ps1` runs v0.17.1 (`$Build` in the script). No client is configured for it, and nothing starts it at login.

**Start it** from the tray (**Start ninfer: Qwen3.8-27B on GPU 1**) or with `F:\AI\llama-serve\start-ninfer.ps1`; stop it with **Stop ninfer** or `stop-ninfer.ps1`. Only one engine holds the GPUs at a time:

- Starting ninfer stops Strata and the router. Unloading the router's model is not enough: any request from Pi or VS Code would load a two-GPU preset onto GPU 1 beside ninfer, and both would slow down. While ninfer runs, the router's clients get no answer.
- Starting the router (`start-router.ps1`, or the tray's **Start router (stops ninfer)**) or Strata (`start-strata.ps1`) stops ninfer first.
- When the tray stopped the router for ninfer, it starts the router again once ninfer stops, whether from the menu or because ninfer exited by itself. A tray restarted while ninfer runs adopts it, but leaves the router stopped after **Stop ninfer**; use **Start router**.

The script sets `CUDA_DEVICE_ORDER=PCI_BUS_ID`, `CUDA_VISIBLE_DEVICES=1`, `NINFER_PORT=8412` (the default, 8080, belongs to signal-cli), `NINFER_GRAFTS=off` (stops the downloader from fetching a private graft through the Hugging Face login) and `NINFER_MODEL_DIR`, for its own process only, and logs to `logs\ninfer.log` (the previous run's in `ninfer.prev.log`). The server is ready in about 10 s (`/health` answers 200), speaks the OpenAI API at `http://127.0.0.1:8412/v1`, and returns llama.cpp-style `timings`, so `bench-depth.ps1 -BaseUrl http://127.0.0.1:8412` works unchanged.

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

**Cleanup and upstream status (checked 2026-11-01).** The Flash-Next conversion (29.8 GB), its `.ngram` companion (27.1 GB), an 8.4 GB partial download and `_download\ninfer.zip` (1.1 GB) were deleted. Kept: the engine folder and `models\qwen3_8_27b.ninfer` (17.7 GB). The newest release on Don-Chad/ninfer-3090 is still v0.16.1 (2026-10-16), but the repo had commits on 2026-10-31 (an optional Studio web dashboard) with no new Windows build.

**Where the code comes from (checked 2026-11-01 against GitHub and the installed binary).** Don-Chad's v0.16.1 (`BUILD_INFO.json`: source commit `8d509ae`) is ashalliants/ninfer-3090 at `931bd49b` (2026-10-15), adopted whole, plus MSVC/CUDA 13 fixes, and it was run on a 3090 before release. On GitHub ashalliants/ninfer-3090 is a fork *of* Don-Chad's repo (made 2026-08-29), but its code has since run far ahead, and Don-Chad's adoption commit `1418aca` (2026-10-16) copied it back whole. Today ashalliants is where the work happens (490 commits ahead of Don-Chad master on 2026-11-01). It has its own, later v0.16.1 (2026-10-22), then v0.16.2, v0.17.0 and v0.17.1 (2026-10-29), each with a Windows zip that its notes say was **built on a machine with no NVIDIA GPU and never run**. Those notes call v0.16.0 (2026-10-14) the last ashalliants Windows build run on a card; the installed one is a day newer in code and was run by Don-Chad. Known issues, by build:

- **Single lane by default.** The v0.16.1 notes say several simultaneous requests need a different multi-lane profile, so two clients at once (Pi plus VS Code, or subagents) queue or fail. The router has no such limit.
- **JSON `response_format` garbled** (Don-Chad #36, open; upstream #392): the grammar forbids whitespace. Present in Don-Chad's v0.16.1. Fixed in ashalliants v0.16.1 and later (its PR #335) and upstream (`9822c38`); we said so on Don-Chad #36 on 2026-11-01. Confirmed fixed in ashalliants v0.17.1 on 2026-11-02 (3/3 `json_object` replies parse).
- **`worker crash: target returned a token outside the public token domain`** (upstream #426, closed with no linked fix): one out-of-vocabulary token fails every request and the server exits. Present in both installed builds. We asked ashalliants to port Brontir/NInferno PR #217's fix ([ashalliants/ninfer-3090#410](https://github.com/ashalliants/ninfer-3090/issues/410), 2026-11-01); their PR #412 ("Out-of-vocabulary token ends its request, not the Engine") merged on 2026-11-02, after v0.17.1, so no release has it yet. The #426 report saw 7 exits in 27 hours of continuous agent traffic on 4 lanes; a single lane should hit it less often (an estimate).
- **Concurrent same-prefix requests crash the engine** (upstream #414, open): the regression range starts at upstream `48d8d185` (2026-10-14), which Don-Chad's v0.16.1 does not contain and ashalliants v0.16.1+ (so the installed v0.17.1) does. It needs `--max-concurrency` above 1; the `qwen38-27b` profile runs 1. #434 (stalls until a restart) is not confirmed on any 3090 build.
- **Reasoning effort** (Don-Chad #20, open PR): stale. Don-Chad's v0.16.1 binary accepts all seven values, `none` to `max`.
- Don-Chad #37 (closed, no explanation in the thread): the 27B looped in its thinking at 234k context. Both installed builds have a `--reasoning-loop` flag; untried.
- Models must be converted to ninfer's own `.ninfer` v3 container. It cannot load GGUFs, so a new Qwen3.8 fine-tune (fork #34 asks about Swift 1.5) waits on someone converting it. Our 17.7 GB file is the maintainers' pinned download.
- The release notes say distinct-GPU execution and Flash-Next were not part of their benchmark; they qualified one desktop-attached 3090 at a 16K context cap (116.7 tok/s median decode, 52-token prompts).

### ashalliants v0.17.1 on Windows (measured 2026-11-02)

ashalliants's v0.17.1 Windows zip, which its notes say was built on a machine with no NVIDIA GPU and never run, runs here. It is installed in `F:\AI\ninfer-3090\ninfer-rtx3090-windows-x64-0.17.1-rtx3090` (CUDA 12.8 libraries, against CUDA 13 in Don-Chad's build); the zip's SHA-256 and all 21 inner checksums matched. Launched with the environment `start-ninfer.ps1` now sets, it reaches `/health` 200 in 8-13 s on GPU 1, loads the existing `models\qwen3_8_27b.ninfer` without a download, and starts with the same profile as v0.16.1 (188k context, rk4v4, DFlash2 K=7, 22.1 GiB on the card).

- **Tool calls** 5/5 (`test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8412 -Model qwen3.8-27b -Runs 5`; with the router stopped, `-Model` is required). **JSON mode** works: 3/3 `json_object` replies parse.
- **Speed is the same as v0.16.1.** `bench-depth.ps1`, thinking off, 3 rounds per build interleaved (0.17.1 first), 5 measured runs per cell; net t/s, median of the three round medians:

| | Code 2k / 52k / 128k | Essay 2k / 52k / 128k |
|---|---|---|
| ashalliants v0.17.1 | 198 / 168 / 144 | 98 / 88 / 76 |
| Don-Chad v0.16.1 | 198 / 169 / 138 | 96 / 88 / 75 |

- **Spread:** the round ranges overlap in every cell (128k code: 136-150 against 138-146). Prompt reads are about 1% faster on v0.17.1 at 128k (1,703-1,724 tok/s against 1,684-1,702) with no overlap, too small to matter. Logs: `logs\bench-depth-20261102-1520` to `-1607`, `logs\ninfer-0.17.1-*.log`, `logs\ninfer-0.16.1-r*.log`.
- **`NINFER_<FLAG>` pass-through (#403) works**: `NINFER_PREFILL_STALL_BUDGET_MS=50` and `NINFER_CORS=on` reached `ninfer-serve`'s command line as `--prefill-stall-budget-ms "50" --cors`, and `NINFER_CORS=maybe` stopped the launcher with exit 2 and `NINFER_CORS must be on or off`.
- **`run.bat` prints `FINDSTR: Cannot open >nul` 84 times at every start** (stderr, harmless), and its guard against a double quote in a `NINFER_*` value never fires. Line 864, `set NINFER_%~1 2>nul | findstr /b /c:"NINFER_%~1=" | findstr /c:"\"" >nul`, has two faults: cmd does not read `\"` as an escape, so `>nul` lands inside quotes and becomes a file name for findstr; and in a pipe the left side runs in a child cmd where `2>nul` leaves a trailing space on the name, so `set` matches nothing. Tested in cmd: `2>nul set NINFER_%~1 | findstr /b /c:"NINFER_%~1=" | >nul findstr /c:"\""` catches `a"b`, passes `50`, and prints nothing. Still on ashalliants master `d0ff053`; reported with this run's results as [ashalliants/ninfer-3090#434](https://github.com/ashalliants/ninfer-3090/issues/434) (2026-11-02).
- Not tested: images, streaming, a client (Pi, Hermes, VS Code), long sessions, more than one lane, and the cache fixes the release is about (#401, #404, #405), which need a full Host cache.
- **Which to run:** v0.17.1. Same speed, JSON mode fixed, and the long-prompt stall fix. Neither build has the out-of-vocabulary fix (#412); move to the first release that does.

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
| A router model fails to load while Strata is running | The tray wasn't running to unload Strata. Use **Unload Strata model** in the tray, or `stop-strata.ps1`, and retry. Strata's own log: `logs\strata.log`; engine log: `F:\AI\strata\strata-<size>.log`. |
| Port 8410 is busy | Another router is already running (for example one started in a window). Stop it first. |

## Files

| File | Purpose |
|---|---|
| `presets.ini` | The model list and each model's settings. |
| `start-router.ps1` | Starts the router (after stopping ninfer): pinned llama.cpp build, port 8410, logs to `logs\server.log`. |
| `stop-router.ps1` | Stops the router and any loaded model. |
| `start-strata.ps1` / `stop-strata.ps1` | Start Strata on port 8411 (after stopping ninfer and unloading the router's model), logging to `logs\strata.log`, and stop it. |
| `start-ninfer.ps1` / `stop-ninfer.ps1` | Start ninfer-3090 (Qwen3.8-27B) on GPU 1 and port 8412 after stopping Strata and the router, logging to `logs\ninfer.log`, and stop it. |
| `memory-saver.ps1` | Lists, closes and reopens background apps for the tray's Memory saver. |
| `unload-router-model.ps1` | Unloads the router's model and waits until its VRAM is free. Strata runs it before every load. |
| `tray\LlamaTray.cs` | The tray app. It starts and stops the router, Strata and ninfer through the scripts above, and unloads Strata when a router model loads. |
| `install-tray.ps1` | Builds the tray app into `tray\bin\` (with the C# compiler built into Windows, no SDK), adds the Start menu shortcut, points the logon task at it, and launches it. Re-run after editing `LlamaTray.cs`. |
| `bench-depth.ps1` | Speed bench at ~2k / 52k / 128k context, for choosing between drafter lanes. Results in `logs\bench-depth-*.txt`. |
| `bench-thinking.ps1` | Thinking-token and accuracy A/B of logit-bias arms (`bench\logit-bias-arms.json`) on math and code tasks (`bench\tasks-*.jsonl`). Results in `logs\bench-thinking-*.txt`. |
| `test-tool-calls.ps1` | Checks that every model's tool calls parse and round-trip. |
| `link-hub-models.ps1` | Hardlinks Unsloth Studio downloads into the library. |
| `docs/ubuntu-setup.md`, `plans/ubuntu-dual-boot.md` | Guide and plan for an Ubuntu 26.04 dual boot on the spare NVMe, to measure Strata on Linux. |

Why port 8410: 8080 is taken by signal-cli in WSL, and the club-3090 WSL stack uses 8020-8213. Pi finds the router through the user environment variable `LLAMA_BASE_URL=http://127.0.0.1:8410/v1`.

The llama.cpp build lives in `F:\AI\llama.cpp\b10951\`. To upgrade, unzip a new build next to it and change `$Build` in `start-router.ps1`, so rolling back is a one-line change.
