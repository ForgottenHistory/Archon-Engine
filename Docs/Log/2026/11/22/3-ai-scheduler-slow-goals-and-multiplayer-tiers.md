# AI Scheduler: Slow-Goal Threshold and Multiplayer Tiers
**Date**: 2026-11-22
**Session**: 3
**Status**: ✅ Complete (compiles; no game uses the scheduler yet)
**Priority**: Medium

---

## Problems Encountered & Solutions

### Problem 1: "Execution timeout" that never times anything out
**Symptom:** The scheduler's docs and API (`SetExecutionTimeout`, "timeout protection",
`AIStatistics.TotalTimeouts`) read as if a goal is cut short after 50 ms. That would make AI decisions
depend on wall-clock time (machine speed), i.e. non-deterministic. A review concluded exactly that.

**Root Cause:** Naming only. `ProcessSingleAI` runs the selected goal to completion and only afterwards
compares the stopwatch with the threshold, to log a warning and count it. Decisions never depended on
wall time.

**Solution:** Renamed to what it is, with no compatibility aliases (the engine is unreleased):
- `AIScheduler.SetSlowGoalThreshold(ms)` / `AISystem.SetSlowGoalThreshold(ms)` (were `SetExecutionTimeout`)
- `AIStatistics.TotalSlowGoals` (was `TotalTimeouts`), `RecordProcessing(..., bool slow)`
- The warning now says how long the goal took and the threshold.
- Docs state it plainly: goals always run to completion; wall time never changes a decision.

### Problem 2: Tiers measured from the host's country only
**Symptom:** (design gap, found in review) `InitializeCountryAI` took one `playerCountryID`; the BFS for
tier distances started from that country's provinces only. In multiplayer, an AI country next to a
non-host player could land in a far tier and be processed every 72 hours.

**Solution:**
- `InitializeCountryAI(countryCount, provinceCount, IReadOnlyList<ushort> humanCountryIDs, hour)`:
  every human country.
- `AIDistanceCalculator.CalculateDistances(IReadOnlyList<ushort> humanCountries, ...)`: one multi-source
  BFS from all their provinces; each human country has distance 0.
- `AISystem.SetHumanCountries(...)` when players join, leave or switch: recalculates tiers. Enabling a
  country's own AI stays the GAME's call (`SetAIEnabled`).
- No human countries: every distance stays at the maximum (all "very far"), as before for a player with
  no provinces.

---

## Decisions

### Determinism of the AI
The AI runs on the host only and its decisions reach clients as commands, so clients and replays stay
in sync even if the host's decisions were not reproducible. The scheduler is still deterministic (tiers
from a BFS, staggering by hour of year, goal scores in FixedPoint64), which keeps a game reproducible for
debugging. Keep it that way: never let wall time decide anything.

---

## Quick Reference for Future Claude

- `Scripts/Core/AI/AIScheduler.cs`: `slowGoalThresholdTicks`, `SetSlowGoalThreshold`, `ProcessSingleAI`
- `Scripts/Core/AI/AIStatistics.cs`: `TotalSlowGoals`
- `Scripts/Core/AI/AISystem.cs`: `InitializeCountryAI(..., humanCountryIDs, ...)`, `SetHumanCountries`, `HumanCountries`
- `Scripts/Core/AI/AIDistanceCalculator.cs`: multi-source `CalculateDistances`, `BurstBFSDistanceJob.humanCountries`
