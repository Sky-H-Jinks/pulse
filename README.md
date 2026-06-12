# Pulse *(working title — rename freely)*

> Self-hosted monitoring with a brain. Uptime checks, host metrics, alerting, and AI-generated incident summaries — one `docker compose up` away.

**Status:** Phase 0 — scaffolding. See [Roadmap](#roadmap).

## Why this exists

Uptime Kuma proved the demand for self-hosted monitoring. Nothing in that space explains *why* something broke — this adds anomaly detection and AI incident summaries on top of the classic status-page core.

It is also a deliberately polyglot system: each service is written in the language the industry actually uses for that job, not for the sake of it. Every choice is justified in that service's README.

## Architecture

```
                         ┌───────────────┐
                         │   dashboard   │  React + TypeScript
                         └───────┬───────┘
                          REST + SignalR
                         ┌───────▼───────┐      webhooks / Slack / email
                         │ control-plane │ ───────────────────────────▶
                         │  ASP.NET Core │      (alert notifications)
                         └───┬───────┬───┘
              owns schema,   │       │ requests summaries
              checks, rules  │       ▼
                             │   ┌──────────────┐
                             │   │ intelligence │  Python + FastAPI
                             │   │ anomalies +  │  (reads metrics,
                             │   │ AI summaries │   writes insights)
                             │   └──────┬───────┘
                             ▼          ▼
                      ┌──────────────────────┐
                      │       Postgres       │  ◀── the shared contract
                      └──────────▲───────────┘
                                 │ bulk inserts
                          ┌──────┴──────┐
                          │   ingest    │  Rust + axum (hot write path)
                          └──────▲──────┘
                                 │ HTTPS metric batches
                          ┌──────┴──────┐
                          │    agent    │  Go — one static binary per host
                          └─────────────┘
```

## Services

| Folder | Language | Role | Why this language (short version) |
|---|---|---|---|
| [`control-plane/`](./control-plane/) | C# / ASP.NET Core | API, check scheduler, alert rules, notifications, realtime hub | Richest feature surface in the system; DI, EF Core, SignalR, background services |
| [`dashboard/`](./dashboard/) | TypeScript / React | Status page, live charts, configuration UI | Hiring-standard frontend; typed client generated from the API |
| [`agent/`](./agent/) | Go | Per-host metrics collector | Single static binary, trivial cross-compilation, tiny footprint |
| [`ingest/`](./ingest/) | Rust | High-throughput metric write path | No GC pauses on the hot path; serde validation; safe long-running network service |
| [`intelligence/`](./intelligence/) | Python | Anomaly detection + LLM incident summaries | The data/ML ecosystem, and the fastest path to LLM integration |

Each service README contains: what it owns, what it talks to, the suggested stack, and a TODO checklist.

## Contracts (how the pieces agree)

- **Postgres is the integration contract.** `control-plane` owns the schema via EF Core migrations — no other service creates tables.
- **`metrics`** — written by `ingest` (temporarily by `control-plane` in Phase 2), read by `control-plane` and `intelligence`.
- **`anomalies` / `insights`** — written by `intelligence`, read by `control-plane`.
- **Agent auth** — agents send an API key issued by `control-plane`; `ingest` validates against the `api_keys` table.
- **Metric payload** — defined once in [`agent/README.md`](./agent/README.md#payload-contract) and mirrored in `ingest`.

## Roadmap

Each phase ends as a **deployable release** with a short write-up. Don't start a phase until the previous one is shipped and documented.

### Phase 1 — Uptime monitor (a complete product on its own)
- [ ] `control-plane`: monitors CRUD, HTTP check scheduler, incidents, webhook alerts, SignalR
- [ ] `dashboard`: status page, monitor management, live updates
- [ ] `docker-compose.yml` runs the whole thing
- [ ] Write-up #1 + screenshots in this README

### Phase 2 — Host metrics
- [ ] `agent`: collect CPU/mem/disk, ship to `control-plane`'s temporary `/api/v1/ingest`
- [ ] `dashboard`: host metric charts
- [ ] Write-up #2

### Phase 3 — Scale the write path
- [ ] `ingest`: Rust service takes over metric ingestion; agents re-pointed
- [ ] `control-plane`: temporary ingest endpoint removed
- [ ] Load test numbers documented
- [ ] Write-up #3 ("why we moved ingestion off the control plane")

### Phase 4 — Intelligence
- [ ] `intelligence`: anomaly detection job + AI incident summaries
- [ ] `dashboard`: insights panel
- [ ] Write-up #4

### Ongoing
- [ ] Path-filtered GitHub Actions CI per service
- [ ] Architecture diagram as an image (replace the ASCII art)
- [ ] Live demo deployment + link here

## Local development

```bash
docker compose up -d postgres
```

Then follow each service's README. Ports: `control-plane` 5000 · `ingest` 8080 · `intelligence` 8000 · `dashboard` 5173 · Postgres 5432.

## Repo conventions

- Every service is self-contained: own README, own Dockerfile, own tests, no cross-folder imports.
- The only shared dependency between services is Postgres and the HTTP contracts above.
- CI runs per service using path filters, so a dashboard change never rebuilds the Rust service.
