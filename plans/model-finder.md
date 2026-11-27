# Plan: the model finder, a web page that filters the models down to the ones that fit

> **Why:** Paul wants to pick a model by what he needs (this much context, images or not, one GPU or two) and see
> the strongest one first, from this PC and from the Linux laptop. Today that answer is spread over README.md
> § The models, the Strata and ninfer sections and three config formats. The tray serves one page that reads the
> same files the engines start from, so the list never drifts from what actually runs.
>
> **Done means:** the tray menu opens the page; from the laptop it answers at `http://<PC>:8407/` with the router
> shared on the network; the context slider and number field, the images toggle and the other filters drop the
> entries that do not fit; any benchmark column sorts the list, each with a plain-words line on what a higher score
> means; and every vision model that reaches more context without its picture reader has a measured text-only
> preset built from files already on disk.
>
> **Review focus:** a context or images flag that disagrees with what its engine starts with (S1: the real-files
> catalog box) · the page reachable from outside the home network and Tailscale (S2: the firewall box) · a
> benchmark score with no source or from another model size (S3: the source box) · a text-only preset that loads
> but spills under WDDM (S4: the `nvidia-smi` box) · a new download to make room (S4: the no-download box).
>
> **Next:** S2, the page reachable from the laptop
>
> **Branch:** per slice, `feat/model-finder-<slice id>` off `main`, one PR each
>
> **Lanes:** A: S1→S2→S3 · B: S4 (after S1; bench sessions Paul starts)
>
> **Flow:** lean

- [~~S1 — The catalog, the page and the tray item, on this PC~~ — SHIPPED 2026-11-27](#s1)
- [S2 — Reachable from the laptop](#s2)
- [S3 — Benchmark scores, sortable, each explained](#s3)
- [S4 — Text-only presets where dropping the picture reader buys context](#s4)

## Traps

- **Facts come from the launch files, not from a second list.** Context, images, GPUs and the engine are read from
  `presets.ini`, the Strata configs and `start-ninfer.ps1`, through `model-sources.ps1` (the reads
  `sync-opencode.ps1` already does). Only what no launch file holds (descriptions, measured speeds, code-only
  drafters, thinking for Strata and ninfer, benchmark scores and their sources) lives in `model-catalog.json`.
- **Lanes of one model share its scores.** A `-dflash`, `-mtp` or `-nopreserve` preset gives the same model's
  answers; only speed (or the prompt) changes. Benchmarks attach to the family, not the preset.
- **Most vision models are already at their native 262k.** A text-only twin gains context only where VRAM, not the
  model, sets the limit: `gemma4-31b` (65k) and ninfer's one-GPU Inkling (256K). `qwen3.8-27b-single`'s 262k is
  plans/context-and-quality-benches.md A1; this plan does not redo it.
- **The tray compiles with the Windows C# 5 compiler**: no string interpolation, no `?.`, no expression-bodied
  members (install-tray.ps1).
- **Do not load models, start Strata or stop the router to test S1-S3.** The page only reads files and the tray's
  own state. S4's loads happen in a bench session Paul starts, with `nvidia-smi` idle first.

### ~~S1 — The catalog, the page and the tray item, on this PC~~ · Opus 5.5 / medium — SHIPPED 2026-11-27 {#s1}

Shipped: the tray serves the page on `127.0.0.1:8407` from `model-catalog.ps1` + `model-catalog.json`; `status.json` is the front door's engine status. Body and rulings: [model-finder_SHIPPED.md](./model-finder_SHIPPED.md#s1).

### S2 — Reachable from the laptop · Sonnet 5.5 / medium {#s2}

**Status:** not started.
- **Note (from S1):** `RequestLoop.Prefixes(port, shared)` in `tray/RequestLoop.cs` already gives both prefix sets;
  `ModelFinder.Prefixes()` calls it with `false`. The finder is started by `StartModelFinder()` in
  `tray/TrayModelFinder.cs`; put `BindListener` beside it in `RequestLoop.cs` and test it from `ModelFinderTests.cs`.
**Touches:** `tray/RequestLoop.cs`, `tray/ModelFinder.cs`, `tray/TrayModelFinder.cs`, `tray/TraySwitchboard.cs`,
`tray/LlamaTray.cs` (the Share toggle), `allow-switchboard.ps1` → `allow-tray-ports.ps1`, README.md, AGENTS.md.
**After:** S1.
**Seam:** the listener's prefixes for `shareOnNetwork` true and false, and the allow script.

The bind-with-fallback in `ApplySwitchboard` becomes `static string BindListener(Action<string[]> start,
Func<bool, string[]> prefixes, bool shared, string what, Action<string, ToolTipIcon> notify)` returning
`"network"`, `"local"` or null, used by both listeners; the Share toggle re-applies both. With Share on network the
page binds `http://+:8407/`, falling back to loopback with a notice like the front door. It stays read-only and
needs no token: it shows nothing a router client could not list. `allow-tray-ports.ps1` reserves 8407 and 8409 and
adds one firewall rule per port with the front door's scope (192.168.1.0/24, 100.64.0.0/10).

- [ ] Red test: the prefixes for both settings.
- [ ] Every reference to `allow-switchboard.ps1` now names the new script (`git grep` empty).
- [ ] Paul runs the script once as admin, then opens `http://<PC>:8407/` from the laptop (his laptop and an admin
      prompt; nothing here reaches either).

### S3 — Benchmark scores, sortable, each explained · Opus 5.5 / high {#s3}

**Status:** not started.
- **Note (from S1):** entries carry `family` already (`model-catalog.json`); scores go in its `families` table and
  `model-catalog.ps1` passes `families` and `benchmarks` through unchanged. Add the sort key to `SORT_VALUES` in
  `web/model-finder.js`. Paul asked on 2026-11-27 that research run on the API credit (the `offload` skill's Batch
  route) while it lasts; it expires in early December 2026.
**Touches:** `model-catalog.json` (`families`, `benchmarks`), `web/model-finder.js`, `web/model-finder.html`,
`web/model-finder.test.mjs`.
**After:** S1.
**Seam:** `sortModels` with a benchmark key, and the catalog's `benchmarks` table.

Scores are the publisher's (model card or paper), per family, each with its source URL and the model size it was
measured on; a fine-tune with no published score shows a dash and sorts last. A benchmark gets a column when at
least three families report it. Each benchmark has a line on what it tests and what a higher number means. The
default sort is the benchmark the most families report. Publishers use different harnesses, so the page says once
that scores within a few points are a tie.

- [ ] Red test: sorting by a benchmark puts missing scores last and keeps ties in name order.
- [ ] Every score has a source URL naming that exact model, checked by reading each page.
- [ ] Playwright: picking each benchmark re-sorts the list and shows its explanation.

### S4 — Text-only presets where dropping the picture reader buys context · Opus 5.5 / medium {#s4}

**Status:** not started; needs a bench session Paul starts.
**Touches:** `presets.ini`, `start-ninfer.ps1`, `model-catalog.json`, Pi's `models.json`, VS Code's
`chatLanguageModels.json`, `sync-opencode.ps1` run, README.md; both GPUs.
**After:** S1.
**Seam:** the router's `/models` and a needle prompt at the new context.

Candidates: `gemma4-31b` / `gemma4-31b-mtp` (65k with images) and ninfer's `inkling2-mini` on GPU 1 (256K). For
the Gemma first try keeping images with the picture reader on the CPU (`no-mmproj-offload`, if `llama-server
--help` on b10951 lists it): one preset, both uses. A twin is added only if that arm misses the context or takes
over 3 s per picture. Inkling's text-only arm drops `--vision`. Only files already on disk; nothing is downloaded.

- [ ] Each arm: peak `nvidia-smi` under 23.5 GiB on both cards, no `retrying without pipeline`, a needle found
      near the new context, tool calls pass.
- [ ] No download: every model path in the diff already exists on disk.
- [ ] Each adopted preset or twin is in Pi, VS Code, opencode and the page.
