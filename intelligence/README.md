# intelligence — Python

The layer that explains things. Watches metric streams for anomalies and writes plain-English incident summaries using an LLM. This service is the project's AI signal — the part that separates it from every other status-page clone.

## Why Python for this part

Anomaly detection lives in Python's home territory: pandas/polars for windowed metric analysis, numpy for the statistics, scikit-learn waiting if the statistical approach ever needs upgrading. The LLM side is the same story — the Anthropic SDK and the whole prompt-engineering ecosystem are Python-first. FastAPI keeps the service layer thin and typed. Any other language would be re-implementing this ecosystem; Python just imports it.

## Owns

- A scheduled **anomaly detection job**: read recent `metrics`, flag outliers, write to `anomalies`
- **Start statistical, not ML**: rolling z-score / EWMA per metric series. It's explainable, debuggable, and good enough — the upgrade path (isolation forest, Prophet) is documented, not pre-built
- `POST /summaries/incident/{id}` — gather the incident's timeline, check results, and nearby anomalies; ask Claude for a structured summary ("what happened, probable cause, suggested checks"); return it for the control-plane to store
- Prompt templates, model choice, and token-cost notes (documented in this README as they evolve)

## Doesn't own

- Alerting decisions — it *annotates*; the control-plane *decides*
- The schema (`anomalies` / `insights` tables come from control-plane migrations)

## Talks to

- **Postgres** — reads `metrics`, `incidents`, `check_results`; writes `anomalies`
- **control-plane** — serves it on port `8000`
- **Anthropic API** — outbound

## Suggested stack

Python 3.12+ · `uv` for dependency management · FastAPI · polars (or pandas) · `anthropic` SDK · APScheduler for the detection loop · pytest

## TODO (starts in Phase 4)

- [ ] Scaffold with `uv`: `pyproject.toml`, `src/` layout, settings via pydantic-settings
- [ ] DB reader: windowed metric queries per host/series
- [ ] Baseline detector: rolling z-score with configurable window + threshold
- [ ] Writer: upsert `anomalies` with severity + the values that triggered it
- [ ] Detection loop on APScheduler (e.g. every 60s)
- [ ] `GET /healthz` + `POST /summaries/incident/{id}` in FastAPI
- [ ] Summary pipeline: assemble incident context → prompt Claude → validate structured output → return
- [ ] Prompt iteration notes: keep a `prompts/` folder with versions and what changed
- [ ] Tune false positives on real data from your own Pi/servers; document threshold choices
- [ ] Tests: detector unit tests with synthetic series (flat, spike, slow drift)
- [ ] Dockerfile + wire into compose (Anthropic key via env)

## Definition of done (Phase 4)

Spike the CPU on a monitored host: an anomaly row appears, the dashboard shows the marker, and closing the incident produces a summary that correctly names the host, the metric, and the time window.
