Concisely summarize the interesting and high-value content from the below Reddit post and comment thread for another agent's interpretation. Make the output easy to read and skimmable.
# We made Inkling 2 Mini run 1.9x faster locally via Unsloth!

## Original Post

Hey guys, over the weekend, we made Inkling 2 Mini run ~1.9x faster locally! ⚡️ Inkling 2 Mini GGUFs can now reach 125 tokens/s on a RTX Pro 6000 GPU via Unsloth, with no accuracy loss. All quants have also been updated for compatibility with llama.cpp and all engines that use it. Plus, we fixed a Q8_0 KV cache issue that caused it to use more memory than F16. Unsloth is generally faster than or on par with Strata for Inkling-2-Mini decode speed. You can try it now via our open-source GitHub repo (we have a GUI where you can also run image/video and train models): [https://github.com/unslothai/unsloth](https://github.com/unslothai/unsloth) GGUF: [https://huggingface.co/unsloth/Inkling-2-Mini-GGUF](https://huggingface.co/unsloth/Inkling-2-Mini-GGUF) Thanks and hope you all have a lovely week! :)

## Metadata

URL: https://old.reddit.com/r/LocalLLM/comments/1y3ml28/we_made_inkling_2_mini_run_19x_faster_locally_via/
Extracted at: 11/23/2026, 12:29:19 PM
43 upvotes
Name: We made Inkling 2 Mini run 1.9x faster locally via Unsloth!
r/Dashboard
submitted 23 Nov 2026 (Mon Nov 23 2026) by yoracale

---

## Comments

**idimetrix** [Score: 3] (2026-11-23T16:52:57):
  Is the 1.9x in the updated GGUFs or only in the Unsloth runtime? Would be good to know if plain llama.cpp gets any of it after a redownload.

  └─ [REPLY] **yoracale** [Score: 1 point] (2026-11-23T17:00:35):
    Yes, the speed boost is only if you use Unsloth. For llama.cpp, the GGUFs now work inside of it but you will not get the speed gain

    └─ [REPLY] **eugene20** [Score: 2] (2026-11-23T17:17:58):
      I wish someone could convince LM Studio to either allow unsloth llama.cpp in their runtime downloads as they're the most popular models, or allow for custom llama.cpp installs so we can use whatever we want :/
      I just prefer LM Studio's cleaner UI and settings.

**FantasticAnus** [Score: 2] (2026-11-23T17:10:53):
  How does it handle expert streaming/offloading? Is it even worth me sticking it on my 1080ti with 32gb of ddr4 box to try, or shall I leave it for now?

  └─ [REPLY] **yoracale** [Score: 2] (2026-11-23T17:18:35):
    Unsloth should automatically do it for you

    └─ [REPLY] **FantasticAnus** [Score: 2] (2026-11-23T17:53:58):
      

**W61k3r** [Score: 2] (2026-11-23T17:16:25):
  Strata has been behind lexiengine for 3 weeks in performance on every model optimized.

**daphatty** [Score: 1 point] (2026-11-23T16:47:23):
  Not on Mac.

  └─ [REPLY] **yoracale** [Score: 1 point] (2026-11-23T17:00:49):
    It's also slightly optimized for Mac too!

**geldonyetich** [Score: 1 point] (2026-11-23T18:18:42):
  Great work, I owe unsloth’s inference engine a reinstall when I get home.

**Educational_Rent1059** [Score: 1 point] (2026-11-23T16:37:12):
  Gj, now getting more than 125t/s on Q8 quant on rtx 5090, thanks for the update

  └─ [REPLY] **yoracale** [Score: 3] (2026-11-23T16:47:07):
    Thanks for trying it out and glad you're getting great results! :)

**evil-doer** [Score: 0] (2026-11-23T17:06:44):
  Strata is garbage for inkling, but Basalt is really fast. Give that a try

  └─ [REPLY] **GeneralDucky** [Score: 1 point] (2026-11-23T17:59:11):
    Basalt is for RTX 50XX only right?

    └─ [REPLY] **evil-doer** [Score: 1 point] (2026-11-23T18:12:56):
      Yes. Theres forks like Adamant that work on 30/40 series, but it hasnt been updated with inkling support (yet?)

**Equal_Analysis_5229** [Score: 0] (2026-11-23T17:02:27):
  the 1.9x speedup is entirely from unsloth's runtime, plain llama.cpp won't get it even with a fresh download. tested both ways.

  └─ [REPLY] **yoracale** [Score: 2] (2026-11-23T17:18:22):
    Yes we wrote that it's for Unsloth desktop, not for the GGUFs itself. The GGUFs were updated to be compatible with llamacpp etc

