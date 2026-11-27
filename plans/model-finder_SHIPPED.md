# Shipped slices of [model-finder.md](./model-finder.md)

### ~~S1 — The catalog, the page and the tray item, on this PC~~ — SHIPPED 2026-11-27 {#s1}

**Status:** shipped 2026-11-27.
**Touches:** `model-sources.ps1` (new), `sync-opencode.ps1`, `model-catalog.ps1` (new), `model-catalog.json` (new),
`web/model-finder.html`, `web/model-finder.js`, `web/model-finder.test.mjs` (new), `tray/ModelFinder.cs` (new),
`tray/LlamaTray.cs`, `tray/tests/ModelFinderTests.cs` (new), `test-model-finder.ps1` (new), README.md, AGENTS.md;
port 8407.
**After:** none.
**Seam:** `model-catalog.ps1 -Out <file>` (the JSON), `GET /`, `GET /model-finder.js`, `GET /catalog.json` and
`GET /status.json` on `127.0.0.1:8407`, and the pure `filterModels` / `sortModels` exports of `web/model-finder.js`.

How it is built:

- **Sources.** `model-sources.ps1` (dot-sourced) exports `Get-RouterPresets`, `Get-StrataConfigs` and
  `Get-NinferProfiles`, lifted from `sync-opencode.ps1`, which then uses them. Strata sizes are the
  `strata-<size>.json` files that have a `run-<size>.bat` beside them (what `start-strata.ps1 -Model <size>`
  accepts). ninfer profiles are the `$Inkling` rows plus `qwen38-27b` (context 188416, the constant
  `sync-opencode.ps1` already uses, moved here).
- **Entry fields**, by rule: `id` (preset id, Strata size, ninfer profile), `engine`; `context` (`ctx-size`,
  `--max-context`, `Context`); `vision` (`mmproj` present; Strata `--vision` not `off`; ninfer Inkling yes, Qwen
  no); `gpus` = `gpu1` when every `device` / `spec-draft-device` line says `CUDA1`, Strata's `"gpu": 1`, ninfer's
  empty `Gpus` or Qwen, else `both`; `drafter` from `spec-type` (`draft-mtp` → `mtp`, `draft-dflash` → `dflash`);
  `thinking` for router presets: `reasoning-budget = -1` → `always`, `reasoning = on` → `on`, `reasoning = off` →
  `off`, neither key → `none`; for Strata and ninfer from `model-catalog.json`.
- **`model-catalog.json`**, keyed by entry id: `family`, `description`, `speed` (`{"code": t/s, "prose": t/s}` at
  2k, from README.md § The models and the Strata / ninfer sections; absent where never measured), `codeOnly`,
  and `thinking` for Strata / ninfer. A `families` table (empty until S3). An entry id the builder finds but the
  JSON lacks still appears, with `family` = its id; the test fails and names it, so a new preset gets its row.
- **Serving.** `ModelFinder` (an `HttpListener` on `127.0.0.1:8407` and `localhost:8407`, started in
  `StartUp()`) serves the two `web\` files, `logs\model-catalog.json` and `status.json`; anything else 404. Before
  serving the catalog it runs `pwsh -NoProfile -File model-catalog.ps1 -Out logs\model-catalog.json` (30 s
  timeout) when that file is missing or older than `presets.ini`, `model-catalog.json`, `start-ninfer.ps1`, the
  Strata folder or any `strata-*.json` or `run-*.bat` in it; on failure it serves the old file, or a 500 with the
  script's error when there is none. `status.json` is the front door's `EngineStatus()`: `router` / `strata` /
  `ninfer` with `state` and `model` (Strata also `size`, the size the tray started), plus `reserve_gpu0`, read on
  the UI thread. The page polls it every 5 s.
- **The page.** Filters: context (a log-scale slider 8K-1M with a number field, kept in step; an entry shows when
  its context ≥ the value), images required, GPUs (any / GPU 1 only, preset to GPU 1 only while Reserve GPU 0 is
  on), engine (router / Strata / ninfer), thinking (any / can think / no thinking), hide code-only drafter lanes,
  minimum code speed. Sort: code speed (default until S3), context, name; missing values last. The loaded model is
  marked. It is product UI, not a scratch page, so its look follows the `impeccable` skill.
- **Tests.** `test-model-finder.ps1` runs `node --test web/` (node 24 is installed), the catalog builder on
  fixtures in `tray\tests\fixtures\`, and builds `ModelFinder.cs` with `tray\tests\ModelFinderTests.cs` the way
  `test-switchboard.ps1` builds its tests (a fake host, a free port).

Boxes:

- [x] Red tests first (filter at the context boundary, images, GPU 1 only; the builder's rules on fixtures; the
      listener's routes, a 404 and a stale catalog rebuilt); commit and show the red run.
- [x] `sync-opencode.ps1 -WhatIf` prints no change after the move to `model-sources.ps1`.
- [x] The builder on the real files: `gemma4-31b` 65536 with images, `qwen3.8-27b-single` gpu1 text, every Strata
      size's context equals its `--max-context`, and no entry lacks a `model-catalog.json` row.
- [x] Tray item "Model finder" opens `http://127.0.0.1:8407/`; `install-tray.ps1` builds; `test-switchboard.ps1`
      still passes. Playwright: at 32K every entry shows; at 262K `gemma4-31b`, `gemma4-31b-mtp` and
      `qwen3.8-27b-single` drop; screenshot checked.
- [x] README.md § Using the model finder; AGENTS.md "Other moving parts" bullet.

Strike when every box is checked. Cancel if a second listener breaks the front door (`test-switchboard.ps1`).

Rulings and findings:

- **Ruling:** `status.json` is the front door's engine status (`router` / `strata` / `ninfer` with `state` and
  `model`, plus `reserve_gpu0`), from one `EngineStatus()` both use, not a second `loaded` shape; the page derives the
  loaded ids (`loadedIds`). Why: one source for engine state. If wrong: the page's `loadedIds` changes.
- **Ruling:** a typed context is read as decimal (`262k` = 262,000) and shown in binary (`256K`). Why: the docs write
  262144 both as "262k" and "256K", and a decimal threshold never drops a model named either way. If wrong: a model
  with 262,000-262,143 tokens would show for "262k".
- **Ruling:** the listener loop (`RequestLoop.cs`) and the hidden pwsh start (`HiddenScript` in `LlamaTray.cs`) were
  extracted and are shared with the front door and the memory saver. S2's bind helper goes beside `RequestLoop.Prefixes`.
- **Ruling:** the Strata list is every `strata-<size>.json` with a `run-<size>.bat`, so it includes
  `inkling-2-mini-sq-20-gpu1`, which the tray menu does not list. `sync-opencode.ps1` output was byte-identical after
  the move (compared on a copy of the config).
- The page also lists the models that do not fit, with what each misses (`whyHidden`), so a filter's effect is visible.
- Not driven: the tray menu click itself (the item is `OpenPath` of the URL, like "Open chat in browser"); the page
  was checked over HTTP and Playwright against the running tray after `install-tray.ps1`.

### ~~S3 — Benchmark scores, sortable, each explained~~ — SHIPPED 2026-11-27 {#s3}

**Status:** shipped 2026-11-27.
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

- [x] Red test: sorting by a benchmark puts missing scores last and keeps ties in name order.
- [x] Every score has a source URL naming that exact model, checked by reading each page. Each publisher's card was
      downloaded and its tables read twice: by a Sonnet 5.5 batch on the API credit (13 cards, about $0.67) and by a
      table parser run here; a script then matched all 57 catalog values to their card cells.
- [x] Playwright: picking each benchmark re-sorts the list and shows its explanation (the repo's `web/` files and a
      catalog built from the real launch files, served by a throwaway static server; the tray was not restarted).

- **Ruling:** scores are only the publisher's numbers for its own model. A score a publisher measured for someone
  else's model (Qwen's card scores Muse Glimmer, Ornith's scores Gemma) is not used, so no family mixes harnesses
  within its own row.
- **Ruling:** left out of the catalog: benchmarks that need images (ERQA, OmniDocBench, CharXiv), C-Eval (a
  Chinese-language exam), and versions that differ from the column's (Qwen3.6's Terminal-Bench 2.0, Ornith's unversioned
  DeepSWE, Inkling's SWE-bench Pro v2 Hard and HLE-Diamond). Eleven columns remain; the table scrolls
  sideways inside its own box, which keeps the header row in view.
- **Ruling:** `source` and `measuredOn` sit on the family row, not on each score: one page per family holds all its
  scores. A score carries only `value` and the `setting` its page names.
- **Ruling:** the default sort applies until a sort is picked by hand (`sortChosen` in the saved state), so a browser
  that had saved the old default (speed) also opens on GPQA Diamond.
- **Ruling:** scores show one decimal; the tooltip has the full number (JSON drops a trailing zero: 89.60 reads 89.6).
- **Ruling:** Qwen3.6-35B-A3B's row is labelled "GPQA"; it is filed under GPQA Diamond because the Gemma 4 columns
  of the same table equal Google's GPQA Diamond figures.
- **Ruling:** DeepSWE 1.1 and JobBench are described from their papers (arXiv 2607.07946, 2605.26329), since no
  model card says what they test.

