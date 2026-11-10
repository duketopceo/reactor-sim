# Ops Assistant

The assistant is an operational advisor, not a scripted chatterbox of canned lines.
Design target: **a senior reactor operator standing at
your shoulder, not a popup engine**.

## What makes it not suck

1. **Reads the real state.** Subscribes to the same ledger the instruments
   show — reactivity components, margins, trends — not scripted triggers.
   It can say *why*: "Xenon is building because you held 100% through a
   transient; expect a pit for the next ~40 minutes."
2. **Procedures, not tips.** A procedure engine: ordered steps with
   prerequisites and exit conditions evaluated against live state
   ("Startup", "Recover from xenon pit", "Loss of secondary sink"). It knows
   where you are in a procedure and only speaks when the next step matters.
3. **Alarm explanation.** Every alarm maps to: what tripped, current value vs
   limit, trend direction, the one next action that helps. Quiet unless a
   limit is actually approached.
4. **Asks before acting.** Operator-assist tiers: advisory (suggest) ->
   supervised (executes with confirmation) -> autopilot (selected procedures
   only). Multiplayer: each player configures their own assistant's tier.
5. **Explainability.** Every recommendation carries its evidence:
   "rho = +210 pcm, mostly rod withdrawal; Doppler will eat ~150 pcm as fuel
   heats" — the assistant teaches the physics instead of hiding it.

## Where procedures live now

Procedure data is `ReactorSim.Unity/Assets/StreamingAssets/procedures.json`; the engine that loads and evaluates
it against live state is in Core `Procedures/` (`Procedure.cs`, `ProcedureLoader.cs`). The first full path is
`cold-start-to-grid` (see [STARTUP.md](STARTUP.md)). The sketch below is older: it was not built as written.
There is no `Assistant/ProcedureEngine.cs` DAG, `Adviser.cs` or `Explainer.cs`; procedures are an ordered list
with prefix semantics (a step unticks everything after it), and advisories live in `Assistant/AssistantEngine.cs`.
Still not built: prerequisites beyond order, supervised and autopilot execution of a procedure, the free-form
LLM front-end.

## Implementation sketch (original, not built as written)

- `Assistant/ProcedureEngine.cs` — DAG of steps, conditions over `Plant` state
- `Assistant/Adviser.cs` — rule layer producing ranked advisories w/ evidence
- `Assistant/Explainer.cs` — renders advisory -> player-facing text/voice
- Optional later: LLM front-end over the advisory layer for free-form Q&A
  (the engine stays authoritative — the LLM narrates, never invents state)

## Source data

Procedure content comes from real operator knowledge (startup sequences,
SCRAM recovery, xenon transient management — all standard PWR doctrine) and
from the simulator's own play flows. All text is original.
