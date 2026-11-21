# Session Recording

**Status:** Implemented (`Core.Recording`)
**Layer:** ENGINE mechanism; the GAME decides whether, when and what to record

## Purpose

When a simulation misbehaves (units that won't move, AI that throws divisions away, a front that drifts), watching the game shows that something is wrong, but rarely why. A session recording writes what happened, hour by hour, to plain tab-separated files. A person, a script or an AI assistant can then read the files afterwards and follow one unit, one decision or one battle through time.

The engine provides the machinery only. Nothing records unless the game starts a recording. The game picks the moment (always in the editor, behind a debug toggle, on a key), the folder and the tables.

## Layout

```
<Root>/<Name>/
  meta.json        start date and tick, wall-clock time, plus RecordingOptions.Meta
  countries.tsv    id -> tag
  commands.tsv     built in: every executed command and its public fields and properties
  log.tsv          built in: Unity warnings, errors and exceptions
  notes.tsv        built in: SessionRecorder.Note, decisions and why
  <table>.tsv      one per table that a source declares
```

- **Time tables** start every row with `tick` (`TimeManager.CurrentTick`, game hours) and `date` (`YYYY-MM-DD HH`).
- **Lookup tables** (`timed: false`) don't have those two columns.
- **Free text** goes through `SessionRecorder.Clean`, which takes out tabs and line breaks. Id lists are comma-joined with `SessionRecorder.Join`.
- **Folder names** should sort by time (start them with a timestamp). `Keep` deletes the oldest folders beyond that count.

## API

```csharp
var options = new RecordingOptions { Root = ".../Recordings", Name = $"{DateTime.Now:yyyyMMdd_HHmmss}_{tag}" };
options.Meta["player"] = tag;
var rec = SessionRecorder.Start(gameState, options);     // null if the folder can't be made
rec.Add(new ProvinceControlSource(p => !IsWater(p)));     // built-in source, optional
rec.Add(new MyUnitsSource());                             // the game's own IRecordingSource

// At the end of the game's tick, after every system has run:
rec.Record();

// Anywhere, in engine or game code (a no-op when not recording):
SessionRecorder.Note("ai", "attack", country, unit, province, target, $"odds {odds:0.00}");

rec.Stop();                                               // or Dispose; also flushes
```

A source implements `IRecordingSource`:

- `Begin(recorder)` declares tables with `recorder.Table(name, columns)` and may write the starting state.
- `Record(recorder)` writes rows. A common pattern is change-only rows: keep the last key per entity and write a row only when it differs, plus a periodic full snapshot.

## Design notes

- **Read only.** Sources and notes must never change state. A recording therefore can't affect determinism, and a recorded run plays out exactly like an unrecorded one.
- **The game calls `Record()`.** The engine can't know when a game's own tick is complete, because game systems often run in their own fixed order after the engine's hourly tick. Recording from inside the engine tick would see half-updated state.
- **Commands are recorded synchronously.** This uses `CommandProcessor.CommandExecuted`, not the queued `CommandExecutedEvent`. A command is then stamped with the tick that ran it, and its data is available.
- **Cost.** Writes are buffered and flushed once per game day and on `Stop`. `SessionRecorder.IsRecording` lets callers skip building note text when nothing records.
- **Failures are contained.** An exception in a source is logged and that source skips the tick. Recording keeps going.

## Reading recordings

Python helpers live in `Tools~/recording/` (the `~` keeps Unity from importing them):

- `rec.py` holds shared loading. It finds recordings under `ARCHON_RECORDINGS`, or `./Recordings` when that isn't set.
- `rows.py [REC] TABLE col=value ... [--cols a,b] [--count COL] [--schema]` filters a table by column name, so a value never matches in the wrong column. `rows.py --tables` lists the tables.

Games add their own analysis scripts on top of `rec.py`.
