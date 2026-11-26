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
> **Next:** S1, switchboard core with tests
>
> **Branch:** `feat/switchboard`, one PR

- [S1 — Switchboard core and its tests](#s1)
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

<a id="s1"></a>
### S1 — Switchboard core and its tests · Opus 5.5 / high {#s1}

**Status:** not started.
**Touches:** `tray/Switchboard.cs` (new), `tray/tests/SwitchboardTests.cs` (new), `test-switchboard.ps1` (new).
**After:** none.
**Seam:** `Switchboard` over HTTP, driven against a fake `ISwitchHost` and fake upstream servers on loopback.

- [ ] Red tests committed first and shown failing (stubs only).
- [ ] Auth: no token, wrong token, `Bearer` and `x-api-key` right token (401, 401, 200 on `/v1/models`).
- [ ] `/v1/models` lists every host target; unknown model answers 404 naming the known ids; a body with no
      `model` uses the host's current engine, or 400 when none is up.
- [ ] Routing: each model's body reaches its own upstream unchanged, `Authorization` is not forwarded, status and
      content type come back; an upstream 400 passes through.
- [ ] Stream: three SSE chunks 300 ms apart reach the client one by one (first chunk before the last is sent).
- [ ] Keepalive: a streaming request whose switch takes longer than the interval gets a `:` comment before the
      switch ends, then the stream; a failed switch ends in a `data:` error event.
- [ ] Switch failure on a non-stream request: 503 with the host's message, upstream untouched.
- [ ] Busy gate: while a slow reply for model A runs, a request for model B does not call `EnsureAsync` until A's
      reply ends; two requests for A overlap; a client that drops mid-wait does not leak its slot.
- [ ] `/switchboard/status` and `POST /switchboard/free` reach the host.

<a id="s2"></a>
### S2 — Tray integration, token, toggle, docs · Opus 5.5 / high {#s2}

**Status:** not started.
**Touches:** `tray/LlamaTray.cs` (`partial`), `tray/TraySwitchboard.cs` (new), `install-tray.ps1`,
`allow-switchboard.ps1` (new), `README.md`, `AGENTS.md`, `.gitignore` unchanged (`logs/` already ignored).
**After:** S1.
**Seam:** `install-tray.ps1` build plus `test-switchboard.ps1`; the tray's engine logic is driven by S3.

- [ ] `install-tray.ps1` compiles every `tray\*.cs`; the build succeeds and `test-switchboard.ps1` is green.
- [ ] Targets: every `presets.ini` section, each `StrataSize.ModelId`, each `NinferModel.ModelId`.
- [ ] `EnsureAsync` per engine reuses `StrataStartAsync`, `StrataLoadAsync`, `StopStrataAsync`,
      `NinferStartAsync`, `StartRouter`, `UnloadStrataAsync` and `client.LoadAsync`; waits poll the tray's own
      state; Reserve GPU 0 refuses Strata and two-GPU ninfer with a message.
- [ ] Menu item "Front door (:8409): load any model on request" toggles `SwitchboardEnabled`; the listener
      follows it live and re-binds when Share on network changes.
- [ ] Token file is created on first enable; a bind failure on `http://+:8409/` falls back to loopback with a
      notice that names `allow-switchboard.ps1`.
- [ ] `allow-switchboard.ps1` (admin): URL reservation for the current user and a firewall rule for TCP 8409 scoped
      to the LAN and Tailscale ranges README already uses.
- [ ] README "Using it from another machine" and AGENTS.md "How the pieces fit" describe the front door, the token,
      the one-time admin script and the rule that it takes the GPUs from whatever is running.
- [ ] Loopback smoke with the real tray (no engine started): `/v1/models` lists all ids with the right token.

<a id="s3"></a>
### S3 — Live check from the laptop · Opus 5.5 / medium {#s3}

**Status:** blocked on Paul (GPUs are his, and it replaces what is running).
**Touches:** the running engines.
**After:** S2.
**Seam:** `curl` against `http://<tailscale ip>:8409/v1/chat/completions`.

- [ ] From the laptop: one request each for a router preset, a Strata id and a ninfer id, from a different engine
      each time; every one answers, and `nvidia-smi` shows no card near 24,576 MiB afterwards.
- [ ] Record the cold-switch times in README, dated, with the undervolt stated.
