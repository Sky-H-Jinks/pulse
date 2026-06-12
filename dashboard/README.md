# dashboard — TypeScript / React

The face of the system: public status page, live monitor dashboard, charts, and configuration UI.

## Why React + TypeScript for this part

It's the hiring-standard frontend stack, which matters for a portfolio project — but it also earns its place technically: TypeScript lets the API client be **generated from the control-plane's OpenAPI spec**, so the C# API and the UI can never silently drift apart. React's ecosystem covers the two hard UI problems here out of the box: realtime updates (the official SignalR JS client) and time-series charting.

## Owns

- Public **status page** (the classic green/red uptime bars)
- Authenticated dashboard: monitor list, detail view, latency/uptime charts
- Monitor + alert configuration forms
- Incident timeline
- Live updates everywhere via SignalR (no polling)
- *Phase 2:* host metric charts per agent
- *Phase 4:* anomaly markers on charts + AI incident summary panel

## Doesn't own

- Any business logic or state the API doesn't give it — if the UI needs a calculation, the control-plane grows an endpoint

## Talks to

- **control-plane** only: REST (generated client) + SignalR on port `5000`. Dev server runs on `5173`.

## Suggested stack

Vite · React · TypeScript (strict) · TanStack Query · `@microsoft/signalr` · Recharts · Tailwind CSS · Radix UI primitives for accessible components · `openapi-typescript` for client generation

## TODO

### Phase 1 — the uptime monitor
- [ ] Scaffold with Vite (React + TS strict mode)
- [ ] Generate typed API client from the control-plane OpenAPI spec; wire into TanStack Query
- [ ] Monitor list page with current status
- [ ] Create/edit monitor form (URL, interval, expected status)
- [ ] Public status page: uptime bars per monitor (last 90 days), overall banner
- [ ] Monitor detail: latency chart, recent checks, incident history
- [ ] SignalR connection with auto-reconnect; live status flips without refresh
- [ ] Dockerfile (static build behind nginx or Caddy) + wire into compose

### Phase 2 — host metrics
- [ ] Hosts page: CPU / memory / disk charts per agent
- [ ] Time-range picker shared across charts

### Phase 4 — intelligence
- [ ] Anomaly markers overlaid on metric charts
- [ ] Incident summary panel rendering the AI write-up

### Later / stretch
- [ ] Dark mode (it's a monitoring tool — people will ask)
- [ ] Status page theming/branding options

## Definition of done (Phase 1)

Kill a monitored service and watch its tile flip red in the open browser tab without a refresh; the public status page is presentable enough to screenshot in the root README.
