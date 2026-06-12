# ingest — Rust

The hot write path. Receives metric batches from every agent, validates them, and bulk-inserts into Postgres at a rate the control-plane was never meant to handle.

## Why Rust for this part

This is the one service where throughput and latency *are* the product. Rust earns its place with: **no GC pauses** on a path where tail latency matters, `serde` giving near-zero-cost deserialization *and* validation in one step, memory safety for a long-running internet-facing service, and tokio for handling thousands of concurrent agent connections on small hardware. It also makes a great interview story: Phase 3 exists because we deliberately moved ingestion off the C# control-plane when volume grew — the kind of "right tool, measured reason" decision this repo is built to demonstrate.

## Owns

- `POST /api/v1/ingest` — the same contract the agents already speak (see `agent/README.md`)
- API key validation against the `api_keys` table (cached in memory with a TTL)
- Payload validation via serde — reject garbage at the door
- Buffering + **bulk inserts** into the `metrics` table (multi-row insert or `COPY`)
- Backpressure: respond `429` when the buffer is full rather than falling over
- `/healthz` and basic self-metrics (rows/sec, buffer depth)

## Doesn't own

- The schema (control-plane's migrations created `metrics`)
- Any business logic — it never decides what a metric *means*

## Talks to

- **agents** — inbound HTTPS on port `8080`
- **Postgres** — bulk writes only

## Suggested stack

Rust (latest stable) · axum · tokio · serde · sqlx (compile-time checked queries) · tracing · `oha` or `k6` for load testing

## TODO (starts in Phase 3)

- [ ] `cargo new`, workspace-free single crate
- [ ] Serde models mirroring the agent payload exactly
- [ ] `POST /api/v1/ingest` handler: auth → validate → push to channel
- [ ] API key middleware with TTL cache (refresh from Postgres)
- [ ] Background flush task: drain channel, bulk insert on size or interval
- [ ] Backpressure: bounded channel, `429 Too Many Requests` when full
- [ ] `/healthz` + tracing setup
- [ ] Load test against the C# temporary endpoint vs this service; **write the numbers down** — this comparison is the whole justification
- [ ] Cutover: re-point agents, delete the C# endpoint, document in root README
- [ ] Dockerfile (multi-stage, distroless) + wire into compose
- [ ] Integration test: spin up Postgres, fire 10k metrics, assert row count

## Definition of done (Phase 3)

Sustains a load (e.g. 50× the Phase-2 agent volume) on a Raspberry Pi or laptop with flat memory and documented p99 latency — and the README's load-test table proves it.
