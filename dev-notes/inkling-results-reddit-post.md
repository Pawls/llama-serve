**Title:** Inkling 2 Mini on native Windows, 2x RTX 3090 without NVLink: 256K on one card, 768K on two (ninfer-all prebuilt, numbers and tradeoffs)

These are Windows numbers from a prebuilt engine: no WSL, Docker or compiling. If you run Linux, the vLLM recipes linked at the bottom are faster than this on every axis except maximum context. Read that section before you copy my setup.

## What you need

| | One-card setup (256K) | Two-card setup (768K) |
|---|---|---|
| GPU | One 24 GB RTX 3090 or newer, headless or close to it: the server uses 23.0 GiB of the card | Two 24 GB cards; the cache card should be headless |
| System RAM | About 10 GB for the server process: ninfer-all keeps an 8 GiB prompt cache in pinned host memory by default. That is too small for long conversations (see the cache section below); I run 20 GiB, which should take about 22 GB of RAM when full (an estimate: the 8 GiB default plus 2 GB was the earlier figure) | Same |
| Disk | 15.4 GiB for the model, about 1 GB for the engine | Same, 22.1 GiB more for the Q8 file |
| OS | Windows 10/11 x64, the MSVC x64 runtime, an NVIDIA driver that supports CUDA 12.9 | Same |

My machine:

| Part | Details |
|---|---|
| GPU 0 | RTX 3090 (MSI Suprim X), PCIe 4.0 x16, runs the Windows desktop. Undervolted: core flat at 1680 MHz from 850 mV, power limit 100% (420 W) |
| GPU 1 | RTX 3090 Founders Edition, PCIe 4.0 x4, headless. Undervolted: core flat at 1650 MHz from 850 mV, power limit 100% (350 W) |
| Link | No NVLink, and no peer access between the cards |
| CPU and RAM | i5-12600K, 64 GB DDR4-3200 |
| OS and driver | Windows 11, WDDM, driver 621.14 |

## Software

- Engine: [ninfer-all](https://github.com/iamwavecut/ninfer-all) release `2026-11-23.9`, Windows CUDA 12.9 package.
- Model: [WaveCut/Inkling-2-Mini-UD-IQ4_XS-NInfer-v3](https://huggingface.co/WaveCut/Inkling-2-Mini-UD-IQ4_XS-NInfer-v3). It is Unsloth's UD-IQ4_XS GGUF converted to ninfer's format, with the vision tower and the DSpark draft head included.

This release stores the KV cache in MXFP8 (16.5 KiB per token, against 32 KiB for F16), the format the model was trained with. ninfer-all's docs measure +0.024% mean NLL against F16. On the previous release (F16 cache), the same card topped out at 128K.

## Commands

One card, 256K. I use the headless card:

```
set CUDA_DEVICE_ORDER=PCI_BUS_ID
set CUDA_VISIBLE_DEVICES=1
ninfer-serve.exe Inkling-2-Mini-UD-IQ4_XS-ninfer-v3.ninfer --model-id inkling-2-mini ^
  --host 127.0.0.1 --port 8412 --max-context 262144 --vision --structured-output
```

Two cards, 768K:

```
set CUDA_DEVICE_ORDER=PCI_BUS_ID
set CUDA_VISIBLE_DEVICES=0,1
ninfer-serve.exe Inkling-2-Mini-UD-IQ4_XS-ninfer-v3.ninfer --model-id inkling-2-mini-2gpu ^
  --host 127.0.0.1 --port 8412 --max-context 786432 --vision --structured-output ^
  --devices 1,0 --tensor-parallel --tensor-split 1,7
```

Why these flags:

- `CUDA_DEVICE_ORDER=PCI_BUS_ID`: without it, two identical cards can swap numbers.
- `--devices 1,0`: ninfer-all keeps the whole KV cache on the first device listed and splits only the routed experts, so the headless card goes first.
- `--tensor-split 1,7`: puts 16 of the 128 experts on the cache card and 112 on the desktop card. At 768K, a 1,3 split fails to start, 71 MiB short.
- `--structured-output`: without it, `response_format` JSON requests get HTTP 400.

For long agent sessions I add three more flags (the numbers behind them are in the cache section):

```
  --host-cache-mib 20480 --prefix-cache-file inkling-2gpu.pfx --admin-token <any string>
```

- `--host-cache-mib 20480`: the pinned RAM that holds the cached conversation. The 8 GiB default overflowed in a long session. 24576 failed to start here (`failed to pin 24576 MiB of host memory`) with 42.8 GB of RAM free, so 20480 is the largest I got working.
- `--prefix-cache-file`: saves that cache when the server stops and restores it at the next start, so a restart doesn't throw away a long conversation.
- `--admin-token`: enables `POST /shutdown` (send the token as a bearer header). The cache file is written only on a graceful stop (Ctrl+C twice, or that request). Killing the process loses the cache and writes no file.

## Results

How I measured:

- Thinking off.
- Temperature 1.0, the model's default. Not greedy, so the speculative draft is accepted less often than in greedy benchmarks.
- Output forced to full length: 800 tokens of code or 1,000 of prose.
- "Net" is output tokens divided by request wall time.
- DSpark on (the default for one request).
- One request at a time.
- 3 measured runs per cell. The one-card rows are 2 rounds per build, alternating builds. The two-card rows are 1 round, so they are estimates.

| Setup | Context | Net tok/s, code 2k / 51k | Net tok/s, prose 2k / 51k | VRAM, GPU 1 / GPU 0 |
|---|---|---|---|---|
| 1 card, UD-IQ4_XS | 256K | 114 / 94-95 | 102-103 / 89 | 23.0 GiB / desktop only |
| 2 cards, UD-IQ4_XS | 768K | 100 / 80 | 79 / 65 | 22.5 / 13.5 GiB |
| 2 cards, UD-Q8_K_XL | 512K | 75 / 65 | 59 / 52 | 21.8 / 15.6 GiB |

GPU 0 numbers include the Windows desktop.

Prompt reads on one card: about 2,000-2,200 tok/s for the code prompt at 2k and 51k. The prose prompt read slower at 2k, about 1,270 tok/s.

The new release against the previous one, same card, alternating runs:

| Build | Context | Code 2k / 51k | Prose 2k / 51k | Prompt read at 51k |
|---|---|---|---|---|
| 2026-11-23.9, MXFP8 cache | 256K | 114 / 94-95 | 102-103 / 89 | 2,183-2,200 |
| 2026-11-21.4, F16 cache | 128K | 105-111 / 75-78 | 95 / 64-65 | 1,039-1,043 |

Greedy, one card, 256K, one round each (for comparison with the greedy numbers other people post):

| | Code 2k / 51k | Prose 2k / 51k |
|---|---|---|
| `--spec none` (no speculative decoding) | 105 / 94 | 105 / 95 |
| DSpark (default) | 137 / 102 | 106 / 92 |

Long prompts. The model had to find a code hidden in the prompt (reasoning effort low) and got it every time:

| Setup | Prompt tokens | Request time | Average prompt read | Peak VRAM, GPU 1 / GPU 0 |
|---|---|---|---|---|
| 1 card, 256K | 259,272 | 203 s | ~1,280 tok/s | 23.0 / desktop only |
| 2 cards, 768K | 370,730 | 450 s | ~820 tok/s | 22.5 / 13.6 GiB |
| 2 cards, 768K | 699,323 | 1,236 s | ~570 tok/s | 22.5 / 13.6 GiB |

Prompt reading slows with depth: near the end of the 699K prompt it was down to about 410 tok/s. Tool calls (3/3), JSON mode and an image test passed on all three setups.

## Tradeoffs and what I didn't test

- **Two cards are slower than one.** With no NVLink or peer access, every MoE layer's partial sums go through host memory. Use two cards only for prompts over 256K.
- **Long prompts take minutes the first time.** Those times are for prompts the server had never seen: 259K tokens took 3.4 minutes on one card, 699K took 20.6 minutes on two. A follow-up turn in the same conversation reads only the new tokens: on a 98,913-token prompt (two cards), the first question took 75 s and the follow-up 6.8 s, with 98,912 tokens served from the cache.
- **The default 8 GiB prompt cache is too small for a long agent session, and a miss is expensive.** In one 768K session with a coding agent (thinking high, 14 tools), turns at 280-375K tokens answered in 2-16 s (cache hits of 89-100%). Three times the server re-read the whole prompt with `cache 0` while the prompt had only grown by 1-3K tokens: 3 minutes at 195K (twice) and 7 minutes 50 seconds at 379K. At that moment `/metrics` showed `ninfer:context_occupancy{pool="host_kv_bytes"}` at 7.76 GiB of the 8 GiB cap. I did not prove that the full cache caused the misses; it is the likely cause and the only full resource I found.
- **What a cached conversation costs.** Four distinct 143K-token prompts (20 GiB cap): the first filled 5.05 GiB of host cache, the second took 9 GiB more, and the total stopped at 18.0 GiB without errors. That is roughly 36 KiB per prompt token, more than the 16.5 KiB of KV alone, because the cache keeps state snapshots too. On that estimate a 379K conversation takes about 13 GiB and a 700K one about 24 GiB, which is more than the 20 GiB I could pin. I have not tested a 700K conversation staying cached.
- **The cache file works, with limits.** With 18.4 GiB of cache, stopping wrote a 19.3 GB file in 13 s and the next start restored it. A file I cut to 10 GB still restored its 3 whole images. It needs a graceful stop (above), and it is only restored by the same build and model file.
- **No headroom at 256K.** That setup starts with 536 MiB free on GPU 1. Windows spills into system RAM instead of failing when VRAM runs out, and then everything runs 5-10x slower, so check `nvidia-smi` and close other GPU apps.
- **1M context (the trained length) doesn't fit safely.** It starts only with `--kv-dtype rk8v4` and leaves 282 MiB free, too little for Windows.
- **Both cards are undervolted** (MSI Afterburner curves, each capped at its board's stock clock for 850 mV). Decode is mostly memory-bound, so the cap should cost it little; prompt reads are compute-bound and could be faster at stock clocks. I haven't measured either at stock.
- **Not tested:** batched concurrent requests, and power limits below 100%. The greedy rows are one round each.
- **Q8 is barely better.** Unsloth measures UD-IQ4_XS at a mean KL divergence of 0.0109 from UD-Q8_K_XL, with the same top token 97.7% of the time. Q8 needs both cards and is 31-42% slower than one card on IQ4_XS.

## How this compares with other 3090 setups

These come from other people's READMEs and use different sampling and quants, so compare them loosely:

| Setup | OS | Context | Decode | Prompt read | Source |
|---|---|---|---|---|---|
| This post, 1 card, undervolted | Windows | 256K | 105 plain greedy at 2k, 94 at 51k; 137 code / 106 prose with DSpark, greedy | ~2,200 at 2k-51k | above |
| This post, 2 cards, PCIe x16 + x4, undervolted | Windows | 768K | 100 net at temp 1.0 | ~570 average over 699K | above |
| maya-inkling-mini, 1 card, 300 W | Linux (Windows port exists, build it yourself) | 344K | 124 plain at 4K, greedy | 2,742 at 4K | [repo](https://github.com/alesha-pro/maya-inkling-mini) |
| vLLM with sm_86 overlays, 1 card | Linux, Docker | 262K | 254 code / 126 prose, DSpark, greedy | not stated | [repo](https://github.com/lulakien/inkling2-mini-3090-recipe) |
| vLLM with sm_86 overlays, 2 cards, PCIe x8/x8, 280 W | Linux, Docker | 262K | 313 code / 166 prose, DSpark, greedy | 7,480 at 2K | [repo](https://github.com/lulakien/inkling2-mini-3090-recipe) |
| vLLM TP2 with NVLink | Linux, Docker | 196K | 143 plain, 223 code with DSpark | ~7,000 | [repo](https://github.com/peaster/inkling-2-mini-vllm-2x3090) |
| llama.cpp b12613, my GPU 1, UD-Q4_K_XL | Windows | 64K max | 43 / 37 net | 730 / 379 | my measurement, earlier build |

If you run Linux, or Docker in WSL2, the vLLM recipes read prompts about 3x faster and decode faster. What this setup offers is native Windows with a prebuilt binary, 256K on one card, and 768K on two PCIe 3090s. That is the longest context I have seen reported for two 3090s. The next highest I found were 458K (vLLM with NVLink, quoted on getrigbase.com) and 557K (a user report on a benchmark site, with no configuration given).

Unsloth's recent "1.9x faster" update applies only to Unsloth's own runtime. Their GGUFs load in plain llama.cpp without the speedup.