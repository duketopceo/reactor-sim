# Observability and debugging playbook

> Pragmatic, not magical: there is no universal "highest standard" of logging. This is a
> design that makes simulation bugs **reproducible, explainable and cheap to report**. It is a
> proposal; nothing here is implemented on master yet except the canonical state hash used by
> `--verify` ([verification](../testing/simulation-verification.md)).
> Educational entertainment: these logs are developer tools, not regulatory records.

Companions: [assets](../design/plant-and-assets.md) ·
[architecture](../ARCHITECTURE.md).

## 1. Principles

1. **The scientific state journal is the source of truth.** Metrics/traces/log lines are views.
   [OpenTelemetry](https://opentelemetry.io/docs/specs/otel/logs/data-model/) *(link not re-verified)* correlation is an *integration
   recommendation* for exporting these views (trace/span IDs on events, severity mapping) — it does **not** replace the deterministic journal.
2. **Core stays dependency-free.** `ReactorSim.Core` (netstandard2.1) defines a tiny `ISimEventSink`; adapters (file, ring buffer, OTel exporter, Unity console) live outside.
3. **Deterministic journal ≠ sampled diagnostics.** The journal (inputs, discrete events, state transitions) is byte-reproducible for a same-build replay and is hashed. Diagnostics (per-step solver stats, timing, metrics, traces) are sampled, may drop under load, and are excluded from hashes.
4. **Player alarms ≠ implementation defects.** Different ID namespaces and channels (§6).
5. **Never silently clamp.** A clamp that changes a value emits a `model.*` event with before/after/reason.

## 2. Event model

Stable, never-reused IDs, `<domain>.<name>.v<N>`:

| Namespace | Examples | Journal? |
|---|---|---|
| `cmd.*` | `cmd.received`, `cmd.applied`, `cmd.rejected` | yes |
| `act.*` | `act.target_changed`, `act.limit_reached`, `act.stuck` | yes |
| `proc.*` | `proc.trip.set`, `proc.trip.reset`, `proc.mode.changed` | yes |
| `alarm.*` (player-facing) | `alarm.raised`, `alarm.acked`, `alarm.cleared`, `alarm.firstout` | yes |
| `model.*` (developer faults) | `model.domain_violation`, `model.nonfinite`, `model.clamped`, `model.solver_failed`, `model.invariant_failed` | yes |
| `diag.*` (sampled) | `diag.step`, `diag.controller`, `diag.energy_balance`, `diag.timing` | no |
| `sys.*` | `sys.run_started`, `sys.snapshot_taken`, `sys.log_dropped` | yes |

Severity (maps to OTel `SeverityNumber`): `TRACE`1–4 · `DEBUG`5–8 · `INFO`9–12 · `WARN`13–16 · `ERROR`17–20 · `FATAL`21–24.

### 2.1 JSON line schema (v1, one object per line, UTF-8)

```json
{
  "schema": "rs.event/1",
  "event_id": "model.clamped.v1",
  "severity": "WARN",
  "run": {"id": "…uuid…", "build_hash": "…", "config_hash": "…", "asset_hash": "…", "seed": 12345},
  "sim": {"tick": 1830, "time_s": 10980.0, "dt_s": 6.0},
  "utc": "2026-11-07T08:41:20.181Z",
  "component": "plant.primary.coolant",
  "source": "Thermal.Step",
  "causality": {"cause_id": 1829017, "correlation_id": "cmd-7f3a", "event_seq": 1829018},
  "change": {"field": "Thermal.PrimaryTempC", "before": 362.4, "after": 360.0, "reason": "clamp_max"},
  "solver": {"method": "explicit_euler", "substeps": 1, "iterations": 0, "residual": 0.0, "rejected_steps": 0, "domain_flags": ["T_above_clamp"]},
  "controller": {"setpoint": null, "error": null, "p": null, "i": null, "d": null, "output": null, "saturated": null},
  "accounting": {"power_fission_kw": 0.0, "power_decay_kw": 0.0, "q_sink_kw": 0.0, "flow_kg_s": 0.0, "d_storage_kj": 0.0, "residual_kj": 0.0},
  "alarm": {"first_out": false, "acked_by": null},
  "command": {"origin": "player|assistant|script|replay", "actor": "p1", "intent_id": "…"},
  "queue": {"depth": 0, "dropped_total": 0}
}
```

* Sim time is authoritative (`tick`, `time_s`); `utc` is for humans only and **excluded from journal hashes**.
* `schema` versions are additive within a major; a reader rejects unknown majors.
* Build/config/asset hashes pin *what* produced the run; `seed` is the master seed (per-stream seeds derive from it).
* Fields not applicable are omitted in the journal form; the schema above shows the superset.
* Units: SI suffixes in field names (`_s`, `_kw`, `_kj`, `_kg_s`) except documented game units (`pcm`, `frac`).

## 3. Levels, filtering, retention

* Per-category filters (`diag.controller=DEBUG, diag.*=INFO`) configurable at runtime; journal categories are never filtered out.
* **Bounded ring buffer** for `TRACE/DEBUG/diag`: N most recent events; on a trigger (any `model.*` ≥ WARN, an `alarm.raised` of chosen class, a trip, a hash mismatch) freeze the pre-trigger window and keep capturing a post-trigger window, then flush as the "pre/post-fault capture". Sizes are tuning knobs measured by the overhead test (§9), not fixed here.
* **Backpressure:** producers never block the tick. If a sink is slow: drop `diag` first, then count; **never drop journal events** (spill to a bounded on-disk queue; if it fills, fail loud with `sys.log_dropped` and stop the run in dev builds). `queue.depth` and `dropped_total` are always reported.
* **Rotation/retention:** size-based rotation of journal and diagnostic files, N most recent runs kept; configurable and offline-first (plain files, no network).
* **Redaction:** no credentials, tokens, user paths, machine names or private-repo data in events; player names are replaced by stable pseudonyms in bundles.

## 4. Journal vs diagnostics vs metrics vs traces

| | Content | Deterministic | Sampled | Use |
|---|---|---|---|---|
| Event journal | commands, discrete events, transitions, faults | yes | no | replay, audit, diffing |
| Sampled diagnostics | per-step solver stats, controller terms, energy residuals | no (hash-excluded) | yes | tuning, debugging |
| Metrics | counters/gauges (tick time, queue depth) | no | yes | performance |
| Traces | spans per tick phase / command | no | yes | latency, causality across Unity ↔ sim ↔ assistant |

OTel mapping: `event_id` → `EventName`; `sim`/`component`/`run` → attributes; `correlation_id` → `TraceId`/`SpanId` when a trace exists; `utc` → `Timestamp`, receive time → `ObservedTimestamp`.

## 5. What to emit per tick (diagnostics)

Solver: method, sub-steps, iterations, residual, rejected steps, domain flags. Controllers: setpoint, error, P/I/D terms, output, saturation flag. Accounting: fission power, decay power, flows, `ΔStorage − ∫(sources − sinks)` residual with named boundaries. Alarms: first-out and acknowledgement. Today's equivalents are the `Plant.Telemetry` row and `AssertFinite`; the journal supersedes those, they do not disappear.

## 6. Player alarms vs developer faults

| | Operator alarm | Developer fault |
|---|---|---|
| Meaning | the *plant* is out of its normal band | the *model/tool* is out of its validity |
| Example | high pressure, trip | `model.nonfinite`, `model.domain_violation` (KD-02) |
| Audience | player; first-out board | developer/CI; overlay, bundle |
| Determinism | part of game state | part of the journal; in release builds becomes a controlled "model limit" game event |
| Never | shown as a developer message | shown as a gameplay alarm |

## 7. Developer overlays

* **Truth vs indicated:** side-by-side process value and instrument reading, colour-coded delta (needs the sensor layer; until then equal).
* **Residuals:** per-subsystem mass/energy residual bars with named boundaries; red when above tolerance.
* **Command trace:** select a command → follow `cmd.received → cmd.applied → act.* → process change → alarm`, via `correlation_id`/`cause_id`, with tick offsets.
* **Hash ticker:** current state hash per tick; mismatch against a reference run highlights the first divergent field path (as `--verify` already reports).

## 8. One-click reproducible bug bundle

A zip written locally (never uploaded automatically), containing:

1. initial state snapshot and the latest snapshot before the fault;
2. **input journal** (ordered, tick-stamped commands, RNG stream states when they exist);
3. exact configuration and coefficient set **with provenance** (source, date, "physical vs game-tuned");
4. runtime info (build hash, .NET/Unity version, OS/CPU, `Physics.PoisonTimeWarp` and other process-wide state);
5. the pre/post-fault ring capture and the event journal;
6. final canonical hash and the command line to replay (`ReactorSim.Runner --replay bundle.zip`, *to be built*);
7. **no** credentials, tokens, user directories or private repo data (checked by a redaction pass).

Acceptance: `replay(bundle)` reproduces the final hash on the same build (cross-platform: tolerance comparison only).

## 9. Overhead and verification of the logging itself

* Overhead gate: tick time with logging at `INFO` vs off, measured on a pinned machine; budget set after the first measurement (none invented here).
* Ring buffer: bounded memory test; no allocation per event on the hot path (pooled buffers).
* Determinism: toggling log level or sinks must not change the state hash (test: DET-01 with sinks on/off).
* Drop accounting: inject a slow sink; assert diagnostics drop, journal does not, counters are right.

## 10. Sensitivity and tuning workflow

1. Pick a parameter from `Physics.cs`/data file with its allowed range and **kind** (physical vs game tuning).
2. Sweep within the constrained range via the Runner (one-factor-at-a-time, then a small design).
3. Record metrics: equilibrium, overshoot, settle time, trip margin, xenon-pit duration, plus conservation residuals.
4. Gate: the benchmark set (`--verify` + `--scenario all`) must pass, and tuned values need a note in the config provenance.
5. Physical coefficients change only with a cited source; gameplay tuning never rewrites a physical constant.
