# Ubuntu 26.04 dual boot: setup guide

How to install Ubuntu on the spare NVMe of this PC, reach the Windows drives from it, and get Strata running there. The
plan that tracks progress is [plans/ubuntu-dual-boot.md](../plans/ubuntu-dual-boot.md). This file is the how-to.

Facts about this PC, checked from Windows on 2026-10-04:

| | |
|---|---|
| Motherboard | ASRock Z690 Pro RS (boot menu: **F11**, setup: **F2** or **Del**) |
| CPU, RAM, GPUs | i5-12600K (has integrated graphics), 64 GB, 2x RTX 3090 24 GB |
| Secure Boot | **Off**, so the NVIDIA driver needs no key enrollment |
| Windows Fast Startup | **Off** (`HiberbootEnabled = 0`), so Linux can write to the Windows drives |
| BitLocker | Off on C:, D:, F: |
| Hibernate | Available. **Never hibernate Windows** while you also use Linux: a hibernated NTFS drive mounts read-only, or gets damaged if forced |

The drives (Windows disk numbers). **Install only onto the 477 GB drive.** It is the only drive that size.

| Disk | Model | Size | Windows letter | Use |
|---|---|---|---|---|
| 1 | Crucial CT2000P3PSSD8 (NVMe) | 1863 GB | C: | Windows. Don't touch |
| 2 | Crucial CT2000P310SSD8 (NVMe) | 1863 GB | F: (`NVMe_G4`) | Models, `F:\AI`. Linux mounts it |
| 3 | **XG7000-512 (NVMe)** | **477 GB** | none | **Ubuntu goes here** (holds only an empty 16 MB Windows reserved partition) |
| 0 | Seagate ST2000DM008 (HDD) | 1863 GB | D: | Data |
| 4 | WD Elements (USB) | 1863 GB | E: | External. Unplug it during the install |

## 1. Before you start (Windows, ~20 min)

1. Download the Ubuntu 26.04 LTS desktop ISO from <https://ubuntu.com/download/desktop> (~6 GB).
2. Write it to a USB stick of 8 GB or more with Rufus (<https://rufus.ie>): pick the stick and the ISO, partition scheme
   **GPT**, then **Start**. This erases the stick.
3. Unplug the WD Elements USB drive, so the installer shows one fewer 2 TB disk.
4. Optional, frees ~2.5 GB of VRAM on GPU 0 in **both** operating systems: in the BIOS
   (**Advanced > Chipset Configuration**), set **Primary Graphics Adapter** to **Onboard** and **IGPU Multi-Monitor**
   to **Enabled** (names can differ slightly between BIOS versions). Then plug the monitor into the motherboard's HDMI/DisplayPort instead of a 3090. The 3090s then
   hold only models.

## 2. Install Ubuntu (~30 min)

1. Shut Windows down (Start > Power > **Shut down**, not Restart or Hibernate).
2. Power on, press **F11** for the boot menu, pick the USB stick (the `UEFI:` entry).
3. Choose **Try or Install Ubuntu**, then **Install Ubuntu**.
4. When asked about software, tick **Install third-party software for graphics and Wi-Fi hardware**. This installs
   the NVIDIA driver.
5. At **How do you want to install Ubuntu?** choose **Erase disk and install Ubuntu**. Then, on the disk choice,
   pick the **477 GB XG7000** drive, and only that drive.
   - If the screen shows a single disk and doesn't let you choose, go back and pick **Manual installation** instead:
     on the 477 GB drive, create a 1 GB **EFI System Partition** and an **ext4** partition at `/` for the rest.
     Set **Device for boot loader installation** to the 477 GB drive.
   - The four 2 TB drives must say *unchanged* on the summary screen. If one doesn't, go back.
6. Finish the install, remove the USB stick, reboot.

**Choosing the OS at power-on.** The PC boots whichever drive is first in the BIOS boot order. To pick each time, press
**F11** at power-on. To get a GRUB menu that lists Windows too, run this once in Ubuntu:

```bash
echo 'GRUB_DISABLE_OS_PROBER=false' | sudo tee -a /etc/default/grub
sudo update-grub        # should print "Found Windows Boot Manager"
```

Then set the 477 GB drive first in the BIOS boot order (**Boot > Boot Option #1**). Windows Update can't overwrite this
GRUB, because it lives on a different drive.

## 3. First boot: driver, Windows drives, tools (~30 min)

### NVIDIA driver

```bash
nvidia-smi              # both RTX 3090s and a driver version; Strata needs 580 or newer
```

If `nvidia-smi` is missing: `sudo ubuntu-drivers install`, then reboot.

### Mount F: (and C:, D:) read-write

Ubuntu reads and writes NTFS with the kernel's `ntfs3` driver. Mount the drives by UUID, so the mounts survive reboots
and changed disk order:

```bash
sudo blkid | grep -i ntfs       # find the UUIDs: LABEL="NVMe_G4" is F:, LABEL="HDD" is D:
sudo mkdir -p /mnt/f /mnt/c /mnt/d
```

Add one line per drive to `/etc/fstab` (`sudo nano /etc/fstab`), with your UUIDs:

```text
UUID=<F: uuid>  /mnt/f  ntfs3  uid=1000,gid=1000,windows_names,nofail  0  0
UUID=<D: uuid>  /mnt/d  ntfs3  uid=1000,gid=1000,windows_names,nofail  0  0
UUID=<C: uuid>  /mnt/c  ntfs3  uid=1000,gid=1000,windows_names,nofail,ro  0  0
```

`windows_names` stops Linux from creating file names Windows can't open. `nofail` lets Ubuntu boot if a drive is
missing. **C: is read-only on purpose**: nothing from Linux should change Windows' own files.

```bash
sudo systemctl daemon-reload && sudo mount -a
ls /mnt/f/AI/llama-serve        # this repo, from Linux
touch /mnt/f/AI/.linux-write-test && rm /mnt/f/AI/.linux-write-test && echo writable
```

If F: mounts read-only with a "dirty" or "hibernated" warning: boot Windows, shut it down with **Shut down** (not
Restart), and try again.

**This repo from Linux.** `F:\AI\llama-serve` is `/mnt/f/AI/llama-serve`. Git needs two settings there, because
NTFS has no Linux permissions or owner:

```bash
git config --global --add safe.directory /mnt/f/AI/llama-serve
git -C /mnt/f/AI/llama-serve config core.fileMode false
```

Don't run git on the same repo from both systems at once (you can't anyway: only one boots at a time). Models and
build trees belong on the Linux drive (ext4), not on F:. NTFS through `ntfs3` is fine for documents and copying, but
slower for many small files and for builds.

### Tools

```bash
sudo apt update && sudo apt install -y git curl gh build-essential python3-venv python3-pip htop nvtop
curl -fsSL https://claude.ai/install.sh | bash      # Claude Code
sudo snap install powershell --classic              # pwsh, for this repo's .ps1 scripts
gh auth login
```

Then start Claude Code in this repo and point it at the plan:

```bash
cd /mnt/f/AI/llama-serve && claude
# "Read plans/ubuntu-dual-boot.md and do the next slice."
```

Claude Code on Linux keeps its own settings in `~/.claude` (empty at first). Your Windows `~/.claude` is a git
repository: to carry over CLAUDE.md, skills and settings, clone it from its remote, or copy it read-only from
`/mnt/c/Users/Paul/.claude`.

## 4. Strata on Linux (~1-1.5 hours, mostly compiling and copying)

**The engine is compiled on this PC.** Strata publishes ready-made engines only for Windows (checked against every
release up to v0.1.39). On Linux, `./setup.sh` compiles the engine for the 3090s (20-40 minutes, once). Its automatic
install of the build tools covers only Ubuntu 22.04 and 24.04, so on 26.04 install them first:

1. **CUDA Toolkit 13.x from NVIDIA's repository.** Ubuntu's own `nvidia-cuda-toolkit` package is CUDA 12.4, too old.
   Follow NVIDIA's network-repo steps for Ubuntu 26.04 at
   <https://developer.nvidia.com/cuda-downloads?target_os=Linux&target_arch=x86_64&Distribution=Ubuntu&target_version=26.04&target_type=deb_network>,
   but install only the toolkit, not their driver:

   ```bash
   sudo apt install -y cuda-toolkit-13-4        # or the newest 13.x listed
   ls /usr/local/cuda-13*/bin/nvcc
   ```

   Don't use CUDA 12.9: its headers clash with 26.04's glibc 2.43 (Strata issue #601).
2. **g++-14.** 26.04's default compiler is gcc 15, which CUDA 13.0 refuses. Newer 13.x may accept it, but g++-14 works
   with every toolkit, so build with it:

   ```bash
   sudo apt install -y g++-14
   ```

3. **Get Strata, and the model files without downloading them again.** Copy the whole `F:\AI\Strata-data` (73 GB:
   the model, the image encoder and the MTP draft layer, with the `.done` markers setup checks) to the Linux drive:

   ```bash
   git clone https://github.com/Niko1221/Strata.git ~/strata
   rsync -a --info=progress2 /mnt/f/AI/Strata-data/ ~/strata-data/     # ~10-15 min NVMe to NVMe
   ```

4. **Run setup**, with the same choices as the Windows install:

   ```bash
   cd ~/strata
   CXX=g++-14 CUDAHOSTCXX=g++-14 STRATA_NVCC=$(ls -d /usr/local/cuda-13*/bin/nvcc | tail -1) \
     ./setup.sh --yes --family qwen --model IQ2_XS --gpus 0,1 --vision gpu --context 262144 \
     --port 8411 --data-dir ~/strata-data --no-browser --no-start
   ```

   It should report the model files as present. It compiles the engine and the image encoder, may prepare the model
   again (minutes), and writes `run-iq2_xs.sh`. If the compile fails, see the Strata repo's `docs/TROUBLESHOOTING.md`
   ("Linux: the engine does not compile").
5. **Start and check it:**

   ```bash
   ./run-iq2_xs.sh                 # in its own terminal; Ctrl+C stops it
   curl -s http://127.0.0.1:8411/health          # "loaded": true, "images": true
   ```

   The start log should show **no** 8 GiB pinning cap: on Linux the whole expert arena is pinned. That is the main
   reason Linux should read prompts faster on two cards.

## 5. Benchmark against Windows

The Windows numbers are in [README.md](../README.md#strata-qwen38-flash-next-125b-moe) and
`logs/bench-depth-20261004-2030.txt`. Run the same matrix from Linux with pwsh:

```bash
cd /mnt/f/AI/llama-serve
pwsh ./test-tool-calls.ps1 -BaseUrl http://127.0.0.1:8411 -Model qwen3.8-flash-next-iq2_xs -Runs 3
pwsh ./bench-depth.ps1 -Model qwen3.8-flash-next-iq2_xs -BaseUrl http://127.0.0.1:8411
```

`bench-depth.ps1` calls `curl.exe` and `nvidia-smi`. On Linux `curl.exe` doesn't exist, so the script needs a small
change (use `curl` when not on Windows) first. That change is part of the plan's benchmark slice.

## 6. Optional: the llama.cpp router on Linux

Only worth doing if the Strata numbers make Linux interesting enough to run the 27B lanes there too. Build the same
llama.cpp version (b10951) with CUDA, copy `presets.ini` to a Linux copy with `/mnt/f/...` (or ext4) model paths,
and start it on port 8410 the way `start-router.ps1` does. See the plan's router slice.

## Undoing it

Ubuntu lives on its own drive and its own boot loader. To remove it: in Windows, open **Disk Management**, delete the
partitions on the 477 GB drive, and set Windows Boot Manager first in the BIOS boot order. Windows is untouched either
way.
