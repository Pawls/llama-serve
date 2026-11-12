# Handoff: Qwen3.8-27B to Unsloth Dynamic 3.1, then Swift 1.5 against it · Opus 5.5 / high

**Status:** open. Written 2026-11-12. Move to `dev-notes/archive/` when done.
**Repo:** `F:\AI\llama-serve` @ `main`. Use a branch for any script change. Docs-only results go straight to
`main` ([AGENTS.md](../../AGENTS.md#git)).

## Context

Unsloth re-uploaded its Qwen3.8-27B GGUFs as "Dynamic V3.1" on 2026-11-11, with the
[same filenames](https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/tree/main) as before (repo commit `acb953b`;
UD-Q3_K_XL in the later `4af51b3`). Unsloth claims +5-10% accuracy at the same size
([Qwen3.8 page](https://unsloth.ai/docs/models/qwen3.8)). Its KL-divergence charts are images and no method is
published. Our three files in `F:\AI\models\llm\unsloth\Qwen3.8-27B-GGUF\` predate 3.1:

| File | Local bytes | Local date | 3.1 size (HF) |
|---|---|---|---|
| UD-Q5_K_XL | 20,876,938,144 | 2026-08-23 | 21.0 GB |
| UD-Q4_K_XL | 17,559,178,144 | 2026-08-23 | 17.7 GB |
| UD-Q3_K_XL | 13,146,393,504 | 2026-09-03 | 13.1 GB |

Each has link count 2: it is hardlinked from `F:\AI\models\llm\hub` by
[link-hub-models.ps1](../../link-hub-models.ps1).

**Paul's decisions (2026-11-12):**

- Replace all three with 3.1, with no old-against-new A/B.
- Compare Swift 1.5 against Unsloth 3.1 **matched by size**, at **`xhigh`** effort (Pi's default).
- **Time to task completion is a primary metric**: Swift 1.5's selling point is less thinking for the same answers.
- Results go in a table.

## Steps

1. **Swap the files.**
   - Download the three 3.1 files to replace the old ones, and delete the old ones. Hardlinks: also replace or
     remove the old copies in `hub`, or the disk space is not freed.
   - Record the HF commit of each file in the README.
   - Leave `mmproj-F16.gguf` alone (not re-uploaded).
2. **Check every preset that uses them.**
   - Presets: `qwen3.8-27b`, `qwen3.8-27b-dflash2` and `qwen3.8-27b-nopreserve` (Q5_K_XL);
     `qwen3.8-27b-single` (Q4_K_XL).
   - For each: the `retrying without pipeline` check, the `nvidia-smi` spill check, and
     `test-tool-calls.ps1 -Model <preset>` ([AGENTS.md § Verifying changes](../../AGENTS.md#verifying-changes)).
3. **Compare, matched by size.**
   - Arms: `swift-qwen3.8-27b-dflash2` (ukisai Swift-1.5 Q5_K_M, 20.92 GB) against `qwen3.8-27b-dflash2`
     (UD-Q5_K_XL 3.1, 21.0 GB). Same drafter, so the only difference is the weights.
   - Use [bench-thinking.ps1](../../bench-thinking.ps1) with `-Arms off -Effort xhigh` on all 50 tasks
     (30 math, 20 code). Run 3 rounds per arm, interleaved, alternating which arm goes first. Each arm is a
     separate run, because the script loads one model.
   - Also run `bench-depth.ps1 -Model swift-qwen3.8-27b-dflash2,qwen3.8-27b-dflash2` for raw t/s.
4. **Results table** in the README (dated, rig stated). One row per arm with these columns:
   - tasks solved (math, code)
   - median and total **wall seconds per task** (time to completion)
   - median reasoning tokens
   - net t/s at 2k / 52k / 128k
   - tool calls

   Also give the per-task wall-time difference on tasks both arms solved.
5. Optional, after the comparison: Q4_K_XL 3.1 on `qwen3.8-27b-single` against its documented 21.7 GiB peak
   ([AGENTS.md § Reserve GPU 0](../../AGENTS.md#how-the-pieces-fit)), and whether Q3_K_XL holds 262k on GPU 1
   alone. Both are untried.

## Traps

- **The bench-thinking summary may not total wall time.** It records `wall_s` per row (the
  `bench-thinking.ps1` line that sets `$row.wall_s`). Check its `.txt` summary; if wall time is missing, add it to
  the summary. That makes it a code change: branch, then a PR.
- **Swift's thinking budget:** keep `-MaxTokens` the same for both arms. A task that runs out of tokens counts as a
  failure, and its wall time still counts.
- **The new files are slightly larger** (Q4_K_XL +0.14 GB). The `-single` lane peaked at 21.7 GiB at 131k on the
  old file, so recheck it.
- **MTP head:** the presets use the drafter built into the GGUF (`spec-type = draft-mtp`, no draft file). Check that
  the 3.1 files still carry it in the load log (Unsloth removed MTP only below UD-Q2_K_XL).
- Run `nvidia-smi` first and confirm the GPUs are idle. Do not start Strata or ninfer while benching.
- Not updated by Unsloth, so do not fetch: UD-IQ3_S, UD-IQ3_XXS, UD-Q4_K_S, UD-Q6_K_XL, Q8_0, BF16, mmproj.
