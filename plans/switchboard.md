# Plan: the switchboard, one front door that loads whatever model a request names

> **Why:** remote clients (Paul's laptop over Tailscale, Claude agents) can use the router but cannot start Strata
> or ninfer, and cannot tell which engine is up. Starting them means walking to the PC. The switchboard is one
> OpenAI-compatible port, `8409`, inside the tray: it reads the `model` of each request, starts or swaps the engine
> that serves it (router preset, Strata size, ninfer profile) using the tray's own start/stop logic, waits until
> it is ready, then proxies the request. Nothing else changes: ports 8410-8412 keep working.
>
> **Done means:** a request naming a router preset, a Strata model id and a ninfer model id each gets answered by
> that engine from a cold or wrong-engine start; a request cannot kill another model's in-flight reply; wrong or
> missing token is refused; the toggle is off by default; README and AGENTS say how to use and secure it.
>
> **Review focus:** a switch that kills another client's streaming reply (S1: busy-gate box) · a stream that is
> buffered instead of passed through (S1: stream box) · the token reachable without the header, e.g. through a
> local reverse proxy such as `tailscale serve` (S1: auth box; loopback gets no exemption) · the tray's own
> router/Strata/ninfer handover racing the switch (S2: switch-state box) · a Windows URL reservation or firewall
> step that silently leaves the port unreachable (S2: bind box).
>
> **Next:** S3, Paul's live check (S2's code is built and installed; its last two boxes need his hands)
>
> **Branch:** `feat/switchboard`, one PR

- [~~S1 — Switchboard core and its tests~~ — SHIPPED 2026-11-25](#s1)
- [S2 — Tray integration, token, toggle, docs](#s2)
- [S3 — Live check from the laptop](#s3)

## Traps

- **C# 5, one `csc` call.** No `?.`, no string interpolation, no `nameof`. The tray builds with the compiler that
  ships in Windows (`install-tray.ps1`).
- **`HttpListener` needs a URL reservation for anything but loopback.** `http://127.0.0.1:8409/` works unelevated;
  `http://+:8409/` (LAN and Tailscale) needs `netsh http add urlacl` once, as admin. Without it, `Start` throws
  access denied and the tray falls back to loopback with a notice.
- **.NET Framework allows 2 connections per host by default.** `ServicePointManager.DefaultConnectionLimit` must be
  raised or two concurrent streams to one engine serialize.
- **Never start Strata, ninfer or a router model while testing.** The GPUs are shared (AGENTS.md). S1 tests use fake
  upstreams on loopback ports; the real engines are touched only in S3, by Paul.
- **The router auto-swaps models (`--models-max 1`), killing the running one.** The busy gate must treat each router
  preset as its own key, not "router".

## Decisions

- Port `8409`, in the tray process (it owns the engine state and the handover). (agent's call: a second process
  would have to re-implement the tray's handover rules and would race them.)
- Token auth always, loopback included, token file `logs\switchboard-token` (untracked, made on first enable).
  `Authorization: Bearer` or `x-api-key`. (agent's call: `tailscale serve` forwards from loopback.)
- Off by default; a tray toggle `SwitchboardEnabled` in `tray-settings.json`. Bind follows `ShareOnNetwork`.
  (agent's call: AGENTS.md says nothing may take the GPUs unprompted.)
- One key per `engine:model`. Requests for the key in use run together; a request for another key waits until the
  in-flight ones end (15 min cap, then 503 + `Retry-After`). Switches run one at a time.
- Streaming requests that need a switch get `200 text/event-stream` at once and `: ` comment lines every 10 s, so
  client timeouts do not fire during a 1-2 minute load; a failed switch then arrives as a final `data:` error
  event. Non-streaming requests wait silently.
- Rejected: a separate control endpoint clients must call first (Pi, Hermes, VS Code cannot), a standalone proxy
  process (races the tray).

### ~~S1 — Switchboard core and its tests~~ — SHIPPED 2026-11-25 {#s1}

Shipped on `feat/switchboard`: `tray/Switchboard.cs` and 42 checks in `.\test-switchboard.ps1`, all green. Body in
[switchboard_SHIPPED.md](./switchboard_SHIPPED.md#s1).

**Note (from S1):** `HttpListener` never raised on a write to a client that had left (a 2 s wait with 150 ms
keepalive writes logged nothing), so a gone client is noticed only when the engine's reply is written. Its queued
switch still runs, which leaves the engine loaded for the next caller. The `catch` that releases a slot when a write
does throw is defensive and cannot be driven from a test.

### S2 — Tray integration, token, toggle, docs · Opus 5.5 / high {#s2}

**Status:** built, installed 2026-11-25 and smoke-tested on loopback; two boxes wait on Paul (the admin script, a menu click).
**Touches:** `tray/LlamaTray.cs` (`partial`), `tray/TraySwitchboard.cs` (new), `install-tray.ps1`,
`allow-switchboard.ps1` (new), `README.md`, `AGENTS.md`, `.gitignore` unchanged (`logs/` already ignored).
**After:** S1.
**Seam:** `install-tray.ps1` build plus `test-switchboard.ps1`; the tray's engine logic is driven by S3.

- [x] `install-tray.ps1` compiles every `tray\*.cs`; the build succeeds and `test-switchboard.ps1` is green.
- [x] Targets: every `presets.ini` section, each `StrataSize.ModelId`, each `NinferModel.ModelId`.
- [ ] (written and compiled; driven only in S3, because the GPUs were busy) `EnsureAsync` per engine reuses `StrataStartAsync`, `StrataLoadAsync`, `StopStrataAsync`,
      `NinferStartAsync`, `StartRouter`, `UnloadStrataAsync` and `client.LoadAsync`; waits poll the tray's own
      state; Reserve GPU 0 refuses Strata and two-GPU ninfer with a message.
- [ ] (menu click not driven; the listener started from the saved flag) Menu item "Front door on :8409" toggles `SwitchboardEnabled`; the listener
      follows it live and re-binds when Share on network changes.
- [x] Token file is created on first enable; a bind failure on `http://+:8409/` falls back to loopback with a
      notice that names `allow-switchboard.ps1`.
- [ ] (needs an administrator shell: Paul) `allow-switchboard.ps1`: URL reservation for the current user and a firewall rule for TCP 8409 scoped
      to the LAN and Tailscale ranges README already uses.
- [x] README "Using it from another machine" and AGENTS.md "How the pieces fit" describe the front door, the token,
      the one-time admin script and the rule that it takes the GPUs from whatever is running.
- [x] Loopback smoke with the real tray (no engine started): `/v1/models` lists all ids with the right token.

### S3 — Live check from the laptop · Opus 5.5 / medium {#s3}

**Status:** blocked on Paul (GPUs are his, and it replaces what is running).
**Touches:** the running engines.
**After:** S2.
**Seam:** `curl` against `http://<tailscale ip>:8409/v1/chat/completions`.

- [ ] From the laptop: one request each for a router preset, a Strata id and a ninfer id, from a different engine
      each time; every one answers, and `nvidia-smi` shows no card near 24,576 MiB afterwards.
- [ ] Record the cold-switch times in README, dated, with the undervolt stated.
