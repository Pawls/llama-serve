# Plan: Ubuntu 26.04 dual boot, with Strata measured against Windows

> **Why:** to measure what Linux gains on this PC, mainly for Strata (Qwen3.8-Flash-Next). On Windows, with the model
> split across both GPUs, Strata pins only 8 GiB of its ~33 GiB expert arena; Linux pins all of it. Strata's docs
> measured +40% to 3x prompt speed from that on other two-card rigs. Windows stays the main OS. Ubuntu goes on the
> spare 477 GB NVMe (Windows disk 3), with its own boot loader.
>
> **Done means:** Ubuntu boots next to Windows and Windows is unchanged. F: is read-write from Linux. Strata runs on
> Linux with tool calls and images verified. `bench-depth.ps1` numbers exist for both OSes on the same model, recorded
> in README.md, with a keep-or-drop call on Linux.
>
> **How-to:** every command is in [docs/ubuntu-setup.md](../docs/ubuntu-setup.md). This plan tracks what is done.
> From Linux, this repo is `/mnt/f/AI/llama-serve` (after slice U2).
>
> **Phone copy:** <https://claude.ai/artifact/HhL8exmnP4sHTKfs7Nextd> (private). After editing this plan or the guide,
> run `python plans/build-ubuntu-page.py <scratch>/ubuntu-dual-boot.html` and republish that file to the URL.
>
> **Next:** U1, write the USB stick and install Ubuntu on the 477 GB drive (guide §1-2, done by Paul at the PC)
>
> **Branch:** not cut yet (docs only so far; commit to `main` per slice)

- [U1 — Ubuntu installed on the spare NVMe, both OSes boot](#u1)
- [U2 — Driver, Windows drives and Claude Code working on Linux](#u2)
- [U3 — Strata compiled and serving on Linux](#u3)
- [U4 — Linux vs Windows benchmark recorded](#u4)
- [U5 — Optional: llama.cpp router on Linux](#u5)

### U1 — Ubuntu installed on the spare NVMe, both OSes boot · Opus 5.5 / medium {#u1}

**Status:** not started. Mostly done by hand at the PC (guide §1-2). An agent can only check the result, so the
disk choice is the step to get right.
**Touches:** disk 3 (XG7000, 477 GB), BIOS boot order, USB stick
**After:** none

- [ ] Ubuntu 26.04 LTS USB stick written with Rufus (GPT)
- [ ] WD Elements USB drive unplugged during the install
- [ ] Installed with "Erase disk" (or the manual layout) on the **477 GB** drive only; the summary screen showed the
      four 2 TB drives unchanged
- [ ] Verify from Ubuntu: `lsblk -o NAME,SIZE,MODEL,FSTYPE,MOUNTPOINT` shows `/` and `/boot/efi` on the 477 GB drive,
      and the 2 TB NTFS partitions untouched
- [ ] GRUB lists Windows (`GRUB_DISABLE_OS_PROBER=false`, `sudo update-grub` prints "Found Windows Boot Manager")
- [ ] Verify Windows still boots from GRUB **and** from F11, and `Get-Disk` in Windows shows disks 0-2 unchanged

**Cancel if:** the installer won't offer the 477 GB drive on its own. Stop before writing anything, and use the
manual layout from guide §2.

### U2 — Driver, Windows drives and Claude Code working on Linux · Opus 5.5 / medium {#u2}

**Status:** not started.
**Touches:** `/etc/fstab`, `/mnt/{c,d,f}`, apt packages, `~/.claude` on Linux
**After:** U1

- [ ] `nvidia-smi` shows both RTX 3090s with driver 580 or newer
- [ ] F: and D: mounted read-write at `/mnt/f` and `/mnt/d`, C: read-only at `/mnt/c`, by UUID in `/etc/fstab`
      (guide §3)
- [ ] Verify: the write test on `/mnt/f/AI` passes, and `/mnt/f/AI/llama-serve/plans/ubuntu-dual-boot.md` opens
- [ ] Verify after a reboot: all three mount without help
- [ ] git, gh, pwsh, build-essential, python3-venv and nvtop installed
- [ ] Claude Code installed, signed in, and run from `/mnt/f/AI/llama-serve`; `git status` there works
      (`safe.directory` and `core.fileMode false` set)
- [ ] Windows `~/.claude` (CLAUDE.md, skills) brought over, by cloning its repo or copying from `/mnt/c`
- [ ] This plan updated **from Linux** and committed, to prove the round trip

**Cancel if:** F: mounts read-only even after a clean Windows **Shut down**. Then check Fast Startup again and run
`chkdsk F:` from Windows before going on.

### U3 — Strata compiled and serving on Linux · Opus 5.5 / high {#u3}

**Status:** not started. No ready-made Linux engine exists (Windows-only zips through v0.1.39), so setup compiles
one. 26.04 needs CUDA 13.x and g++-14 installed by hand (guide §4). This slice is where most unknowns sit.
**Touches:** `~/strata`, `~/strata-data`, CUDA toolkit in `/usr/local/cuda-13*`, port 8411
**After:** U2

- [ ] `cuda-toolkit-13-x` from NVIDIA's ubuntu2604 repository and `g++-14` installed; `nvcc --version` says 13.x
- [ ] `~/strata` cloned; `F:\AI\Strata-data` copied whole to `~/strata-data` (73 GB)
- [ ] `./setup.sh` with the guide's flags finishes with `[ok] engine compiled` and the image encoder built,
      without downloading the model again
- [ ] `./run-iq2_xs.sh` reaches `ready:`; the start log shows the whole expert arena pinned (no 8 GiB cap)
- [ ] Verify: `/health` says `loaded: true, images: true`
- [ ] Verify: `pwsh ./test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411 -Model qwen3.8-flash-next-iq2_xs -Runs 3`
      passes 3/3
- [ ] Verify: the image test from the Windows session (a PNG with "7319" in blue) is read correctly
- [ ] Record the start time and VRAM per card, compared with Windows (45-50 s; 22.0 / 20.6 GB)

**Cancel if:** the build fails after both documented fixes (g++-14, a CUDA 13.x toolkit; Strata's
`docs/TROUBLESHOOTING.md`). Open a Strata issue with the log instead of patching the engine here.

### U4 — Linux vs Windows benchmark recorded · Opus 5.5 / medium {#u4}

**Status:** not started. The Windows baseline is `logs/bench-depth-20261004-2030.txt`.
**Touches:** `bench-depth.ps1`, `README.md`, `logs/`
**After:** U3

- [ ] `bench-depth.ps1` runs on Linux pwsh: `curl` instead of `curl.exe` when not on Windows. Verify that a Windows run
      still works (2k depth only is enough)
- [ ] Full matrix on Linux: `pwsh ./bench-depth.ps1 -Model qwen3.8-flash-next-iq2_xs -BaseUrl http://127.0.0.1:8411`
- [ ] Prompt speed compared on fresh tokens at 52k and 128k (Windows: ~3,100-3,200 t/s), from the prefill rows
- [ ] README.md Strata section: a Linux column next to the Windows numbers, with the date
- [ ] Decision written in the README: whether Linux is worth booting for Strata, from the measured difference

**Cancel if:** none. A "no real difference" result is still a result. Record it and strike the slice.

### U5 — Optional: llama.cpp router on Linux · Opus 5.5 / medium {#u5}

**Status:** not started. Do it only if U4 shows Linux worth using, or to compare the 27B lanes too.
**Touches:** `~/llama.cpp`, a Linux copy of `presets.ini`, a `start-router.sh`, port 8410
**After:** U4

- [ ] llama.cpp b10951 built with `-DGGML_CUDA=ON` (same build as Windows, so the comparison is fair)
- [ ] Linux presets file with Linux model paths, kept next to `presets.ini` and generated from it, not hand-copied
- [ ] Router starts on 8410. Verify: `pwsh ./test-tool-calls.ps1` passes on `qwen3.8-27b` and
      `qwen3.8-27b-dflash2`
- [ ] `bench-depth.ps1` on `qwen3.8-27b-dflash2` recorded next to the 2026-09-14 Windows numbers

**Cancel if:** U4 shows no gain worth booting into Linux for.
