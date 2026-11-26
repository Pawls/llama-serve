# Shipped slices of [the switchboard plan](./switchboard.md)

### ~~S1 — Switchboard core and its tests~~ — SHIPPED 2026-11-25 {#s1}

**Status:** shipped on `feat/switchboard`; 42 checks green (`.\test-switchboard.ps1`).
**Touches:** `tray/Switchboard.cs` (new), `tray/tests/SwitchboardTests.cs` (new), `test-switchboard.ps1` (new).
**After:** none.
**Seam:** `Switchboard` over HTTP, driven against a fake `ISwitchHost` and fake upstream servers on loopback.

- [x] Red tests committed first and shown failing (stubs only).
- [x] Auth: no token, wrong token, `Bearer` and `x-api-key` right token (401, 401, 200 on `/v1/models`).
- [x] `/v1/models` lists every host target; unknown model answers 404 naming the known ids; a body with no
      `model` uses the host's current engine, or 400 when none is up.
- [x] Routing: each model's body reaches its own upstream unchanged, `Authorization` is not forwarded, status and
      content type come back; an upstream 400 passes through.
- [x] Stream: three SSE chunks 300 ms apart reach the client one by one (first chunk before the last is sent).
- [x] Keepalive: a streaming request whose switch takes longer than the interval gets a `:` comment before the
      switch ends, then the stream; a failed switch ends in a `data:` error event.
- [x] Switch failure on a non-stream request: 503 with the host's message, upstream untouched.
- [x] Busy gate: while a slow reply for model A runs, a request for model B does not call `EnsureAsync` until A's
      reply ends; two requests for A overlap; a client that drops mid-wait does not leak its slot (see the note in
      the plan: the leak branch itself is not reachable from a test).
- [x] `/switchboard/status` and `POST /switchboard/free` reach the host.
