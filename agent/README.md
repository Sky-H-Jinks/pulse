# agent — Go

A lightweight collector you drop onto any host you care about. Gathers system metrics, batches them, ships them home. One binary, no runtime, no dependencies.

## Why Go for this part

Agents live on *other people's machines*, which dictates the requirements: a **single static binary** with no runtime to install, **trivial cross-compilation** (`GOOS=linux GOARCH=arm64 go build` covers the Pi from a Windows dev box), a tiny memory footprint, and goroutines for running collectors concurrently without ceremony. This is why the industry's agents — Datadog's, Prometheus's node_exporter, Telegraf — are written in Go. Doing the same here is the defensible choice, not a gimmick.

## Owns

- Collecting host metrics on an interval: CPU %, memory, disk usage/IO, uptime, load
- Batching + shipping over HTTPS with retry, exponential backoff, and jitter
- Graceful degradation: buffer in memory when the server is unreachable, drop oldest first
- Its own identity (`agent_id`) and API key from config

## Doesn't own

- No local storage, no web UI, no config server — it's deliberately dumb. Smart agents are how agents get heavy.

## Talks to

- *Phase 2:* `POST` batches to control-plane's temporary `/api/v1/ingest`
- *Phase 3:* re-pointed to the Rust `ingest` service on port `8080` — **only the URL changes**

## Payload contract

Defined here, mirrored by `ingest`. Change it in both places or not at all.

```
POST /api/v1/ingest
Authorization: Bearer <api key>
Content-Type: application/json

{
  "agent_id": "0f8c…",
  "host": "pi-5",
  "sent_at": "2026-06-12T10:15:00Z",
  "metrics": [
    { "name": "cpu.percent",        "value": 23.4,  "at": "2026-06-12T10:14:55Z" },
    { "name": "mem.used_bytes",     "value": 3.1e9, "at": "2026-06-12T10:14:55Z" },
    { "name": "disk.used_percent",  "value": 71.2,  "at": "2026-06-12T10:14:55Z", "labels": { "mount": "/" } }
  ]
}
```

## Suggested stack

Go (latest stable) · standard library `net/http` for shipping · `shirou/gopsutil` for metrics · config via flags + env vars (a YAML file is a later nicety) · **no framework** — a framework-free agent is part of the point

## TODO (starts in Phase 2)

- [ ] `go mod init`, project layout: `main.go`, `internal/collect`, `internal/ship`
- [ ] Config: server URL, API key, interval, hostname override (flags + env)
- [ ] Collectors: CPU, memory, disk, uptime/load via gopsutil
- [ ] Batcher: gather on a ticker, flush on size or interval
- [ ] Shipper: POST with retry + backoff + jitter; in-memory buffer with a cap
- [ ] Graceful shutdown (flush on SIGTERM)
- [ ] `-version` flag and a `/healthz`-style self-log line on start
- [ ] Cross-compile targets via Makefile or GoReleaser: linux/amd64, linux/arm64, windows/amd64
- [ ] Example systemd unit in `deploy/`
- [ ] Dockerfile (`FROM scratch`) for the demo compose setup
- [ ] Unit tests for batcher + retry logic (httptest server)

## Definition of done (Phase 2)

`scp` one binary to the Pi, run it with two env vars, and watch the Pi's CPU graph appear in the dashboard. Binary under ~10 MB, RAM under ~20 MB.
