# Pulse — Claude Code project guide

Self-hosted monitoring platform. Polyglot monorepo: each service is written in the language the industry uses for that job. Architecture and contracts: `README.md`.

**Each service folder's `README.md` is the spec for that service.** Read it before touching the service. Its TODO checklist is the work queue.

## Current phase

**Phase 1.** In scope: `control-plane/`, `dashboard/`, root `docker-compose.yml`. Do **not** create code in `agent/`, `ingest/`, or `intelligence/` — their READMEs are specs for later phases.

## Map

| Path             | What                                                                                            | Stack                                                             |
| ---------------- | ----------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `control-plane/` | API, check scheduler, incidents, alerts, SignalR hub. **Owns the DB schema via EF migrations.** | .NET 10, ASP.NET Core, EF Core + Npgsql, xUnit + Testcontainers   |
| `dashboard/`     | Status page + admin UI                                                                          | Vite, React, TypeScript (strict), TanStack Query, Tailwind, Radix |
| `agent/`         | _(Phase 2)_ Go host agent                                                                       | —                                                                 |
| `ingest/`        | _(Phase 3)_ Rust ingest path                                                                    | —                                                                 |
| `intelligence/`  | _(Phase 4)_ Python anomalies + AI summaries                                                     | —                                                                 |

## Hard rules

- Postgres is the only contract between services. Schema changes happen **only** through EF Core migrations in `control-plane/`.
- The agent metric payload is defined in `agent/README.md`. Never change it unilaterally.
- One README checklist item per session unless explicitly told otherwise. Small, reviewable diffs.
- When a checklist item is completed, tick its checkbox in that service's README **in the same commit**.
- Every feature lands with tests: control-plane uses Testcontainers integration tests; dashboard uses Vitest where there is logic worth testing.
- No dependencies beyond the stacks listed in the service READMEs without flagging it and getting approval first.
- Never commit secrets. Local connection strings follow `docker-compose.yml`.

## Commands

- Infra: `docker compose up -d postgres` (repo root)
- control-plane (cwd `control-plane/`): `dotnet build` · `dotnet test` · `dotnet watch --project src/ControlPlane.Api`
- dashboard (cwd `dashboard/`): `npm run dev` · `npm run build` · `npm test`

## Definition of done

A change is done when it builds, all tests pass, and behaviour is proven — prefer a test over a manual check. For API features, exercise the endpoint against a running Postgres before claiming completion.
