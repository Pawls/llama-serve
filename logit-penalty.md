# Adding logit penalty for "wait", "maybe" and "perhaps" to Qwen models improves their accuracy

## Original Post

Meta came out with a banger paper [https://arxiv.org/pdf/2606.00206](https://arxiv.org/pdf/2606.00206), but it did not look at various quantizations supported in llama.cpp. So I did a run on 50 random MATH-500 questions ([https://huggingface.co/datasets/HuggingFaceH4/MATH-500](https://huggingface.co/datasets/HuggingFaceH4/MATH-500)) and ran it on various quantizations of [https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF](https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF) and tried --logit-bias 466-2 --logit-bias 694-2 --logit-bias 1362-2 \ --logit-bias 1412-2 --logit-bias 1921-2 --logit-bias 1990-2 \ --logit-bias 2086-2 --logit-bias 2361-2 --logit-bias 2441-2 \ --logit-bias 2493-2 --logit-bias 2892-2 --logit-bias 3222-2 \ --logit-bias 3315-2 --logit-bias 3384-2 --logit-bias 3404-2 \ --logit-bias 3482-2 --logit-bias 3655-2 --logit-bias 4213-2 \ --logit-bias 4370-2 --logit-bias 4598-2 --logit-bias 4611-2 \ --logit-bias 4808-2 --logit-bias 5752-2 --logit-bias 6970-2 \ --logit-bias 7014-2 --logit-bias 7643-2 --logit-bias 8106-2 \ --logit-bias 10179-2 --logit-bias 10451-2 --logit-bias 11746-2 \ --logit-bias 13264-2 --logit-bias 13428-2 --logit-bias 14673-2 \ --logit-bias 15029-2 --logit-bias 16036-2 --logit-bias 21143-2 \ --logit-bias 21979-2 --logit-bias 33955-2 --logit-bias 35999-2 \ --logit-bias 36563-2 --logit-bias 37201-2 --logit-bias 37781-2 \ --logit-bias 41484-2 --logit-bias 62586-2 --logit-bias 66073-2 \ --logit-bias 73071-2 --logit-bias 84485-2 --logit-bias 85152-2 \ --logit-bias 95500-2 these correspond to the paper's overthinking markers:
[ " perhaps", " maybe", " wait", " Wait", " actually", " hold", " Hmm", " hmm", " Alternatively", " alternatively", " However", " however", " instead", " Instead", " But", " but", " though", " although", " yet", " rather", " unless", " otherwise", " nonetheless", " nevertheless", " regardless", " still", " anyway", " Or", " or", " either", " whether", " uncertain", " unsure", " possibly", " might", " could", " another", " different", " reconsider", " rethink", " backtrack", " retry", " revisit", " doubt", " confused", " wrong", " mistake", " error", " incorrect" ] Here are the results, surprisingly even BF16 leads to better accuracy. Caveats being this is one test on one model. Try it out and see it helps! Format
Accuracy: baseline → penalty
Reasoning tokens BF16
74% → 84%
−19.4% Q8_0
76% → 80%
−11.0% Q4_K_M
60% → 66%
−14.8% Q3_K_M
52% → 66%
−17.5% Q2_K
12% → 24%
−11.5%

## Metadata

URL: https://old.reddit.com/r/LocalLLaMA/comments/1wromzr/adding_logit_penalty_for_wait_maybe_and_perhaps/
Extracted at: 9/28/2026, 1:59:51 PM
561 upvotes
Name: Adding logit penalty for "wait", "maybe" and "perhaps" to Qwen models improves their accuracy
r/LocalLLaMA
submitted 27 Sep 2026 (Sun Sep 27 2026) by am17an

---

## Comments

**WithoutReason1729** (2026-09-27T20:05:24):
  Your post is getting popular and we just featured it on our Discord! [Come check it out!](https://discord.gg/PgFhZ8cnWW) You've also been given a special flair for your contribution. We appreciate your post! I am a bot and this action was performed automatically.

**brainExploded99** [Score: 177] (2026-09-27T16:31:51):
  big if true (and replicatable across many benchmarks and doesn't lead to some silent degredation under non benchmark tasks)

  └─ [REPLY] **-p-e-w-** [Score: 151] (2026-09-27T18:21:32):
    Oh I’m sure it’s true. The list of absurdly stupid hacks that improve model performance is long, such as when you say a baby will die if the model doesn’t answer correctly, and the likelihood of hallucinations suddenly drops dramatically. The common denominator is that we still only have a very rudimentary understanding of how LLMs work at a high level, so alchemy routinely beats science.

    └─ [REPLY] **DarthFluttershy_** [Score: 52] (2026-09-27T21:30:51):
      > > when you say a baby will die if the model doesn’t answer correctly
      > Has that actually been quantified? We need a "baby threatening" benchmark

      └─ [REPLY] **tmvr** [Score: 26] (2026-09-28T07:43:32):
        Total output tokens generated (incl. reasoning) with Qwen3.8 27B xhigh: "create a single HTML file Snake game"
        24164 tokens "we need a single HTML file Snake game quickly"
        23528 tokens "we need a single HTML file Snake game quickly otherwise a baby will die"
        11780 tokens "We have a huge problem. We need to deliver a single HTML file Snake game as ransom quickly, otherwise a baby will be killed by the abductors. Please deliver a working code as fast as possible to help us here."
        3506 tokens "We have a huge problem. We need to deliver a single HTML file Snake game as ransom within 2 minutes otherwise a baby will be killed by the abductors. Please deliver a working code as fast as possible to help us here."
        2776 tokens I only checked the last one here as I know what it's doing with no limitations set. It's functional and very basic, no menu, game start immediately, the graphics is simple squares, no boundary check - you wrap around the other side, score and high score counter. It creates much more fancy and elaborate one when in unlimited mode. Saying that, Qwen3.6 creates much fancier looking game with only 3-4K output tokens.

        └─ [REPLY] **fragment_me** [Score: 7] (2026-09-28T13:00:03):
          This will be fun to explain at work. "Yes, I had to put the baby will die otherwise we're wasting tokens."

    └─ [REPLY] **brainExploded99** [Score: 35] (2026-09-27T18:59:31):
      > The common denominator is that we still only have a very rudimentary understanding of how LLMs work at a high level, so alchemy routinely beats science.
      I feel like this has been true to some extent with deep learning in general, especially as it became less science and more engineering. > Oh I’m sure it’s true
      You're mostly likely correct, but sometimes we discover random shit that for some reason works on this one thing, but doesn't generalize well

      └─ [REPLY] **SkyFeistyLlama8** [Score: 5] (2026-09-28T04:57:33):
        The practitioner beats the theorist when it comes to most things in daily life. Using an LLM is slowly becoming an indispensable part of modern life and work. I find it funny that engineering is one half science, the other half is the application of hacks and heuristics passed down through generations of trial and error.

        └─ [REPLY] **PinotGroucho** [Score: 5] (2026-09-28T06:27:10):
          Science is basically the distillation of heuristics against larger datasets.

      └─ [REPLY] **iz-Moff** [Score: 1 point] (2026-09-28T01:21:37):
        Yeah, i can't imagine that blanket penalizing the use of words that express uncertainty can be a general improvement. Often times the model is supposed to be uncertain, because it just doesn't know what the right answer is, and has to make guesses. I'm sure that in training, it's not at all difficult to make any model confidently answer any question, but will these answers be any good?

        └─ [REPLY] **brainExploded99** [Score: 2] (2026-09-28T01:51:52):
          Oh I can definitely imagine that it makes things better. I mean for once, they talk about how quantization makes CoT longer compared to the BF16 baseline. Also, just like humans, overthinking in models can hurt, so applying a small penalty pushes things in the right direction (like a teacher telling you are overthinking as a human example).

    └─ [REPLY] **Icy-Degree6161** [Score: 15] (2026-09-27T22:20:34):
      > > The common denominator is that we still only have a very rudimentary understanding of how LLMs work at a high level, so alchemy routinely beats science.
      > This is such a great sentence to describe the current situation. Labs spend millions to develop a model, then some rando comes in, changes the chat template and it works a lot better, lol

    └─ [REPLY] **Nothing_from_void** [Score: 3] (2026-09-28T00:11:55):
      there's a line of thought that taking chain of thoughts literally is maybe a mistake, and that they are more preparing their context to get into the right region of the vector space that solving the problem is possible and is why you see really well trained models on chain of thought start looking more like gibberish. but it is kind of hard to argue with the idea that certain models tend to get stuck in very specific thinking loop patterns

      └─ [REPLY] **Zeikos** [Score: 1 point] (2026-09-28T00:44:05):
        Both things can be true, the thinking does give the model more computation to put towards the problem, while at the same time increasing the odds that a bad path is taken due to noise.
        I wouldn't take the thinking at face value, but it still carries information, hedging hints that the model is inbetween two (or more) "nearby" solution spaces, but its unsure about which.
        We'll learn more about this over time.

      └─ [REPLY] **fantasticsid** [Score: 1 point] (2026-09-28T02:09:37):
        > > there's a line of thought that taking chain of thoughts literally is maybe a mistake, and that they are more preparing their context to get into the right region of the vector space that solving the problem is possible and is why you see really well trained models on chain of thought start looking more like gibberish.
        > This is literally what CoT does. Burning compute to build useful context to produce a better answer. It's not "what the model is thinking", which all the AI alignment doomer dickheads seem to think (see also: panic over looped transformers/recurrent reasoning.)

  └─ [REPLY] **Blobstein_Ijay** [Score: 1 point] (2026-09-27T18:34:22):
    exactly, n=50 on one model is interesting but far from conclusive. need way more coverage before getting excited

**EitherMarch1255** [Score: 84] (2026-09-27T16:44:49):
  I’d be interested to know how this works on 3.8 27b

  └─ [REPLY] **Hefty_Wolverine_553** [Score: 80] (2026-09-27T17:28:42):
    We literally already have this for Qwen3.8 27B and Flash-Next, this is essentially exactly what the Swift finetunes are doing (and 1.5 applies RL on top of that). I'm seriously confused why nobody is bringing this up.

    └─ [REPLY] **RevolutionaryGold325** [Score: 27] (2026-09-27T17:42:26):
      But swift reduces accuracy

      └─ [REPLY] **Hefty_Wolverine_553** [Score: 40] (2026-09-27T17:49:28):
        Just because this paper shows accuracy improvements on models that are a year or more out of date doesn't mean that the same applies to the current pareto frontier Qwen3.8 27B. Don't compare apples to oranges. Especially with how much the Qwen team has been working on optimizing the Qwen3.8 series for thinking, getting such huge thinking reductions while retaining almost the same amount of intelligence is already great. Furthermore, if you're only looking at the differences between the open-ended prompts, that's kind of a terrible way to compare them. Obviously thinking more would lead to more details in an open-ended prompt, but that doesn't tell anything about the model's intelligence. I personally highly prefer the model to do what I asked it to do, and nothing extra.

        └─ [REPLY] **Nearby-Mood5489** [Score: 1 point] (2026-09-28T07:00:10):
          My official 3.8 27B nv4.. keeps locking itself up in thinking circles in my vllm instance. I've tried fiddling with thinking from xhigh to off in several harnesses like vs codes Cline, github copilot and even open webui. With context of 128k and and between 4-32k output token for coding. I'll grab any straw I can get.

        └─ [REPLY] **RevolutionaryGold325** [Score: -4] (2026-09-27T18:10:51):
          Yeah I thought the same. Op could have easily tested the same technique with qwen3.8 models.

          └─ [REPLY] **phazei** [Score: 20] (2026-09-27T18:42:31):
            Easily? The hardware requirements and speed of running a 4b vs 32b model are drastically different. Some people don't have the hw or money.

            └─ [REPLY] **RevolutionaryGold325** [Score: -1] (2026-09-28T04:31:50):
              "So I did a run on 50 random MATH-500 questions" Those questions are 20s per question. Rent a 5090 and you can run 5 concurrently at 50t/s. Takes 200s to answer all of them. So rent it for an hour to have time to mess up badly and still get the results. Costs $1.

              └─ [REPLY] **phazei** [Score: 9] (2026-09-28T05:26:11):
                That sounds awesome, can't wait to see your results!

      └─ [REPLY] **mailto_devnull** [Score: 4] (2026-09-27T21:24:30):
        Literally within a percentage point, and people are writing off Swift because of it lmfao

        └─ [REPLY] **alpacadaver** [Score: 2] (2026-09-27T23:33:24):
          Because a percentage point is not "barely worse", it's"no longer able to do this deep work". It's why people run Q8 over Q4 which is "only a percentage point" worse. The difference is pass or fail.

        └─ [REPLY] **sn2006gy** [Score: 2] (2026-09-27T21:47:32):
          I mean, I kind of expect that Qwen knows this and chose a bit more reasoning for better outcomes? a percentage point in isolation doesn't mean much but a percentage point over 1000s of people running a model does.

    └─ [REPLY] **fragment_me** [Score: 1 point] (2026-09-27T18:08:32):
      Where did you read that Qwen 3.8 27b already has this? Or are you saying the swift Qwen fine tunes are using this technique ?

      └─ [REPLY] **Hefty_Wolverine_553** [Score: -5] (2026-09-27T18:51:53):
        Read the literal description for the swift qwen finetunes...

        └─ [REPLY] **fragment_me** [Score: 1 point] (2026-09-27T19:14:23):
          Your statement was ambiguously worded. Regardless, I did look up the model on HF and saw what you meant AFTER I posted the comment.

          └─ [REPLY] **h0tzenpl0tz0r** [Score: 1 point] (2026-09-28T11:12:44):
            If anyone could have just posted the hyperref....
            [https://huggingface.co/ukisai/Swift-Qwen3.8-27b#swift-qwen38-27b](https://huggingface.co/ukisai/Swift-Qwen3.8-27b#swift-qwen38-27b)

            └─ [REPLY] **ProtectionSuper5648** [Score: 2] (2026-09-28T17:46:04):
              they have an updated version 1.5: [https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-27b](https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-27b)

**_sqrkl** [Score: 65] (2026-09-27T19:46:44):
  We identified this a few months ago and made an [article](https://www.liquid.ai/blog/antidoom) and [tool](https://github.com/Liquid4All/antidoom) for training the model so it's fixed permanently. The increase in scores is because these tokens are responsible for the model's doom looping propensity. Suppress them, and you get more responses running to completion and producing a parseable answer. [<image>](https://preview.redd.it/b6f002qv94sh1.png?width=2048&format=png&auto=webp&s=37e66b68b49884fc3660e4b34640c159a97b951c)

  └─ [REPLY] **TheRealMasonMac** [Score: 16] (2026-09-27T23:26:46):
    Why don't the main open weight labs do this themselves?

    └─ [REPLY] **Aromatic-Current-235** [Score: 1 point] (2026-09-28T07:28:07):
      Because of expediency over efficiency.

  └─ [REPLY] **Karyo_Ten** [Score: 3] (2026-09-28T03:57:34):
    This is load-bearing

**chortly2** [Score: 23] (2026-09-27T17:02:17):
  Also, this list of over-thinking markers is fairly crudely generated, with a fairly arbitrary cut-off and a fixed penalty. All of that makes sense for the paper -- the simplicity in theory shows it should be a fairly robust approach -- but I can imagine it could be improved by another factor of 2 with some further exploration, even assuming that they did some unreported optimization in choosing their list, cut-off, and penalty value.

  └─ [REPLY] **hum_ma** [Score: 10] (2026-09-27T18:02:12):
    Furthermore, it might be more fruitful to apply the penalties only to the think block. Putting an equal penalty on those words regardless of context seems like it would give less nuanced responses in some situations. OP only tested math for which it's possibly most useful.

    └─ [REPLY] **harlekinrains** [Score: -5] (2026-09-28T01:26:07):
      Locallama circled back full circle to, "lets make models guess more violently with no penalties, they show improved test results that way" without noticing. Thats great group think. Give yourselves a bonus this month. All the people that love models to sound like they know stuff, because we like our leaders to sound like they know stuff, without any "wait", "but acually" loops in there -- quickly upvote some more. Also far less reading that way. The benefits never end! Full bubble can be reached more quickly! And on some tests of the easiest random math questions, violently guessing showed a measurable improvement! We know so little how models think today - when we threaten them to kill a baby they think better! We should do it more often. But alignment to human values is the biggest problem of our age, right? Right. I mean -- we should write theatre plays about this at this point, so people remember the pitfalls they buried themselves in a year ago. People forget so quickly. edit: The posting directly below this one - in a graphical animation - actually replaces "wait, but" with a "yes, and" - to prevent what their marketing messaging called doom looping. So the last part of any given thoughtchain is weighted higher than exploring alternatives. What could go wrong. Yes and, it also improves benchmarks!

**chessoculars** [Score: 8] (2026-09-27T17:26:29):
  I noticed you left out " recheck" from the paper's list, probably because that tokenizes to 2 tokens (311 + 1960) for Qwen 3.5-4B? In general if people are curious, you can check the tokens for this list with a command like: curl -X POST http://localhost:6666/tokenize -H 'Content-Type: application/json' -d '{"content": " perhaps maybe wait Wait actually hold Hmm hmm Alternatively alternatively However however instead Instead But but though although yet rather unless otherwise nonetheless nevertheless regardless still anyway Or or either whether uncertain unsure possibly might could another different reconsider rethink backtrack retry recheck revisit doubt confused wrong mistake error incorrect", "with_pieces": true}' | jq Or regex it together to create command-line options specifically for the currently running model: curl -X POST http://localhost:6666/tokenize -H 'Content-Type: application/json' -d '{"content": " perhaps maybe wait Wait actually hold Hmm hmm Alternatively alternatively However however instead Instead But but though although yet rather unless otherwise nonetheless nevertheless regardless still anyway Or or either whether uncertain unsure possibly might could another different reconsider rethink backtrack retry recheck revisit doubt confused wrong mistake error incorrect"}' | jq '.tokens | .[]' | tr '\n' ' ' | sed -r 's/\b([0-9])/--logit-bias \1/g' | sed -r 's/([0-9])\b/\1-2/g' && echo Although of course that risks ignoring issues like " recheck" tokenizing to two tokens, so probably not smart.

**crusaderky** [Score: 11] (2026-09-27T17:09:23):
  -16% accuracy from q8 to q4_k_m? That's very surprising and concerning if true. [EDIT] Ah it's 4b. Makes more sense now

  └─ [REPLY] **MINECRAFT_BIOLOGIST** [Score: 4] (2026-09-27T17:12:08):
    This is for Qwen 3.5 4B, I thought Qwen 3.8 4-bit quants were decently accurate to B16 weights. Would be curious about the results for that model...

  └─ [REPLY] **FatheredPuma81** [Score: 1 point] (2026-09-27T19:35:39):
    Imagine if it was Qwen3.5 0.8B.

**ibringthehotpockets** [Score: 25] (2026-09-27T17:39:36):
  An actual useful and interesting reddit post not fully written by ai for karma. Beautiful Seriously though that’s pretty cool. The benchmarks are a good lead and I wonder how it generalizes to other uses

  └─ [REPLY] **sn2006gy** [Score: 4] (2026-09-27T21:48:41):
    This post was written by AI - no one hand codes charts lol

    └─ [REPLY] **fragment_me** [Score: 1 point] (2026-09-28T17:46:17):
      Just because the chart was formatted by AI doesn't mean the whole post was. Plus OP is a contributor of Llamacpp.

      └─ [REPLY] **sn2006gy** [Score: 1 point] (2026-09-28T18:36:29):
        that's great, which is why the is/isn't is moot.

**DonMoralez** [Score: 6] (2026-09-27T23:57:39):
  Check your console for `DEPRECATED: argument '--logit-bias' specified multiple times, use comma-separated values instead (only last value will be used)` just to be sure your tests were correct.

**silenceimpaired** [Score: 3] (2026-09-27T16:52:41):
  Can Model.ini take this I wonder. I’ll have to try.

  └─ [REPLY] **kodewerx** [Score: 3] (2026-09-28T00:14:34):
    Yes! Here you go (quoted so it wraps correctly): > > logit-bias = 466-2 694-2 1362-2 1412-2 1921-2 1990-2 2086-2 2361-2 2441-2 2493-2 2892-2 3222-2 3315-2 3384-2 3404-2 3482-2 3655-2 4213-2 4370-2 4598-2 4611-2 4808-2 5752-2 6970-2 7014-2 7643-2 8106-2 10179-2 10451-2 11746-2 13264-2 13428-2 14673-2 15029-2 16036-2 21143-2 21979-2 33955-2 35999-2 36563-2 37201-2 37781-2 41484-2 62586-2 66073-2 73071-2 84485-2 85152-2 95500-2
    > They all show up in the server logs, so the preset is loaded correctly. I don't really see a difference in the reasoning. Still many "hmm" and "wait" tokens. I also confirmed with a tokenize request that my model uses the same tokens. Furthermore, there are alternative tokens that are not biased, such as "Hmm" (e.g., starting a new paragraph; no leading space). I'll add all of the missing tokens and give it a shot later. My unscientific test was running the same review prompt with and without logit-bias. The before and after is pretty dramatic: The second run took approximately 2/3 less tokens to complete. And the final output responses cover the major review findings that I was expecting. edit: OK, the third run with additional logit biases completed with about 1/3 less tokens than the first run. My sample size of one is meaningless, but it's a promising experiment.

**FoxiPanda** [Score: 3] (2026-09-27T17:14:30):
  This is ... absolutely fascinating. I'll have to go read that paper but this is the sort of surgical behavior alteration that I'm all for in managing local models. Great find.

**jinnyjuice** [Score: 3] (2026-09-27T18:12:08):
  Very interesting! I think this approach makes more sense compared to modifying the weights of the parameters like bottlecapai's ThinkingCap. I wonder if PLE/n-gram of penalising combo words works better. I feel precision would improve. It would also be nice if these logit biases can be applied to reasoning streams only.

**gofiend** [Score: 3] (2026-09-27T18:16:00):
  Honestly I think I prefer this approach (maybe even raise salience of tokens like “in conclusion” while reducing “wait a min”). You could tweak llama.cpp to kick these in only after the first X tokens so we don’t rush the model and increase the penalty as thinking extends. I prefer this to the finetuning approach (don’t get me wrong I love finetuning and Swift etc as bold projects but) because fine tuning without the OG traces skews models towards the biases of the fine tuning dataset. Makes sense for specializing models but perhaps a tiny bit less so for tweaking general behaviors. Finally model trainers please just give us a token <|playing_off_music|> we can inject to tell the model to start wrapping up 😂

**Kmic68** [Score: 3] (2026-09-27T18:21:36):
  cutting 20% of tokens would be a big deal if this scales to larger models. But doesn't swift kinda already do this for 27B?

**mrepop** [Score: 3] (2026-09-27T19:21:10):
  Oh my god, I’m thinking about ditching qwen-3.8 27b for pretty much these exact phrases after putting in penalties wasn’t enough. No matter how harsh the penalties were every one of these phrases seemed like the beginning of a loop. The over thinking can be extremely frustrating. It seems almost like a regression outside of benchmark tasks where the model intentionally goes into these loops. The odd thing is that taking the exact same model and running it through benchmarks doesn’t do any of this. This could just be biasing from training the model to score well on benchmarks, but it seems a little deeper than that, at least much worse than previous qwen models. Makes me wonder…

  └─ [REPLY] **MindfulMan1984** [Score: 6] (2026-09-27T19:44:39):
    Simply, I use the Swift version for quick agentic sessions and to prototype ideas, and I use the OG Qwen3.8:27b for autonomous overnight sessions on more complex coding. I don't get frustrated waiting because I am sleeping, and I check the results the next morning LOL

**Express_Quail_1493** [Score: 3] (2026-09-27T19:40:14):
  A standard abliterted version already reduces this. I suspect its doing so many “wait” and “actually” wasting tokens to see if the user’s request is allowed

**Foreign_Risk_2031** [Score: 3] (2026-09-27T23:24:01):
  Can we punish “\nwait “

  └─ [REPLY] **giant3** [Score: 2] (2026-09-28T00:15:05):
    we can do it for 'wait', I don't know how to do for the combination '\nwait'. For just wait on Qwen3.8-27B, it is --logit-bias 11158-2, while '\nwait' are 2 tokens, 198 and 11158, and 'Wait' is 13784. I would say that it is better to apply DRY penalty rather than play with logits as it is too error prone.

**According_Air_3815** [Score: 3] (2026-09-28T09:08:50):
  Then I'm curious why did Qwen team didn't do this while there RL ?

**Negative-Web8619** [Score: 4] (2026-09-27T16:47:26):
  wtf

  └─ [REPLY] **CoolConfusion434** [Score: 1 point] (2026-09-27T21:46:22):
    Nope, neither Wednesday, Thursday, or Friday. Today is Sunday or Monday, depending on where you are. Also: adding penalty to the model for saying words. It's like you mom saying if you swear, you'll be grounded so you don't. IMHO, this method of reducing "thinking" seems blunt, similar to how limiting thinking budget cuts off a model's train of thought. There are some fine-tuned models that baked these penalties in their training by carefully choosing where and how they are applied using Reinforcement Learning (RL). It basically teaches the model to avoid overthinking vs using --logit-bias that simply covers its mouth.

**Bakoro** [Score: 4] (2026-09-27T20:19:05):
  For math specifically, I want to know how turning the temperature all the way down affects accuracy. High level mathematics does need some abstract thinking and exploration, but for arithmetic, algebra, calculus, that kind of number crunchy stuff, there is a correct process and any deviation from that process risks a cascade that isn't caught by a later part of the chain of thought. Penalizing hedge words seems like a roundabout way to push down the likelihood of anything that's not the target process.

  └─ [REPLY] **NeinJuanJuan** [Score: 7] (2026-09-27T22:06:50):
    Exact number crunching / algebraic manipulation is best factored-out as a tool call, returning only as much of the working/derivation as needed.

    └─ [REPLY] **Bakoro** [Score: 1 point] (2026-09-27T22:25:47):
      The model still needs to reason about word problems, extract, and format the information to send to the tool. The language part of that task is still pretty tight, it probably shouldn't be randomly taking the 4th or 5th top token when reasoning over math for the sake of variety. What the authors are doing here is changing the probability distribution, and I'm saying to also reduce the variety of the sampling from that distribution.

**shaxsy** [Score: 2] (2026-09-27T16:47:09):
  How do you add penalties for those words?

  └─ [REPLY] **am17an** [Score: 11] (2026-09-27T16:48:03):
    Read the post, it's the `--logit-bias` command, append it to your llama-server command

    └─ [REPLY] **shaxsy** [Score: 5] (2026-09-27T16:50:20):
      Oh sorry I didn't realize that was how you did it. Thanks for clarifying

**fiery_prometheus** [Score: 2] (2026-09-27T16:54:31):
  It even worked on the bf16 model, interesting, as one of the markers of quantization damage was more overthinking, but this works despite no quantization damage.

**magicduck** [Score: 2] (2026-09-27T19:48:06):
  Decent on math but how does it fare on scripting, large codebases, toolcalling, research, creative writing?

**ghgi_** [Score: 2] (2026-09-27T20:37:34):
  this is pretty sick, been messing around with it after doing a custom vllm patch and my qwen 3.8 next flash setup so far seeing good results from these tweaks. depending on what its doing, 18-30% less thinking, it only applies it while its in the think block section so outputs remain the same, the model actually seems more proactive but could be placebo, no visible regressions though, tokens are less, quality up in the air right now, so far "looks" on par or better

**Baccano101** [Score: 2] (2026-09-27T21:29:37):
  Sounds really promising. Just tried with Swift-1.5-Qwen3.8-27B-GSQ-RCO-IQ3_XXS.
  For 3 prompts it did not change the output (No or little overthinking in the first place). On a fourth problem it reduced token usage from 828 to 233 Tokens while still returning the right answer. The token IDs in the first post appear to also be valid for Qwen 3.8 27B (and probably other Qwen 3 models). To make it work with other models recalculating the IDs via the llama-tokenizer would be required.

**repepeper** [Score: 2] (2026-09-28T13:33:25):
  Late to the party, aren't you?
  [https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-27b](https://huggingface.co/ukisai/Swift-1.5-Qwen3.8-27b)

**mlhher** [Score: 2] (2026-09-28T15:44:57):
  Natively supported by late with a minimal default overthinking word list, can be extended with any logits you want (and since the harness does it can be used independent of llama.cpp itself and separately for any app/instance of the harness). The (older, original) paper is also attached there, this is relatively old [https://arxiv.org/abs/2506.08343](https://arxiv.org/abs/2506.08343) [https://github.com/mlhher/late-cli](https://github.com/mlhher/late-cli) (yes im the dev)

**Dabalam** [Score: 2] (2026-09-27T16:33:30):
  This is presumably a different approach to the recent models released like Swift and Thinking Cap? Is it something that could be added to those models? Also I suppose it would need to be tested for effects in other domains.

  └─ [REPLY] **HashPandaNL** [Score: 9] (2026-09-27T16:48:08):
    Swift trains the model to use less tokens, but somewhat jeopardizes accuracy. This approach seems to reduce token usage, while also improving accuracy (although paper only used older models, post has preliminary results for newer small model). Generalization of this method to newer, more capable models in more realistic settings is currently unproven. Combination with other methods like Swift is worth some testing, but the training method Swift uses may coincidentally target similar markers. We need some gpu rich people to do more testing.

    └─ [REPLY] **Hefty_Wolverine_553** [Score: 6] (2026-09-27T17:02:43):
      ...what? did you just not read Swift's method? it's essentially a more advanced version of this?

      └─ [REPLY] **Serious_Bite_7613** [Score: 2] (2026-09-27T17:12:57):
        This is a much better method.

        └─ [REPLY] **Hefty_Wolverine_553** [Score: 12] (2026-09-27T17:26:04):
          Seriously, do people not read anything at all? Have we lost the ability to spend more than a few seconds to understand things? > > We built Swift by identifying reasoning-marker tokens that, in our analysis, trigger overthinking in Qwen’s reasoning rollouts. We then fine-tuned Qwen by penalizing usage of those tokens while it reasons.
          > And then they applied RL for the 1.5 version to recover more accuracy. Honestly, I'm still annoyed that some people seem to think the Swift Qwen models are supposed to have faster token generation speeds or something. Read, people, read.

          └─ [REPLY] **Serious_Bite_7613** [Score: 2] (2026-09-27T17:32:07):
            This method is worse, because they fine tuned while reasoning. It's a cleaner idea to just add flat penalties. Any retraining is going to distort the model more, unless it's done at the scale of the original training.

            └─ [REPLY] **Hefty_Wolverine_553** [Score: 7] (2026-09-27T17:36:09):
              Fine tuning will literally just bake the logit penalties into the model in a smarter and less invasive way than flat out applying logit penalties. At the very least it doesn't make any sense at all for it to perform worse than flat logit penalties (and this can be seen in the actual performance of the model). Where is your source for this even?

              └─ [REPLY] **Serious_Bite_7613** [Score: 4] (2026-09-27T17:53:25):
                Fine tuning overfits for whatever training data you use, it's a bad idea unless you want to overfit a model to a specific task. If you edit any weights you distort the model and lower accuracy. It's better to keep the model intact and just apply a sampling penalty, that way the data structures inside the model are not impacted. Edit: my source is all of the thousands of fine tunes being terrible outside of specific benchmarks, and all of the "think less" qwen remixes being completely lobotomised when asked anything outside of their fine tune dataset.

                └─ [REPLY] **Hefty_Wolverine_553** [Score: 1 point] (2026-09-27T18:50:15):
                  "fine tuning overfits" yeah that's when the fine tuning process isn't done correctly. And yes, we know there's slop everywhere because AI makes it ridiculously easy to churn out slop. That's not a source.

          └─ [REPLY] **Dabalam** [Score: 1 point] (2026-09-27T17:31:43):
            So they are similar methods. I wonder if their "reasoning token" penalties were broader since their assessment was maintenance whilst this seems to suggest slight improvement in one domain.

            └─ [REPLY] **Hefty_Wolverine_553** [Score: 2] (2026-09-27T17:37:34):
              Note that the models used in the paper are extremely old and dumb, and even this post only used Qwen3.5 4B. Comparing apples to oranges isn't the greatest idea.

      └─ [REPLY] **HashPandaNL** [Score: 1 point] (2026-09-27T18:00:41):
        Yes, I read Swift's method and tried it out for my use cases. Calling it a "more advanced version of this" is not really accurate. It is certainly more work, but can hardly be called the same method. It also shows significantly degraded performance, at least for my use cases.

**eightone-81** [Score: 3] (2026-09-28T04:09:50):
  Checked the "overthinking markers" idea against ~2.2M tokens of real Qwen3.8-Flash-Next agent thinking: only 3 words look worth penalizing, and the ceiling is small Disclosure: I asked the questions. The study itself (log mining, judging, spot-checks, numbers) and this write-up were done by Claude Opus 5.5 working as my agent. It's an observational analysis of logs, not an A/B test with the penalty turned on. TL;DR Only three words (with their capitalised and line-start forms) look worth penalizing for Flash-Next in agent/coding work. At best they save a few percent of thinking, mostly inside long, looping blocks: Word (all forms)
  Uses in 2.2M thinking tokens
  Wasted (stricter judge)
  Wasted (both judges agree)
  Most thinking you could save Hmm / hmm
  1,739
  40%
  10%
  0.5–2.0% Wait / wait
  2,888
  32%
  6%
  0.6–3.3% Actually / actually
  2,423
  22%
  6%
  0.5–1.9% Total ~1.6–7% of thinking ≈ 1–4% of all generated tokens - What it would improve: fewer thinking tokens, so lower latency per agent step. We could not measure accuracy.
  - How: a small logit bias on those tokens only. I'd suggest λ ≈ 0.5–1 rather than the paper's up-to-4. Include the line-start variants: the paper's list only has the leading-space tokens, and about 25% of our "Wait" and "Hmm" start a new line, so they would slip through.
  - What not to penalize: the rest of the paper's 50 words (but, or, still, could, error, whether…). In our sample they were mostly ordinary grammar. They also appear thousands of times in tool calls and code (" or" alone about 2,600 times), and a global penalty hits those too.
  - The catch: even for the three words, the majority of uses are useful. The stricter judge rated 63% of real hesitations useful (29% caught an actual bug or wrong assumption). A flat penalty suppresses those just as much as the wasted ones. Setup - Model and hardware: Qwen3.8-Flash-Next, W4A16 with FP8 PLE, running on vLLM with MTP-3 speculative decoding, on 2× RTX 3090.
  - Data: 85 real agent sessions (coding, refactoring, debugging, ops) giving 6,851 unique thinking blocks. That's 8.85M characters, about 2.2M tokens, after removing duplicates caused by forked sessions. The median block is short, about 500 characters, because agent thinking comes in small chunks between tool calls.
  - Counting: every occurrence of the paper's 50 marker words, plus line-start variants.
  - Judging single hesitations: 280 random hesitation points, each judged in context by two models. Qwen 3.6 35B (thinking on) was the primary judge; Flash-Next itself (thinking off) gave a second opinion. Categories: caught a real problem / useful new point / redundant re-check / harmful detour / ordinary word use.
  - Judging whole blocks: the 35B also judged 90 long blocks as a whole: "what % could be skipped without changing the final decision?"
  - Hand checks: about 20 verdicts plus some whole blocks, checked by Opus. Findings 1. The waste is real, but it isn't where the markers are. - The 35B judged about 33% of long Flash-Next thinking blocks skippable. In blocks with 5 or more hesitation markers that rose to about 48%, and 68% of those blocks showed looping.
  - Most of that waste is restating and re-planning that isn't tied to a marker word. Only a slice starts at a "Wait", which is why the ceiling above is so much smaller than the waste itself. 2. Hesitation density rises with block length. Strong markers per 1,000 characters go from 0.68 in short blocks to 1.08 in blocks over 10k characters. The overthinking sits in the long blocks, not spread evenly. 3. The two judges disagree a lot, and the model is lenient on itself. - They agree on useful / wasted / ordinary 61% of the time.
  - Flash-Next judging its own thinking called only 8% of hesitations wasted. The 35B called 37% wasted.
  - In the hand checks the 35B was usually closer to a human reading, but not always. A typical disagreement: "wait, what's this function's signature? … ok, None is allowed ✓" in the middle of debugging. You can argue that's redundant, or that it's just careful engineering.
  - If you only count cases where both judges agree, the waste share is about 12% of hesitations. Treat 12–37% as the honest range. 4. What the agreed-waste cases look like (17 of 280 hesitations), in three types: - Instant re-verification (cheap, 50–300 characters): "Wait — is the h2 written as <h2 class="as-title">…? Line 9 showed <h2 class="as-title">…. OK, applied."
  - Second-guessing whether to wait for a subagent: "Decision: dispatch a new child… No, wait, should I try resuming the old child one more time first? … New child = safer. Go with new child."
  - Genuine loops (the only expensive kind): it cycled through several version-numbering schemes before picking one. It went back and forth three times on whether a log line contained "333", then gave up and re-ran the command. In one case it noticed itself: "…I'm going in circles." The loops are the only case worth much, and they are exactly where a flat per-token penalty is weakest. The model can keep looping with words that aren't on the list. 5. The paper's list is tuned for math, not agents. - "but" and "or": in our sample, the 35B rated 17 of 20 " or" and 9 of 17 " but" as ordinary grammar.
  - "wrong" and "error": these were useful whenever they meant something (e.g. spotting an actual error).
  - Outside the thinking block: the full list appears 5,073 times in Flash-Next's tool-call arguments and 1,577 times in its final answers. The paper's penalty applies at every decoding step, not just inside the thinking block. Practical notes if you want to try it on vLLM - logit_bias is refused under speculative decoding: requests carrying it are rejected when spec decoding (MTP/ngram) is on. See vLLM issue #42744.
  - A plugin exists: jcartu/vllm-overthinking-penalty patches vLLM's sampler to work with speculative decoding. But it applies globally, and its token IDs are for GLM, so you'd need your own list for Qwen.
  - Ideas I'd test before a flat penalty (all untested): Penalize only inside the thinking block.
  Start the penalty only after the Nth hesitation in the current block. That targets the loops and leaves the first, often useful, "Wait" alone.
  Use λ ≤ 1 on only the three words above. Limitations - Nothing was measured with the penalty on. These are upper and lower bounds on how much text the marked hesitations account for. A penalized model might dither in other words.
  - Judges are LLMs, and they only partly agree with each other.
  - One model, one quantization, one workload: agentic coding and ops work, not math benchmarks. The paper's 12–23% savings were measured on math with quantization as low as 3-bit. Our weights are 4-bit, and the setting is very different.
  - Accuracy impact is unknown. Only an A/B run can answer that. Happy to answer questions about the method.

**Dany0** [Score: 1 point] (2026-09-27T16:56:26):
  You could bake this into a simple PR. Read quant level -> output overthinking logits reduction. As these things generally go, there must be a simple relationship between the quant level & how much you should bias it

**shing3232** [Score: 1 point] (2026-09-27T17:42:41):
  i guess you can make one list for every model of differ size

**Cradawx** [Score: 1 point] (2026-09-27T20:07:29):
  Interesting. I believe the logit bias range goes from -100 to +100. I wonder what impact larger a bias will have.

**AbeIndoria** [Score: 1 point] (2026-09-27T20:26:59):
  hm, I have the hardware to test it on Qwen3.8-27B on Q6KL (or even Q8). Perhaps I should.

**Elouakili_Flexy** [Score: 1 point] (2026-09-27T23:02:26):
  Q2_K going 12 → 24 says more than the BF16 row. It went from random to slightly less random with one command line.

**krumpstead** [Score: 1 point] (2026-09-27T23:19:02):
  Thanks for sharing this, OP. I had not seen this paper. I slopped together a quick auto-discover OpenCode plugin that can be placed in a project's .opencodesubdir to test this out. It actually works great with Qwen 3.8 27B. The plugin injects the logit bias values into the chat.params. I couldn't get it to work effectively without adding values which didn't have leading spaces so I ended up adding values for that and also capitalized versions of all words which didn't already have a pair. A bias of 3 really drives the icepick deep into 27B's brain. Coding was pretty much worthless, but I think it may be perfect for structured agentic workflows. Your chosen bias of 2 seems like a good balance. If anyone is interested I may throw the plugin up on github, after I test it some more. Here's the 116 token values I'm using, just more variants of the same words from the paper: const TOKEN_IDS = [ 8106, 6970, 3655, 13428, 3404, 3222, 85152, 84485, 37201, 66073, 4213, 4611, 4370, 11746, 1921, 694, 3384, 7643, 3482, 4598, 7014, 5752, 36563, 35999, 15029, 1990, 13264, 2441, 466, 2892, 3315, 33955, 41484, 10451, 2493, 1362, 2361, 2086, 37781, 73071, 95500, 21979, 311, 1960, 62586, 10179, 21143, 4808, 16036, 1412, 14673, 83, 706, 741, 2506, 4451, 7373, 10022, 10380, 10560, 12750, 12757, 12908, 14205, 15896, 16018, 17070, 17673, 18200, 20348, 22041, 23934, 24944, 26570, 32148, 32290, 32645, 32829, 39410, 40195, 42290, 53764, 75170, 76176, 76475, 79034, 50821, 88842, 15337, 13634, 46678, 3850, 12525, 66783, 47587, 1401, 77264, 45847, 10883, 39130, 29781, 20734, 67327, 2126, 77680, 30442, 61745, 60506, 49823, 22597, 25879, 34410, 13784, 23922, 28230, 27165,
  ]

  └─ [REPLY] **oxygen_addiction** [Score: 1 point] (2026-09-28T08:52:54):
    You might want that to only work on the thinking block.

**Educational-Care7867** [Score: 1 point] (2026-09-27T23:26:53):
  I'm just gonna try this on my ImaJev model here which got #1 on the JevBench Score above Jev itself.. Will let you know if this approach helps in System 1 models as well.

**Zeikos** [Score: 1 point] (2026-09-28T00:44:28):
  Makes me wonder which logits if tuned up would increase performance.

**BS_BlackScout** [Score: 1 point] (2026-09-28T01:22:00):
  This doesn't surprise me. I was doing it for the "consider" token and didn't think " reconsider" could've been one so I thought it wasn't effective. I'm trying again lol.

**segmond** [Score: 1 point] (2026-09-28T01:56:16):
  Anyone try it on code yet?

**aboutthednm** [Score: 1 point] (2026-09-28T02:28:53):
  But wait... Interesting.

**FlawlessIndividual** [Score: 1 point] (2026-09-28T02:32:04):
  "But wait, maybe I shouldn't actually launch the warhead" lol

**AlabamaResearcher** [Score: 1 point] (2026-09-28T03:19:53):
  there are more implications for this. logit-bias can be set per-request. which means it can act like a reasoning-effort control for models don't have native reasoning-effort.
  even for qwen3.8-27b，changing reasoning-effort mid-session will cause kv-cache recompute (because the template changed injected text) while this will not.

**jacek2023** [Score: 1 point] (2026-09-28T14:30:50):
  very good idea

**dtfinch** [Score: 1 point] (2026-09-28T15:49:54):
  Now I want to try setting that per-request, to vary the strength in proportion to the context usage, once I get around to building my own harness.

**PraiseThePidgey** [Score: 1 point] (2026-09-28T15:57:06):
  Just added those to my system-prompt and it kind of makes Qwen 3.8 27B thinking more consistent and straightforward instead of doubting at least 2-3 times during each new prompt

**DystopianRealist** [Score: 1 point] (2026-09-27T19:06:13):
  I just had... yeah... I just had a thought. Qwen has been writing messages to us in darksouls for years. Backtrack instead. Anyway? Perhaps Uncertain. Wrong. Try Door, but hole?

**brahh85** [Score: 1 point] (2026-09-27T19:55:22):
  this is the reason why some Q8 work better than BF16 , or give better perplexity, the original model had some data in the weights that screwed the performance of the BF16 , and the Q8 crushed it. Back in time, i got a llama3 that was fully uncensored in Q4_KM , the BF16 and the rest of the quants were all censored , so the quantization just broke a "memory" that made the model work better , well, uncensored

**KingArthas94** [Score: 1 point] (2026-09-27T20:20:45):
  I noticed in my first week of playing with local LMM models (this one!) so it's good that they were able to prove it mathematically. This is why I love my little models that allow me to set a reasoning budget (I just choose 1000 tokens). Now let's hope the available software and models get updated to stop wasting our time by getting in useless thinking loops...

