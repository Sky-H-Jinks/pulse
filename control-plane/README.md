# control-plane — C# / ASP.NET Core

The brain of the system. Everything with business logic lives here: what to monitor, when something counts as down, who gets told, and pushing live state to the dashboard.

## Why C# for this part

This is the service with the **richest feature surface** — CRUD APIs, a scheduler, a rules engine, notification fan-out, realtime push, auth, and database ownership. That's exactly the kind of long-lived, feature-dense backend ASP.NET Core is built for: first-class DI, EF Core migrations as the schema source of truth, `BackgroundService` for schedulers, and SignalR for realtime without bolting on a separate websocket layer. It's also the showcase language here, so it gets the most product-shaped service.

## Owns

- The **Postgres schema** (EF Core migrations — no other service creates tables)
- Monitor definitions (HTTP/TCP checks: URL, interval, timeout, expected status)
- The **check scheduler** — a `BackgroundService` that runs due checks and records results
- Incident lifecycle (open on N consecutive failures, close on recovery)
- Alert rules + notification dispatch (webhook first; Slack/email later)
- **SignalR hub** broadcasting check results and incident changes
- API key issuance for agents
- *Phase 2 only:* a temporary `/api/v1/ingest` endpoint for agent metrics, retired when the Rust service takes over in Phase 3

## Doesn't own

- High-volume metric ingestion (Phase 3: `ingest`'s job)
- Anomaly detection or summaries (`intelligence`'s job)
- Any UI

## Talks to

- **Postgres** — read/write everything
- **dashboard** — serves REST API + SignalR on port `5000`
- **intelligence** — calls `POST /summaries/incident/{id}` when an incident closes
- **Notification targets** — outbound webhooks

## Suggested stack

.NET 10 · ASP.NET Core (controllers or minimal APIs) · EF Core + Npgsql · SignalR · `BackgroundService` (reach for Quartz only if scheduling outgrows it) · Swashbuckle/OpenAPI · xUnit + Testcontainers

Start as a **single project** (`src/ControlPlane.Api`) plus a test project. Split into Core/Infrastructure layers only when it hurts — premature clean architecture is the classic portfolio-project killer.

## TODO

### Phase 1 — the uptime monitor
- [x] Scaffold solution: `src/ControlPlane.Api`, `tests/ControlPlane.Tests`
- [x] EF Core entities + initial migration: `monitors`, `check_results`, `incidents`, `api_keys`, `metrics` (empty until Phase 2)
- [x] Monitors CRUD endpoints (+ validation)
- [x] Check scheduler `BackgroundService`: pick due monitors, run HTTP checks concurrently, store results
- [ ] Incident logic: open after N consecutive failures, close on first success
- [ ] Uptime/latency summary endpoint for the dashboard (e.g. last 24h/7d/30d)
- [ ] Webhook notifier on incident open/close
- [ ] SignalR hub: broadcast `CheckCompleted`, `IncidentOpened`, `IncidentClosed`
- [ ] OpenAPI spec exposed (the dashboard generates its client from this)
- [ ] Integration tests with Testcontainers (Postgres)
- [x] Dockerfile + wire into root `docker-compose.yml`

### Phase 2 — agent support
- [ ] API key issuance endpoint + hashing at rest
- [ ] Temporary `POST /api/v1/ingest` accepting the agent payload (see `agent/README.md`)
- [ ] Host metrics query endpoints for dashboard charts

### Phase 3 — handover
- [ ] Delete the temporary ingest endpoint; document the cutover

### Phase 4 — intelligence integration
- [ ] Call `intelligence` for a summary when an incident closes; store on the incident
- [ ] Surface anomalies in the API

### Later / stretch
- [ ] Slack + email notifiers
- [ ] User auth (then multi-tenancy)
- [ ] Maintenance windows, alert silencing

## Definition of done (Phase 1)

A fresh clone can `docker compose up`, create a monitor via the API, watch it go red when the target dies, and receive a webhook — with tests proving the incident state machine.
