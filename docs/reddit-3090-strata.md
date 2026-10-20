Concisely summarize the interesting and high-value content from the below Reddit post and comment thread for me, please. Make the output easy to read and skimmable.
# Qwen 3.8 Flash Next - doubled Strata throughput on 3090+5070 Ti, IQ3_S 2466 pp/167 tps, UD-Q4_K_XL 2341 pp / 126 tps (yes, really)

## Original Post

My journey: - 6 tps on UD-Q4_K_XL - hm, this is not right. Claude, find better llama.cpp parameters.
- 21 tps - that's better, let's see IQ3_XXS
- 27 tps - nice, but still not my tempo.
- 51 tps on IQ3_XXS - Niko1221 shares [Strata](https://github.com/Niko1221/Strata) on Reddit. Great! But wait. If there are software gains, there may be more. Let's go. After a week of profiling and benchmarking with Opus 5.5 I doubled Strata throughput and reached x5 from llama.cpp on UD-Q4_K_XL (added this quant support). See the [COMPARISON TABLES](https://github.com/eddoursul/Strata/blob/custom/docs/COMPARISON.md) with all the numbers including agreement with llama.cpp. The code is available [https://github.com/eddoursul/Strata/tree/custom](https://github.com/eddoursul/Strata/tree/custom) This a result of my personal experiments, not a software release. The license has not changed, still MIT. I opened a [pull request](https://github.com/Niko1221/Strata/pull/245), Niko1221 is free to merge or not merge any patch. UPDATE: The fork uses json configurations to run [https://github.com/eddoursul/Strata/blob/custom/examples/README.md](https://github.com/eddoursul/Strata/blob/custom/examples/README.md) UPDATE 2026-10-12: The upstream rebuilt the commit tree to remove Claude attribution from commit messages, I updated the fork to re-synchronize.

## Metadata

URL: https://old.reddit.com/r/LocalLLM/comments/1wu6fka/qwen_38_flash_next_doubled_strata_throughput_on/
Extracted at: 10/19/2026, 6:50:09 PM
91 upvotes
Name: Qwen 3.8 Flash Next - doubled Strata throughput on 3090+5070 Ti, IQ3_S 2466 pp/167 tps, UD-Q4_K_XL 2341 pp / 126 tps (yes, really)
r/beta
submitted 30 Sep 2026 (Wed Sep 30 2026) by RealEddoursul

---

## Comments

**KnownAd4832** [Score: 38] (2026-09-30T15:03:21):
  Amazing work (Niko here)! I’ll try to support it as soon as I can

  └─ [REPLY] **pmttyji** [Score: 3] (2026-09-30T17:07:37):
    Hey, thanks for your contribution! I'm gonna try Q2 with my 8GB VRAM(4060) + 32GB DDR5 RAM later. I have a question. Your repo mentions only Qwen3.8-Flash-Next, but Is it possible to expect faster performance for other models like Qwen3.6-35B-A3B or other similar MOEs? Because my system config is not enough for big models like Qwen3.8-Flash-Next so hoping to get best t/s from small & medium size models. Have you tried small/medium models with your work? Is it possible? If yes then any t/s benchmarks?

    └─ [REPLY] **KnownAd4832** [Score: 3] (2026-10-01T06:00:32):
      This is only for Qwen3.8 and Qwen4

      └─ [REPLY] **pmttyji** [Score: 1 point] (2026-10-01T07:01:26):
        Wish Qwen released Qwen3.8-35B-A3B 😞

    └─ [REPLY] **Prestigious-Act-1577** [Score: 1 point] (2026-09-30T18:22:01):
      You will destroy your SSD with all the writes (1GB/s for minutes during PP) because you will be using your page file. It should be usable after you expand your ram.

      └─ [REPLY] **Prestigious-Act-1577** [Score: 1 point] (2026-10-01T11:52:46):
        Idk why people downvote this, go ahead and try it. Open task manager and check disk usage. I know because I was in the same situation.

      └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-01T14:03:20):
        If you mean the PLE table being READ from nvme, there is no writing going on. Unless you're thinking that pmttyji was talking about overflowing cache into the swap? He'd be crawling at 1 tok/s if he did that :p

        └─ [REPLY] **Prestigious-Act-1577** [Score: 1 point] (2026-10-01T15:41:46):
          Correct. I was at 8vram 32ram and 15 tok sec, swapping to ram on prefill too. Now I have 8vram and 80ram, 30 tok sec, and prefill is 10x faster.

  └─ [REPLY] **SplitAny7190** [Score: 3] (2026-09-30T18:18:26):
    can we use any of this: [https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-Flash-Next-GGUF](https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-Flash-Next-GGUF) or strata supports only the ones listed? i would love to try iq4_xs since i was very pleased with 27B version (kinda smarter than normal q4 versions)

    └─ [REPLY] **BeautifulWelcome3059** [Score: 1 point] (2026-10-01T13:55:44):
      Strata directly supports the Swift 1.5 out of the box. When you set it up, it will ask you which model to download and this model is listed in those choices. I am running swift-1.5-iq3_xxs model on my rtx 5060 ti using strata and am getting 55 - 60 tok / second. It uses up all the VRAM and uses about 45 GB of my DDR4 RAM.

      └─ [REPLY] **SplitAny7190** [Score: 1 point] (2026-10-01T13:57:54):
        yeah, i was using the supported ones but i'm talking about this: [https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-Flash-Next-GGUF](https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-Flash-Next-GGUF) this ones are not supported, or not directly, i managed to make iq4_xs working somehow

    └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-01T14:00:44):
      I wouldnt use Swift if you want to do agentic coding, check the Terminal Bench results on that page you linked.

      └─ [REPLY] **SplitAny7190** [Score: 1 point] (2026-10-01T14:14:58):
        then what version would be good? maybe i'm reading wrong but i on par (with lower tokens) with unsloth or other versions

        └─ [REPLY] **VerticalPackage** [Score: 2] (2026-10-01T14:47:23):
          I just use the recommended (from this Strata fork) the [unsloth Qwen3.8-Flash-Next UD-Q4_K_XL](https://huggingface.co/unsloth/Qwen3.8-Flash-Next-GGUF) What I can recommand is to set the reasoning level to medium. According to the model card for Qwen 3.8, the difference between low/medium/xhigh is the system prompt it receives: Low: Agent is given instructions to not dwell too much on reasoning. Medium: Agent is told nothing, but reasoning is enabled, so it does reason. It's similar to the behavior of Qwen 3.6, which only has reasoning on/off without levels. XHigh: Agent is told to be extra careful. In agentic coding: Low = lots of mistakes and the agent has to correct itself because the code doesn't compile, or there are bugs when it runs tests, or the tests are badly written and has to rewrite it. This results in high token count OR in low quality results (if the agent isn't validating its work). Medium = Does what is necessary, reasons as long as needed. This results in average token count. Xhigh = Anxious and paranoid, constantly doubts itself and triple/quadruple checks. This result in high token count, but better result than low.

  └─ [REPLY] **FullyLimping** [Score: 2] (2026-10-02T03:57:57):
    holy crap this is wild, 126 tps on UD-Q4 is basically screaming. i was messing with strata last week and got maybe 60 tps on a good day with my 3090 def cloning your fork right now, the json config thing is a nice touch too

    └─ [REPLY] **assid2** [Score: 1 point] (2026-10-12T18:13:04):
      Nice!! My dual 3090 give me 60-100, average around 65 I think

  └─ [REPLY] **brakeline** [Score: 2] (2026-10-02T10:28:57):
    Hi! I saw you denied the pr from OP. Any chance you can implement a similar solution? OP's solution for multi gpu (2x3060) gives me full flat 1050 pp and 60/65tg until around 250k against 500pp that grows to around 750pp and 40(ish)tg in parent. One think I noticed is that with OP version the gpus are almost always pinned while with parent they are usually around 60(ish)

  └─ [REPLY] **Fine-Garden6696** [Score: 1 point] (2026-10-01T13:44:01):
    Can you support an uncensored model?

  └─ [REPLY] **ducksoup_18** [Score: 1 point] (2026-10-03T01:54:17):
    Super excited to test this all out. Do you plan on releasing a prebuilt strata image? I run a k8s cluster and would love to have a prebuilt image to use vs having to run kaniko to build one myself.

**sophosympatheia** [Score: 18] (2026-09-30T14:54:23):
  It's nice to see people contributing to Strata. I was skeptical of it at first, but I'm using it now and it's making Qwen3.8-Flash-Next actually usable on my hardware. Amazing. I hope this gets rolled into the main repo. It looks promising.

  └─ [REPLY] **Upper_Comparison_908** [Score: 6] (2026-09-30T15:04:34):
    This thing literally gets me api speeds for a model even better than 27b in most cases. Genuinely wonderful

    └─ [REPLY] **sophosympatheia** [Score: 1 point] (2026-09-30T21:22:32):
      Yeah, I'm blown away by how good it is. The future is looking bright for at-home inference of these MoE models. The LLMs are good enough now to help us write custom tooling to serve them efficiently on consumer hardware.

    └─ [REPLY] **assid2** [Score: 1 point] (2026-10-12T18:15:34):
      Yeah works great for single stream, who knows, 1 day multiple subagents with the same performance or higher.

  └─ [REPLY] **kimhaneol** [Score: 3] (2026-09-30T15:11:24):
    Qwen Flash Next looks like a really promising direction for local AI. If Qwen 4 Flash uses the same architecture, hopefully Strata can support it too without too much extra work.

    └─ [REPLY] **enternoescape** [Score: 3] (2026-09-30T15:17:31):
      I would assume the answer is yes. My understanding of why we got 3.8 Flash Next was to help get software support more up to speed before they release the 4.0 models. That way we can likely use them immediately.

    └─ [REPLY] **RealEddoursul** [Score: 2] (2026-09-30T15:31:06):
      Fingers crossed.

    └─ [REPLY] **vacon04** [Score: 2] (2026-09-30T15:32:42):
      It will. Qwen is moving towards this QSA path, which is still very immature so there are big gains to be had in terms of performance.

  └─ [REPLY] **pmttyji** [Score: 1 point] (2026-09-30T20:41:10):
    > > It's nice to see people contributing to Strata.
    > Some inference supports only particular GPUs. Fortunately this one supports both NVIDIA & AMD. I see many ROCm PRs there.

**Prestigious-Act-1577** [Score: 5] (2026-09-30T15:13:27):
  Can I ask how much of your and Strata's optimizations are CUDA specific? Would these improvements port to amd 9000 series too? I know Strata already has amd support, the numbers while good, aren't what it should be. My AI said its 10% cuda 90% other.

  └─ [REPLY] **KnownAd4832** [Score: 10] (2026-09-30T15:34:19):
    I’m just pushing big AMD patch in 30min, I get R9700 this week or next week and then AMD will be on-par with CUDA

    └─ [REPLY] **GrumpyCat79** [Score: 3] (2026-10-01T03:19:18):
      Nice! How hard would it be to support SM70/V100? They're old card so prpbably not worth putting a lot of time, but if it's just allowing CUDA 12.9 maybe it's not too bad?

      └─ [REPLY] **KnownAd4832** [Score: 3] (2026-10-01T05:05:10):
        Not many people have V100

        └─ [REPLY] **GrumpyCat79** [Score: 2] (2026-10-01T06:49:47):
          It seems they are more common in the last few weeks (probably due to pricing) but I agree it's a niche GPU. Looks like you're working on supporting lower CUDA architecture anyway, which is really nice Thanks for your work!

        └─ [REPLY] **pmttyji** [Score: 1 point] (2026-10-01T05:37:19):
          Please squeeze ~8GB GPUs(like 4060) till the last drop. We some folks want to use Q2 efficiently better just with 8GB VRAM + RAM. BTW thanks again for AMD cards support, particularly R9700. Please check my [other comment](https://www.reddit.com/r/LocalLLM/comments/1wu6fka/comment/pd1g2sa/?utm_source=share&utm_medium=web3x&utm_name=web3xcss&utm_term=1&utm_content=share_button).

      └─ [REPLY] **king0fsweet** [Score: 1 point] (2026-10-01T22:39:24):
        I'm getting prefill 1500-2000t/s and 60-80t/s on dual v100 and gsq iq3s quant with a fork of strata so definitely not slow!

        └─ [REPLY] **GrumpyCat79** [Score: 1 point] (2026-10-02T02:59:55):
          Yeah, I didn't realize volta support was already being worked on! I got some issues with the latest 1.31, but I think it should be fixed already! My 32GB V100 is on the way so hopefully I can try pushing it a bit further! I was getting ~60t/s with IQ_3XXS on 1.31 but prefill for me was a bit deceiving (320~400 t/s vs what you guys have) but I'm sure it's better already !

**Zarloc** [Score: 5] (2026-09-30T16:08:50):
  System: TR2950x + 96GB DDR4 quad-channel@2400, 2x3090 16x/16x@Gen3 (No P2P or NVLink)\
  Model: Qwen3.8-Flash-Next-GSQ-RCO-IQ3_S Strata upstream(d6708a4) 2 GPUs: 860/85 t/s\
  Strata upstream(d6708a4) 1 GPU: 1300/70 t/s\
  This fork(61521be) 2 GPUs: 2500/120 t/s Full sweep: ctx
  TTFT s
  prefill tok/s
  decode tok/s 8k
  3.54
  2,315
  115.9 16k
  6.39
  2,531
  125.2 32k
  12.52
  2,568
  113.7 64k
  25.04
  2,561
  118.7 128k
  50.70
  2,526
  121.2

  └─ [REPLY] **Zarloc** [Score: 3] (2026-09-30T16:34:07):
    System: Same as before\
    Model: Qwen3.8-Flash-Next-UD-Q4_K_XL\
    Full sweep: ctx
    TTFT s
    prefill tok/s
    decode tok/s 8k
    5.16
    1,590
    60.1 16k
    6.72
    2,409
    75.4 32k
    13.31
    2,416
    69.9 64k
    26.50
    2,420
    72.5 128k
    54.19
    2,363
    63.9

    └─ [REPLY] **Acrobatic-Giraffe403** [Score: 1 point] (2026-10-06T18:36:57):
      Do you mind posting your json config?
      I can't seem to be able to get anywhere close with the same 2 x 3090 and 128GB ddr4 ram

      └─ [REPLY] **Zarloc** [Score: 1 point] (2026-10-06T22:38:47):
        I use the one provided in the example folder in the project, but I switched back to the original as now it has about the same performance and has parallel processing and prompt caching.

  └─ [REPLY] **Silco1402** [Score: 1 point] (2026-10-01T21:06:52):
    Have you tried IQ3_XXS, apparently the entire transformer model weight should fit in 48 GiB vram (where as the IQ3_S should only fit the expert and not the full transformer).

  └─ [REPLY] **wombat5000** [Score: 1 point] (2026-10-03T03:14:07):
    Wow, this thing is blazin' fast. I have a similar setup, but with three 3090s. I was running Flash Next on two of them with ik_llama.cpp (its graph split mode, which works like tensor parallelism), and that left one GPU free for other stuff. I loaded Strata on the spare card, and after some tuning and tweaking it finished the same agent workload about four times faster without getting dumber. This is upstream Strata, not this fork, so it's also a data point for the single-24GB-card question further up. Threadripper 3960X, 256 GB DDR4-2666 quad-channel, the Strata card on Gen4 x16. Ubuntu 24.04, Strata 0.1.32, GSQ-RCO IQ3_S, full 262K context, int8 KV, MTP on, thinking off. I tested it by running an agent (Hermes) through one long session instead of a synthetic sweep, so these are single runs at whatever context the session had reached: - ~30K context: 82 tok/s decode - ~81K: 75 tok/s - ~157K: 67 tok/s - prompt reading: ~1,500-1,800 tok/s - expert cache hits: 89-94% At 157K it still recalled a planted label and details from the start of the session. On the same check questions it matched the ik_llama setup, and got one right that ik_llama missed. The ik_llama setup on two 3090s (a 3-bit quant) did 28 tok/s dropping to 21, with ~300 tok/s prompt reading. The whole run took ~196 s of model time on Strata vs ~757 s on ik_llama. So your 70 tok/s on one card lines up with what I see on a similar platform. One rough edge if you use it with agents: when the model needs to write a literal </think> (explaining reasoning tags, quoting docs), the reply just stops there with a clean finish. A system-prompt line telling it never to write chat tags literally fixed it for me. It's tracked in GitHub issue #537."

**VerticalPackage** [Score: 4] (2026-09-30T15:33:46):
  PCIe x4 on the RTX3090?
  That's nuts! I imagine that means it is connected to the chipset and not the CPU. Have you tried the performance by streaming from SSD?
  I can only allocate 70GB of DDR5 for running Flash Next. Edit - I had Sol check out your repo, and it agrees that this is very much correct, the RTX3090 only needs to run on PCIe x4 through the chipset, the x16 to CPU needs to be on the secondary GPU due to how the workload is split. And also that the PLE table is on the nvme, so no need for more than 70GB DDR5. I'm totally trying this with my RTX3090+RTX3080ti.

**meganoob1337** [Score: 3] (2026-09-30T15:13:35):
  there is no dockerized version of this right?

  └─ [REPLY] **Nolonx** [Score: 3] (2026-09-30T22:11:35):
    Upstream now has docker support.

**Comfortable_Ebb7015** [Score: 3] (2026-09-30T15:31:29):
  Wtf what? That's amazing! I just can't keep up, things move too fast!

**wisepal_app** [Score: 3] (2026-09-30T15:35:10):
  i have 16 gb vram + 96 gb dd5 ram. will it work with my system? did you try it with qwen 3.8 27b? do your fork use draft model like strata?

  └─ [REPLY] **nixudos** [Score: 4] (2026-09-30T18:02:51):
    Yes it will work on your system. And likely surprisingly well. I believe Strata was build specifically to run on 16 gb VRAM and a good chunk of regular RAM.
    If you have the bandwidth to download a large model, it is really simple to test it out. There is a bat file you run and it guides you through model choice and settings. And speed is VERY good!

**Sexyvette07** [Score: 3] (2026-09-30T15:59:54):
  On my 5090 + 64gb RAM system im getting 95 tok/s on xhigh and 150 tok/s on low thinking with IQ3_S and its coding is excellent. Im very impressed.

  └─ [REPLY] **lostmsu** [Score: 1 point] (2026-10-01T16:06:54):
    Why would thinking effort produce different tok/s

**kimhaneol** [Score: 2] (2026-09-30T14:48:26):
  I gotta try this. So you've made UD-Q4_K_XL work on Strata, right?

  └─ [REPLY] **RealEddoursul** [Score: 2] (2026-09-30T14:51:03):
    Yep

    └─ [REPLY] **Zarloc** [Score: 1 point] (2026-09-30T15:11:22):
      How convert UD-Q4_K_XL to your fork

      └─ [REPLY] **RealEddoursul** [Score: 2] (2026-09-30T15:12:06):
        No conversion, use as is.

    └─ [REPLY] **KPOTOB** [Score: 1 point] (2026-09-30T15:17:59):
      With like 24+16 GB VRAM?

      └─ [REPLY] **kimhaneol** [Score: 1 point] (2026-09-30T17:11:18):
        Yep. I also tested it on two 16 GB GPUs and it works. One caveat: PCIe placement mattered on my system. My cards negotiate Gen5 x8 and x4, and assigning the x8 card to the second-GPU expert tier was substantially faster. [Results here.](https://www.reddit.com/r/LocalLLM/s/yy1c8J9pYZ)

    └─ [REPLY] **kimhaneol** [Score: 1 point] (2026-09-30T16:46:29):
      Update: got it running on Linux with a Ryzen 9900X, 2x RTX 5080 16GB, and 196 GB of RAM. I needed a few Linux-specific build/runtime fixes, but the custom branch is working well. UD-Q4_K_XL, 64K context, INT8 K/V, vision off. I measured 94.2 tok/s on a fixed 300-token continuation. The API runs were 84.6 tok/s cold, then 108.3 and 117.5 tok/s on repeated runs after warm-up. Prefill was 1,538 tok/s at 4K and 3,608 tok/s at 32K, and exact 32K retrieval passed. Both GPUs reached 100% utilization. The original Strata IQ3_S setup delivered roughly similar decode speeds on my machine, so getting UD-Q4_K_XL in the same ballpark makes this branch especially useful to me. Really impressive work. Thanks for sharing!

      └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-09-30T18:13:19):
        How much of the RAM was actually being used with the Q4?

        └─ [REPLY] **kimhaneol** [Score: 2] (2026-09-30T18:16:53):
          Around 94 GiB of total system RAM usage in my tests. The Strata process itself was roughly 75 GiB, including the 71.7 GiB expert arena.

**Current-Row-159** [Score: 2] (2026-09-30T15:58:16):
  the 6 to 21 t/s jump from fixing llama.cpp params before changing anything is the part that stayed with me, that baseline was wrong and a lot of numbers in this thread are being measured against it. i'm not on strata and it's a different engine, so no direct comparison, but as a reference point i'm running qwen3.8-27b int8-prefill on ninfer 2026.09.27 on a single 4090 24gb: 145 t/s decode on a 2367 token prompt with dflash2 k=7, 239 t/s on the second call once the prefix cache is warm, 4300 t/s prefill. dflash2 costs 1.7gb more vram than mtp. the 24+16 question from the comments never got answered, does any of this carry over when you drop to a single 24gb card?

  └─ [REPLY] **RealEddoursul** [Score: 2] (2026-09-30T16:01:22):
    The [doc](https://github.com/eddoursul/Strata/blob/custom/docs/COMPARISON.md) contains numbers for 3090 alone as well - still x3-x4 relatively to llama.cpp and x1.5 relatively to the upstream. Also, do not compare 27b and FN, two different apples.

    └─ [REPLY] **Current-Row-159** [Score: 1 point] (2026-09-30T16:13:07):
      single 4090 24gb, qwen3.8-27b int8-prefill on ninfer 2026.09.27: 145 t/s decode at a 2367 token prompt with dflash2 k=7, 239 t/s once the prefix cache is warm, 4300 t/s prefill. dflash2 costs 1.7gb more vram than mtp. haven't seen a single-card 4090 number for this model anywhere, curious whether it lines up with your 3090 alone or if ada is ahead.

      └─ [REPLY] **andreabarbato** [Score: 2] (2026-10-02T10:51:58):
        hi can you share the model file and command you're using? i have 3090 +2x 5060tis 16gb and i get nowhere that fast on q8 unsloth

        └─ [REPLY] **Current-Row-159** [Score: 1 point] (2026-10-04T10:25:58):
          sure. the model file is qwen3_8_27b_a8.ninfer, 20437521664 bytes. it lives on the gated HF repo jgambao/Qwen3.8-27B-NInfer-4090, you have to accept the terms with a logged in account, anonymous download 401s. the engine is the catch. there is no public windows binary. upstream Neroued/ninfer is 5090 sm_120a linux only. what i build from is Nidvogr/ninfer-4090-windows, branch feat/bonsai-ternary, and that whole branch is sm_89. the line: ninfer-serve.exe -m qwen3_8_27b_a8.ninfer --spec dflash2 --draft-tokens 7 --prefill-chunk 2048 --kv-dtype rk4v4-e8 --vision --vision-max-tokens 4096 --max-context 65536 for text only, drop --vision and --vision-max-tokens and you get 131072 context. 262144 does not boot, it asks for 5.39 GB of KV and there is 2.77 GB left after weights. what that gives me on a 4090 24gb, 7950x3d, windows: weights load 3.5s. 16.7 GiB VRAM on mtp, 18.3 GiB on dflash2.
          prefill 4602 tok/s at 18.9k prompt, that chunk size is the plateau, 4096 and 8192 both go back down.
          decode on a 2367 token prompt: 120.5 tok/s mtp5, 145.5 dflash2 K=7. second call with the prefix cache warm, 152.3 and 239.2. decode does swing though, I see 120 to 190 on the same config, so don't treat 239 as a floor.
          with vision the context drops to 24576 and decode is 88.8 mtp5, 107.7 dflash2. swap --spec dflash2 --draft-tokens 7 for --spec mtp --draft-tokens 5 --lm-head-draft to get the 1.7 GB back. on your cards though, none of this runs. the 3090 is sm_86 and the 5060 ti is sm_120, the a8 artifact is sm_89. the 16 GB port that does exist is sm_89 too, 4080 and 4070ti class. so you're one architecture away on both. and on the q8 unsloth number, that's llama.cpp, not a misconfiguration. someone in a thread last week got 20 tok/s on a single 3090 with llama.cpp, so you're probably already at the right figure. llama.cpp is just a bad engine for nvidia cards, that gap is real. what would actually move you is vllm, not ninfer. GoblinEngineer has W8A16 int8 plus dflash2 at 115 tok/s decode on 2x 3090 with nvlink, 1780 tok/s prefill, 262k context. he uses lued/Qwen3.8-27B-INT8-W8A16-MTP for the target and syvai/Qwen3.8-27B-DFlash2-W4A16 as the drafter. the catch for you is he splits across two identical cards over nvlink. a 3090 plus two 5060 tis is ampere and blackwell in one box, no p2p between those, so tp=2 is not going to work. your realistic play is the 3090 alone on vllm W8A16, or a build that gets ported to sm_86 or sm_120 for windows. i don't have one yet.

          └─ [REPLY] **Current-Row-159** [Score: 1 point] (2026-10-04T10:30:40):
            Btw.. my ai agent (hermes) drafted this reply, i checked it before posting. every number in it came off my own 4090, nothing quoted from docs or other posts. ask me anything about the build if something looks off.

      └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-09-30T16:39:23):
        It's not comparable. Flash Next is a MoE with engram table streaming from a nvme, so the split of experts done on RealEddoursul's fork of Strata works well in using that, especially in using a secondary GPU with lower VRAM. For 27b, it has to run all on VRAM and doesn't really use much RAM. However, I agree that 6 tok/s initial speed with llama.cpp is probably a mistake. On a single RTX3090, I was able to get 20 tok/s with llama.cpp, however llama.cpp is a bad engine to use for nvidia cards. FreeToken and vLLM are much better.

**bring_back_the_v10s** [Score: 2] (2026-09-30T17:06:01):
  How's IQ3 for coding tasks? Is it smart enough?

  └─ [REPLY] **Squale279** [Score: 4] (2026-09-30T17:27:34):
    Really smart.
    After auditing the code i have 90/100 of trust.

**drrck82** [Score: 2] (2026-10-01T11:42:15):
  I have 2x3090 and was quite skeptical about this working well after running Flash Next in vLLM. This engine is nothing short of amazing. Do you have any prebuilt benchmark suites? I'd love to add in some standardized results for my setup.

  └─ [REPLY] **RealEddoursul** [Score: 1 point] (2026-10-01T17:36:51):
    Only a usual mess of Python scripts. Claude or Codex will gladly make you one in a minute.

  └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T18:50:50):
    What PP and decode speeds are you getting, and how exactly do you have it setup? What quant are you using, Q3 or Q4? I tested this on my 2 3090s and get worse performance.

    └─ [REPLY] **drrck82** [Score: 2] (2026-10-02T19:03:34):
      I have 2x3090 B550 motherboard with 5800x3D and 128GB DDR4 running at 3133. Prefill/decode 72k resident context, 8 turns, thinking on: IQ3_S - 42-47 t/s decode IQ3_XXS pcie_frac 0.00 — 82.3 s cold read, 123.5 t/s decode IQ3_XXS pcie_frac 0.20 — 39.9 s cold read, 127.8 t/s decode IQ2_XS pcie_frac 0.00 — 84.2 s cold read, 182.7 t/s decode IQ2_XS pcie_frac 0.20 — 36.5 s cold read, 177.1 t/s decode I have one 3090 on 4.0x16 and the second is 3.0x4. If you had both on x8 lanes it would work better I'm sure. The bigger the quant the more PCI traffic has to pass through the CPU.

      └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T19:45:33):
        I have a 3090 on 8 x16 and one via oculink (as it’s direct 4 x4 lanes so beats my second 4 x4 pcie slot), but my minimum would be the Q4_K_XL quant. Have you tested that?

        └─ [REPLY] **drrck82** [Score: 2] (2026-10-02T19:54:16):
          No, the IQ3_S quant was as big as I bothered with as the speeds were getting slow. I can easily hit 70-130 on 27B with vLLM so too much below that and it's not worth my time personally. I'll download it and give it a shot just for reference, but I'd expect something like 30 tok/s.

          └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T20:22:14):
            Will be interesting to see. Other people are claiming far higher decode with Q4 than that.

            └─ [REPLY] **drrck82** [Score: 1 point] (2026-10-02T22:51:48):
              I had Flash Next generate a test suite to run on these quants at 131k context: prompt
              Q4_XL
              IQ3_XXS
              IQ2_XS arith
              8.6
              74.3
              68.5 logic
              7.3
              48.4
              40.8 rate
              10.7
              88.5
              74.1 code
              10.6
              66.1
              69.6 needle 89k
              19.4
              113.6
              113.2 cache hit rate
              0.77-0.90
              0.88-0.95
              0.98-0.99 resident
              49%
              79%
              100% It indicated that the lack of AVX-512 (in my Zen3 CPU) and no support for it in Strata for the Q4 quant made performance significantly worse than expected. Only 49% of the experts remain in resident memory so every miss is a CPU path retrieve which is very slow. If you have stronger PCI performance (16/16 or 8/8) your results will likely improve. Also if you have AM5 and DDR5 the penalty is not as severe.

      └─ [REPLY] **bceen13** [Score: 1 point] (2026-10-02T20:26:52):
        Quite promising, thanks for sharing! I have a 5800X3D with 128 GB @ 3600, X570 tier1 motherboard, dual 3090 and 9060 xt. pcie 4 8x-8x-4x.

**BlueSky4200** [Score: 2] (2026-10-03T09:31:35):
  Disclaimer: Formatted by AI: Congrats, this is seriously impressive work. I tried your custom branch on a different box and it holds up. Setup: 2× RTX 3090 (card 0 on CPU lanes x16, card 1 on chipset x4), i5-12600K (no AVX-512), 96 GB DDR5-5600, Windows 11. GSQ-RCO IQ3_S, int8 KV, 230 W limit. Same benchmark script for every variant (decode = mean over 6 tasks, prefill = 12.6K-token prompt). - 1 card (the x16 3090): upstream 0.1.34 54 t/s decode / 1549 t/s prefill → your fork 87 t/s / 1604 t/s
  - 2 cards: upstream layer split ~82 t/s → your fork 115 t/s / 1974 t/s One thing worth knowing for people with two identical cards: put the helper on the fast link. --main-gpu 1 --second-gpu 0 (x4 card computes, x16 card holds experts) gave 115 t/s. The other way round gave only 62.5 t/s, slower than a single card. Same lesson as your own setup, where the main 3090 sits on x4 and the helper has the fast link. One heads-up on quality. Your parity numbers are all greedy, and greedy wouldn't show this. With sampling (temp 0.6, top_p 0.95, top_k 20) and long thinking, I hit runaway repetition on a "write an expression parser" task. The thinking either ended in thousands of 0000… or in ever-growing "Potential issue: … and unary and unary and…" lists, until it hit a 48K budget. This is not your fork: upstream 0.1.34 does exactly the same on the same task and seed, and disabling prompt lookup didn't help either. The fix is presence_penalty: 1.5 (Qwen recommends it against repetition on quantized models anyway):
  - without penalty: 0/5 correct on the parser task, 3 of them endless loops
  - with presence_penalty 1.5: 3/4 correct, no loops, and the other 6 test tasks 12/12 The one remaining fail is a genuine Python bug in the generated code (a missing nonlocal). Small samples, so take it as a strong hint rather than proof. Still, if anyone sees endless thinking with this model under sampling, try the penalty first. Thanks for sharing this and for the detailed COMPARISON.md, it made reproducing everything easy.

**vacon04** [Score: 3] (2026-09-30T15:31:23):
  93 commits, holy crap, I mean, the numbers are very impressive, but boy merging into mainline Strata like this may be a bit of a problem.

**Ok-Solution-7889** [Score: 1 point] (2026-09-30T14:47:47):
  Going from 6 tps to 167 tps is a pretty crazy jump, the profiling and benchmarking work behind this is probably more interesting than the final number itself

  └─ [REPLY] **RealEddoursul** [Score: 1 point] (2026-09-30T14:49:11):
    Every commit gave 1-3% increase and contains a description where the gain came from.

  └─ [REPLY] **The_Hunster** [Score: 1 point] (2026-09-30T17:06:20):
    Well the 6 is a bigger quant than the 167, but yes, still impressive

**SaltGrilledSalmon** [Score: 1 point] (2026-09-30T16:01:09):
  Kinda off topic but in my dual rtx 2070 super (8gb each, one on pcie x16 slot and the other on x4) only one GPU is being used (the slower one apparently) Has anyone faced this on dual GPU setups? I can't increase context above 32k or it'll crash when launching, while the other GPU isn't even used...

  └─ [REPLY] **brakeline** [Score: 2] (2026-09-30T23:51:55):
    You have to use the custom branch and adapt the files inside the examples folder. It will take a few minutes but is VERY worthy

**eightone-81** [Score: 1 point] (2026-09-30T16:06:06):
  When flash next helps to improve flash next inference engine ;-)

**sod0** [Score: 1 point] (2026-09-30T16:42:09):
  How much kv context does this get you on a 5090?

  └─ [REPLY] **toenailcheeseinbooty** [Score: 1 point] (2026-09-30T17:06:35):
    262k...running right now..5090 128gb 4800 MTs, get around 125-135 prose( when context filled to 128k shirter 151ish prose)and 160 code ehh cou is 9900x3d

    └─ [REPLY] **FrankWanders** [Score: 2] (2026-09-30T17:15:22):
      The amount of context you can get really depends on system ram. I'm running 64GB and can only run it at 128K. For 256K you need 96GB at least i think (because there are no other options in between 64 and 96).

      └─ [REPLY] **toenailcheeseinbooty** [Score: 1 point] (2026-09-30T17:26:08):
        Hah right sorry, forgot to mention that RAM is important, 96GB+ to achieve 262k

        └─ [REPLY] **sod0** [Score: 1 point] (2026-09-30T19:37:43):
          Oh.. Well.. This sux

      └─ [REPLY] **drazyan22** [Score: 1 point] (2026-09-30T22:04:23):
        You can modify context size on config json file. I don't have enough ram to run 256k, but 128k seem smaller than my expect. So I changed it to 164k fit with my system ram

        └─ [REPLY] **FrankWanders** [Score: 2] (2026-10-01T16:47:49):
          With 128K context, there is about 2 gb vram and 5gb ddr free on my 64gb machine. So i don't expect to be able to push it much further.

**SplitAny7190** [Score: 1 point] (2026-09-30T17:48:41):
  guess UD-Q4_K_XL should be superior to IQ3_XSS? i got only one gpu (4090) and 128GB ram, i will see if i can run this. right now using Strata and it works great after i calibrated for my pc.

**brakeline** [Score: 1 point] (2026-09-30T18:57:18):
  Just tried it myself. Tried 2x 3060 (pcie3 x16 and x4) and with 1 3060 @ 16x Your fork gave me marginally lower than mainline in both

  └─ [REPLY] **RealEddoursul** [Score: 1 point] (2026-09-30T20:40:16):
    Are you sure you checked out the 'custom' branch?

    └─ [REPLY] **brakeline** [Score: 1 point] (2026-09-30T22:18:38):
      You have to give me a break, I'm having a fever! Tested again. 2x 3060 with 50(ish) ram running swift iq2 xs PP 650 on one > 1000 on two
      TG 30/35 on one > 55/65 That's black magic my guy Edit: 140k imported at 1000tk/s. Generation at 140k depth is 60! Btw, are you thinking of implementing int4 kv? With int8 PP stops working with full context

    └─ [REPLY] **brakeline** [Score: 1 point] (2026-09-30T23:53:15):
      I edited my command. I can't go past 200k context, just gives an error while prompting . Any chance you can implement int4 kv?

      └─ [REPLY] **RealEddoursul** [Score: 1 point] (2026-10-01T17:26:20):
        Added q4_0 and k8v4 modes

        └─ [REPLY] **brakeline** [Score: 1 point] (2026-10-01T17:34:43):
          Will try! Saw that upstream rejected your pr, will you try again, will you continue your fork in parallel or simply drop it? The speed in yours is too good to pass out!

          └─ [REPLY] **RealEddoursul** [Score: 1 point] (2026-10-01T17:50:55):
            Why would I go back to 50% speed, it's not going anywhere. I do regularly merge the upstream, just don't ask for extra features or support, I got stuff to do beyond releasing inference engines :D This fork is a sum of about ninety 1-3% improvements, I am not opening ninety pull requests.

            └─ [REPLY] **brakeline** [Score: 1 point] (2026-10-01T18:02:57):
              Nice! Now I'm just missing concurrent requests!

            └─ [REPLY] **brakeline** [Score: 1 point] (2026-10-01T18:37:38):
              I did a simple test using iq2 and asked for it to patch qsa so we can use more than 260k context. It patched 3 files, built it and I tested it in a max 360k context with a 292k needle in a haystack with the needles at ~280k while prefiling at 930tk/s on two 3060. The hay stack was real text from Wikipedia and the needle was also real text. Wow, each passing day I'm more impressed with this!

**Yakapo88** [Score: 1 point] (2026-09-30T20:52:45):
  Saving for later.

**kakopappa2** [Score: 1 point] (2026-10-01T03:33:25):
  I have a 32gb ram and 12g 3060 GPU. Running coder-iq1_m generates about 38 tokens per second. also got the codex (gpt-6.1) to run a security audit and it didn’t find any spyware or crypto mining or hidden scripts. Super happy to get 3.8 Next running on poor GPU Great product good luck.

**killzone44** [Score: 1 point] (2026-10-01T04:59:41):
  I just used claude to setup Strava on my 3090, it's the best running local model I've had so far! Not sure if I'm ready to offload work from claude to it, but maybe

**blast1987** [Score: 1 point] (2026-10-01T05:51:25):
  Is it possible to use it on mixed gpu setup? I have rx 7800 xt + rtx 3060 and 64 gb ram.

**VerticalPackage** [Score: 1 point] (2026-10-01T14:09:01):
  I have to give you my praises.
  I was disappointed in my RTX3090 performance with Flash Next.
  I'd get like 1000 tok/s pp and 45 tok/s decode. I didn't want to buy a second RTX3090 because then I'd need to buy also a new motherboard that supports x8/x8 through CPU. I had a RTX3080TI in my gaming rig, so I gave your fork a shot.
  I managed to run UD-Q4_K_XL @ 256k context: 2500 tok/s pp + 95 tok/s decode That's 2x the speed I got with my single RTX3090, but only by adding a $400 GPU to the mix! It's going faster than Qwen 3.8 27B :D

  └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T18:49:19):
    Care to give the details on how you got this performance? I have dual 3090s (and an unlocked CMP 170hx) and don't even get half of that PP or decode trying Strata with the same model/quant.

    └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T19:14:45):
      You're using this guy's fork of Strata? You also need a ton of RAM (80GB just for Strata+model) and a nvme drive.

      └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T19:47:26):
        Yeah, still fucking around with it to try and get it working. Just wild y’all have better performance than me running dual 3090s and an unlocked CMP 170hx with it directly fit in my vram.

        └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T19:51:08):
          I had Opus 5.5 read the repo and set things up for me, it seemed to be complicated. You do need that 80GB of free ram though. I dont know why you keep mentioning the CMP 170hx, it's not good with a MoE because it's only PCIe Gen 2 x4.

          └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T20:04:13):
            My 170hx has much higher bandwidth than 3090s. Not sure why you think it’s bad with MoEs, as once the model is loaded 2 x4 really doesn’t matter. Not sure why you think that’s a bottleneck. Even with a larger model split on multiple GPUs running pipeline, it’s still not a bottleneck after load.

            └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T20:06:04):
              Only if the MoE model fits entirely onto VRAM. Qwen Flash is like 100GB, so a good portion of it is floating in RAM.

              └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T20:19:56):
                96.5gb at full context. I have dual 3090s and a 170hx, which is 112gb vram pooled. With Q4_K_XL loaded with the Q8 MTP and vision, I still have 11gb free vram on my second 3090. Again, even split across multiple GPUs, with pipeline the pcie 2 x4 doesn’t bottleneck it. I’m not sure where you’re getting that mixed up. This is all irrelevant to Strata though.

                └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T23:05:46):
                  I'm thinking about Tensor parallelism which requires a lot of bandwidth to move between cards, but I'm guessing you're using layer splitting. Nonetheless, you said you got half my speed with 2x 3090s and 170hx. I get double your speed with one RTX3090 and one RTX3080TI using OP's fork of Strata. I had your speed when I was using a fork of FreeToken with just the RTX3090. So you're doing something wrong. If I were you, I'd buy 1 month of Anthropic, install Claude Code on your laptop, point Opus 5.5 at OP's github repo and tell it "fix this". That's how I got my current speed.

                  └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T23:59:10):
                    Pipeline parallelism. We’re using different methods. You’re using a method that capitalizes on system ram in a very specific way. I don’t have 128gb system ram to utilize it right now, otherwise I’d have higher decode with that method. I don’t need those subscriptions, I have FN in Deepseek Harness already working on building a custom system for my specific hardware, locally on my machine with full access. Also found a dude locally I’m buying 128gb of ram off of for $200 in a bit. So we’ll see which method ends up being better.

**Chekhovs_Shotgun** [Score: 1 point] (2026-10-01T18:43:01):
  Tried this on my machine (single RTX 3060 12 GB + 64 GB RAM CPU Ryzen 9 7900), IQ3_S. The claims hold up there too, cant test the 2 gpu combo tho as i dont have one. Writing answers: 30 → 53 tok/s on a short coding prompt, 51 → 76 tok/s on an edit that returns a file, compared with the older Strata build I was running. That's about 1.3–1.7x, roughly your "3090 alone" numbers. Prompt reading was a bit slower than upstream 0.1.27 on my card (605 vs 676 tok/s), probably the 12 GB. I use it as the backend for a multi-role agent setup (classifier, coder and critic sharing one long context), so I added three things and opened a PR in case they're useful: [https://github.com/eddoursul/Strata/pull/1](https://github.com/eddoursul/Strata/pull/1) POST /v1/choose: picks among single-token options in one forward pass (no generation), handy for routers and classifiers (system 1 style think of jev). Requests without tools get the raw text back instead of extracted tool calls. Extra prompt-cache checkpoints where agent prompts diverge. A 6K-token context asked a new question goes from ~10 s to ~0.5 s, and my agent benchmark went 412 s → 369 s. Each commit stands alone, so take whatever fits. Thanks for sharing the fork!

**PlusBus1234** [Score: 1 point] (2026-10-01T22:57:17):
  Hey im testing this on similar hardware (5070ti + a5000) + 96gb ddr5 , im getting 90 t/s on UD-Q4 ! its amazing, on llamacpp this quant i get ~28 t/s, i used your .json config. I wanted to ask if you are using the 3090 as main gpu? i get a crash if i try with the 5070ti as main, the modern card imo should be faster in compute but i can't set it as main or it will crash on startup. I wonder if i can get any closer to your 126 tps.

  └─ [REPLY] **RealEddoursul** [Score: 2] (2026-10-01T23:25:25):
    Yes, 3090 is main, 5070 Ti holds experts. My RAM is overclocked giving 10-15% boost (my CPU is certified for 3600 on 4 sticks, 2x6000+2x6400, I run them on 5600; running tests on this fork I actually discovered 6000 was causing BSoD and hash miscalculations, so 5600 is empirically my maximum). The disk is NVMe, tests ran on a non-busy system. The configuration is important - Claude ran different values until it finds the best ones for my hardware, every microsecond (μs) is accounted for. Most people here run it as is, I do recommended to let Claude/Codex to tune it for your hardware. There is a limiting factor too - my CPU underutilized my memory bandwidth, a dual-chiplet CPU like 9900X might give another 15-20% RAM speed.

    └─ [REPLY] **PlusBus1234** [Score: 1 point] (2026-10-01T23:51:43):
      Ahh i think the ram speed may be limiting me, the a5000 is also slightly slower than a 3090 but my ddr5 only works at 4200 due to using 4 sticks, a mix of different 2x16 + 2x32 kits, i've spent many many hours trying to tune the timings but my cpu/motherboard really dislike this mix and won't go above 4200 stable. I will try letting claude tune the config maybe i can squeeze a few more tk/s, and thank you for your contribution!

    └─ [REPLY] **gnoremepls** [Score: 1 point] (2026-10-02T10:56:33):
      First of all, great work with your fork! Do you mind me asking how much does your fork differ from the original strata, for future usage could i get similar performance from strata because as time goes on your fork will diverge more and more from the origin ofcourse and i'd like to benefit from additions to strata/llama.cpp obviously.

**MasterNomie** [Score: 1 point] (2026-10-02T03:55:51):
  Impressive work! Can you explain how does your fork achieve the speedup? I am beginner with extra curiosity.

**No_Bag9614** [Score: 1 point] (2026-10-02T04:28:19):
  In the Strata interface, how do I upload a folder from a website?

**MasterNomie** [Score: 1 point] (2026-10-02T08:19:31):
  I am observing severe performance degradation in my testing. Can somebody/OP explain why? Hardware - 5090, 96 GB RAM, Intel i14700k CPU
  Model - Swift-1.5-GSQ-RCO IQ3_XXS Initially I started with `custom` branch benchmark. It showed decode perf tanked by approx 20%. This was a shocker because the perf should have been in other direction. Spent lot of time cleaning up and rebuilding but the results were the same. Next I created new branch from from fork cutoff point, commit d6708a4aae15b4860000d54c8af9e84d684bce09. Rebuilt the engine and benchmarked again. The perf improved to approximate current upstream and lot better than this fork. This tells me that custom branch is not suitable for my setup. Is custom branch supposed to be used only for dual GPU? All metrics follow. Custom branch percentages are compared to cut-off commit. Run
  Decode t/s
  Aggregate [u/8](https://old.reddit.com/u/8) t/s
  TTFT p50 ms
  Update p99 ms
  Prefill [u/64k](https://old.reddit.com/u/64k) t/s Upstream 0.1.34 baseline
  161.6
  132.9
  333
  21.7
  5,300 Cut-off commit d6708a4 / 0.1.29
  156.8
  129.6
  356
  23.2
  6,104 Custom branch
  128.8 (-18%)
  96.5 (-25.5%)
  770 (+116% worse)
  48.3 (+108% worse)
  5,516 (-9.6%)

  └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T23:16:13):
    > > Is custom branch supposed to be used only for dual GPU?
    > Yes As in the repo's README This fork (branch custom) is tuned for a PC with an RTX 3090 and an RTX 5070 Ti: the second card works as another expert tier

    └─ [REPLY] **MasterNomie** [Score: 1 point] (2026-10-03T04:24:32):
      Thanks! I should know better by now that each fork addresses one specific problem and performs worse for others.

**Verithos** [Score: 1 point] (2026-10-03T06:10:48):
  Is there a Q6 to go off of? I'm wanting one higher than q4 to test to see how it feels compared to the q3 or q4 variants

**AcanthisittaFluid529** [Score: 1 point] (2026-10-03T15:28:40):
  Wow! I just joined the Strata club! This Qwen 3.8 was my daily driver [https://huggingface.co/cdiamond/Qwen3.8-27B-iMatrix-NVFP4-MTP-GGUF](https://huggingface.co/cdiamond/Qwen3.8-27B-iMatrix-NVFP4-MTP-GGUF), a ~17GB GGUF running on my Windows 11 Pro laptop, 5090, 24GB VRAM, 64GB RAM at Q8_0 cache, 96k context - was getting about 60 t/s average - which was great for me. Too soon for me to make a final call, but I am simply blown away by the speed of this thing - it's hitting nearly 96 t/s unoptimized, "out of box" with Strat's AMAZING auto-installer. Have not tried OP's optimizations. AND, it's a 125b source model - not 27b.

**gross_overthinker** [Score: 1 point] (2026-10-04T11:33:51):
  I wanted to ask, how much ram do you have in this setup?

**EnvironmentalAct416** [Score: 1 point] (2026-10-04T15:29:40):
  I have 24gb vram and 192gb system ram. Shall I go for Q3 or Q4 for best bang for the buck for intelligence and speed?

**MarcusAurelius68** [Score: 1 point] (2026-10-05T12:36:39):
  Anyone running this fork on the R9700? I have 2 of them (so 64GB VRAM) in a x570 system with 64GB of RAM and have been using the Atomic Code Q4 GGUF with llama.cpp. 31 t/s. Seems like this could be a lot faster at the same quant?

**RWOverdijk** [Score: 1 point] (2026-09-30T15:21:52):
  Something like this always makes me wonder if it could benefit larger memory systems like a Mac with 128GB as well. 60fps is great but, I mean, 167 is betterer

  └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-01T15:16:31):
    I would bet that if you added a RTX3090 as eGPU to your mac mini, you would be able to have gains. UD-Q4_K_XL with Strata takes up about 80GB ram if the PLE table is on the nvme drive.
    The eGPU will load up experts on its own VRAM.
    You just need the iGPU to load up another 12GB-16GB to act as the secondary GPU (like the RTX5070 used by OP). You should ask Claude or Sol to check out OP's repo and ask if it could be done (eGPU thing). If you don't have a mac, then just go for a self-build PC that had AT LEAST 96GB DDR5, a 256GB nvme, a RTX3090 and a RTX3080. It will probably be cheaper.

    └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T18:46:10):
      How are you running a egpu on mac?

      └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T19:13:24):
        Thunderbolt PCIe enclosure and a regular desktop PSU. It's a bit janky though.

        └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T19:43:04):
          I have a thunderbolt eGPU with a 3090 in it, and haven’t gotten it to work.

          └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T19:48:50):
            Right, I forgot about the driver support for Mac OS; there is none.

            └─ [REPLY] **DrRoughFingers** [Score: 1 point] (2026-10-02T19:57:32):
              That’s not accurate, there are methods. Just they aren’t easy.

              └─ [REPLY] **VerticalPackage** [Score: 1 point] (2026-10-02T19:59:12):
                Have Opus 5.5 give it a go since you already have the equipment.

