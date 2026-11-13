# Handoff: Strata to v0.2.13, then Swift 1.5 IQ3_S against Unsloth 3.1 UD-IQ3_XXS · Opus 5.5 / high

**Status:** open. Written 2026-11-12. Move to `dev-notes/archive/` when done.
**Progress (2026-11-13):** steps 1-5 done on branch `feat/strata-0.2.13-swift-vs-unsloth31` (engine 0.1.59,
results in README § "Swift 1.5 IQ3_S against Unsloth 3.1 UD-IQ3_XXS"; Swift won on time to completion and
accuracy). The shard hashes differ, so no hardlink. Waiting on step 6: Paul's approval to delete the loser.
**Repo:** `F:\AI\llama-serve` @ `main`. Use a branch for code changes (tray, scripts). `F:\AI\strata` is upstream's
clone: never commit there or edit its source ([AGENTS.md](../../AGENTS.md#what-this-repo-is)).

## Context

**Strata versions.**

- Installed: code v0.2.11-4-g54828c7e, engine **0.1.57**. `F:\AI\strata\engine\BUILD.json` is dated 2026-11-05,
  and `engine\.previous` holds 0.1.55.
- AGENTS.md and the README still say 0.1.55, so update them.
- Latest: **v0.2.13 / engine 0.1.59** (2026-11-09; 0.2.13.1 changes only Intel Arc).
- Since 0.2.10, upstream uses DFlash2 as the default drafter (tuned in 0.2.12). All our configs still use
  `--mtp`.

**The Unsloth 3.1 file, installed but never run.**

- Unsloth's Dynamic 3.1 **UD-IQ3_XXS** for Flash-Next was installed on 2026-11-05: 83.6 GB, 4 files in
  `F:\AI\Strata-data\models\unsloth-UD-IQ3_XXS`, pack `packs\unsloth-ud-iq3_xxs`.
- Its config, `F:\AI\strata\strata-unsloth-ud-iq3_xxs.json`, is already tuned by hand: reserves 1344/1310,
  `--resident-experts`, `--conversation-cache-mib 0`, `--adapt-async 1`, env `STRATA_HC_Q8=1`,
  `idle_unload_s`, `sampling`, `before_load`.
- It has never started (there is no `strata-unsloth-ud-iq3_xxs.log`).
- It is in none of these: the tray's `StrataSizes` ([tray/LlamaTray.cs](../../tray/LlamaTray.cs)), Pi's
  `models.json`, Hermes's `config.yaml`, VS Code's `chatLanguageModels.json`, the docs.

**Our UD-Q4_K_XL is not 3.1 and needs nothing.** Unsloth's 11-04 commit only re-split it: the shard totals match to
within 6 bytes, and Strata's 0.2.11 notes say the same. The 3.1 4-bit file is UD-IQ4_XS ("UD-IQ4_XS-v31" in
Strata, 93.7 GB). It is not part of this work.

**Paul's decisions (2026-11-12):**

- Compare Swift 1.5 against Unsloth 3.1 **matched by size**: Swift GSQ-RCO IQ3_S (84 GB, config
  `strata-swift-iq3_s.json`, the default) against UD-IQ3_XXS 3.1 (83.6 GB).
- Effort **`xhigh`**.
- **Time to task completion is a primary metric**: Swift 1.5's selling point is less thinking for the same answers.
- Results go in a table.
- **The loser is deleted only after Paul has reviewed the results and approved the deletion.**

## Steps

1. **Baseline on the current engine.**
   - Run `bench-depth.ps1` on Swift ([AGENTS.md § To update](../../AGENTS.md#strata-state-and-updating-it)).
   - Back up every `strata-*.json` and `run-*.bat`.
2. **Update.**
   - Run `cd F:\AI\strata; cmd /c ".\UPDATE.bat < nul"`.
   - Diff every config against the backup. Re-add any hand-added key or flag that dropped (the list is in
     AGENTS.md).
   - In each start log, read the `VRAM free with everything loaded` lines. A "LOW" means the desktop card needs a
     larger reserve.
   - Bench Swift again against the baseline.
3. **First start of UD-IQ3_XXS.** Run `.\start-strata.ps1 -Model unsloth-ud-iq3_xxs`, then check:
   - the start log: no LOW, `page-locking` status, expert-arena size
   - free RAM
   - `test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411`
4. **Compare.**
   - Use `bench-thinking.ps1 -BaseUrl http://127.0.0.1:8411 -Arms off -Effort xhigh` on all 50 tasks.
   - Run 3 rounds per arm, interleaved, with a Strata restart and a settle before each.
   - Also run `bench-depth.ps1` for t/s.
5. **Results table** in the README (dated, rig stated). One row per arm with these columns:
   - tasks solved (math, code)
   - median and total **wall seconds per task**
   - median reasoning tokens
   - net t/s at 2k / 52k / 128k
   - prompt t/s
   - load time
   - free RAM while serving
   - tool calls

   Also give the per-task wall-time difference on tasks both arms solved.
6. **Stop for Paul.** Show him the table and assign him a task to approve deleting the loser. Delete nothing before
   he answers.
7. **After his approval:**
   - Delete the losing model folder and its pack, config and `run-*.bat`.
   - If UD-IQ3_XXS wins: add it to `StrataSizes`, run `install-tray.ps1` (this restarts his tray, so say so),
     add it to the client configs, and decide the default with Paul.
   - Ship the code part through a PR.

## Traps

- **Effort reaches Strata as `xhigh`.** Qwen3.8's template knows only `low`, `medium` and `xhigh`, so Strata maps
  `high`, `xhigh` and `max` all to `xhigh` (`F:\AI\strata\serve\frontend.py` near line 132; `docs\AI_SETUP.md` § 6:
  "`high` maps to `xhigh`"). `xhigh` is also the default when a request names no level. bench-thinking's
  `chat_template_kwargs.reasoning_effort` is read (`frontend.py` near line 612). To confirm in the start log or a
  rendered prompt, look for the sentence "Reasoning effort is set to xhigh."
- **The bench-thinking summary may not total wall time.** Its `.txt` summary may not include `wall_s`. Add it if it
  is missing (a code change: branch, then a PR).
- **Disk:** both of UD-IQ3_XXS's shard 4 and ISTA IQ3_XXS's shard 2 are 28,800,138,432 bytes. If their SHA-256
  hashes match, hardlink them to save 28.8 GB. F: had 183 GB free on 2026-11-12, before the 27B download.
- **Do not start Strata unprompted**: it takes the GPUs. A failed `before_load` hook still loads the model beside a
  router model, so unload the router first.
- Keep DFlash2-against-MTP out of this comparison (one variable at a time). It is a follow-on item for Swift.
- The engine's expert profile (`F:\AI\strata\data\expert-profile.bin`) was built on the original model. Swift
  pre-fills from its own learned profile, and UD-IQ3_XXS uses the original one. Note this in the results.
