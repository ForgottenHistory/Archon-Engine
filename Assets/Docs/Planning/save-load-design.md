# Grand Strategy Game - Save/Load & Replay Architecture

**📊 Implementation Status:** ⚠️ Implementation Unknown (Command system ✅, save/load system status unclear)

> **📚 Architecture Context:** This document builds on the command pattern. See [master-architecture-document.md](master-architecture-document.md) and [data-flow-architecture.md](data-flow-architecture.md) for command system details.

## Executive Summary
**Key Insight**: Command pattern + deterministic simulation = trivial save system  
**Save Types**: Full snapshot (5MB) or Initial state + commands (500KB)  
**Performance**: <100ms to save, <1s to load, instant replay  
**Features**: Save anywhere, replay battles, multiplayer resync, debug time travel

## The Foundation: Why Commands Make This Easy

### Traditional Save System (Painful)
```csharp
// Without commands - need to save EVERYTHING
public class TraditionalSave {
    public void SaveGame() {
        // Must carefully save every bit of state
        SaveProvinces();      // Who owns what
        SaveArmies();         // Where they are
        SaveDiplomacy();      // All relationships
        SaveEconomy();        // All treasury, trade
        SaveBuildings();      // What's being built
        SaveModifiers();      // All active effects
        SaveAI();            // AI goals and state
        // ... 100 more things to track
        
        // Miss one thing = corrupted save
        // Add new feature = break save compatibility
    }
}
```

### Command-Based Save System (Beautiful)
```csharp
// With commands - just save initial state + commands
public class CommandBasedSave {
    public void SaveGame() {
        // Option 1: Save current snapshot
        SaveCurrentState();  // Clean, versioned state
        
        // Option 2: Save initial + all commands
        SaveInitialState();  // Starting scenario
        SaveCommandHistory(); // Every command executed
        
        // Can perfectly recreate game by replaying commands
    }
}
```

## Save Architecture Overview

### Three Types of Saves

```
1. SNAPSHOT SAVE (Traditional, 5-10MB)
   ├── Complete game state at moment
   ├── Can load instantly
   └── Larger file size

2. COMMAND SAVE (Compact, 100KB-1MB)
   ├── Initial scenario reference
   ├── All commands executed
   ├── Must replay to load
   └── Tiny file size

3. HYBRID SAVE (Best of both, 2-5MB)
   ├── Periodic snapshots (every 50 years)
   ├── Commands since last snapshot
   ├── Fast load with small size
   └── Best for long games
```

## Command Pattern Implementation

> **See:** [data-flow-architecture.md](data-flow-architecture.md) for complete command pattern implementation.

Commands form the foundation of the save system:
- Every game state change is a command
- Commands are serializable and deterministic
- Replaying commands recreates exact game state

## Snapshot Save System

### State Serialization
```csharp
public class SnapshotSave {
    private const uint SAVE_VERSION = 1;
    private const uint MAGIC_NUMBER = 0x47535345; // "GSSE"
    
    public void SaveSnapshot(string filename) {
        using (var file = File.Create(filename))
        using (var compress = new GZipStream(file, CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(compress)) {
            // Header
            writer.Write(MAGIC_NUMBER);
            writer.Write(SAVE_VERSION);
            writer.Write(DateTime.Now.Ticks);
            writer.Write(TimeManager.CurrentDate);
            
            // Game info
            writer.Write(ScenarioID);
            writer.Write(GameRules.Checksum);
            
            // State data
            SaveProvinces(writer);
            SaveNations(writer);
            SaveDiplomacy(writer);
            SaveEconomy(writer);
            SaveMilitary(writer);
            SaveAI(writer);
            
            // Checksums for validation
            writer.Write(CalculateChecksum());
        }
    }
    
    private void SaveProvinces(BinaryWriter writer) {
        writer.Write(ProvinceCount);
        
        // Structure of arrays for compression
        // All owners together compress better than mixed data
        for (int i = 0; i < ProvinceCount; i++) {
            writer.Write(provinceOwners[i]);
        }
        for (int i = 0; i < ProvinceCount; i++) {
            writer.Write(provinceControllers[i]);
        }
        for (int i = 0; i < ProvinceCount; i++) {
            writer.Write(provinceDevelopment[i]);
        }
        // ... other province data
    }
}
```

### Versioning & Compatibility
```csharp
public class SaveVersioning {
    public interface ISaveUpgrader {
        uint FromVersion { get; }
        uint ToVersion { get; }
        void Upgrade(SaveData data);
    }
    
    // Upgrade from version 1 to 2
    public class SaveUpgrader_1_to_2 : ISaveUpgrader {
        public uint FromVersion => 1;
        public uint ToVersion => 2;
        
        public void Upgrade(SaveData data) {
            // Version 2 added new building types
            foreach (var province in data.provinces) {
                if (province.buildings == null) {
                    province.buildings = new byte[NEW_BUILDING_COUNT];
                }
            }
        }
    }
    
    public SaveData LoadSave(string filename) {
        var data = ReadSaveFile(filename);
        
        // Upgrade through each version
        while (data.version < CURRENT_VERSION) {
            var upgrader = GetUpgrader(data.version);
            upgrader.Upgrade(data);
            data.version = upgrader.ToVersion;
        }
        
        return data;
    }
}
```

## Command-Based Save System

### Minimal Save Files
```csharp
public class CommandSave {
    public void SaveCommands(string filename) {
        using (var file = File.Create(filename))
        using (var compress = new GZipStream(file, CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(compress)) {
            // Header (tiny)
            writer.Write(MAGIC_NUMBER);
            writer.Write(SAVE_VERSION);
            writer.Write(ScenarioID);  // Which starting scenario
            writer.Write(GameRules.GetHash());  // Settings/mods
            
            // Command count
            writer.Write(commandHistory.Count);
            
            // Commands (highly compressible)
            foreach (var command in commandHistory) {
                command.Serialize(writer);
            }
        }
        // Result: 100KB-1MB even for long games
    }
    
    public void LoadCommands(string filename) {
        using (var file = File.OpenRead(filename))
        using (var compress = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new BinaryReader(compress)) {
            // Read header
            var magic = reader.ReadUInt32();
            var version = reader.ReadUInt32();
            var scenario = reader.ReadString();
            
            // Load scenario
            LoadScenario(scenario);
            
            // Read and replay all commands
            var commandCount = reader.ReadInt32();
            
            for (int i = 0; i < commandCount; i++) {
                var command = DeserializeCommand(reader);
                
                // Fast forward time to command timestamp
                TimeManager.AdvanceTo(command.tick);
                
                // Execute command
                command.Execute(GameState.Current);
                
                // Update progress bar
                if (i % 100 == 0) {
                    UpdateLoadingProgress(i / (float)commandCount);
                }
            }
        }
    }
}
```

### Fast Replay with Checkpoints
```csharp
public class HybridSave {
    // Checkpoint every N commands or X game-years
    private const int CHECKPOINT_INTERVAL = 1000;
    private const int CHECKPOINT_YEARS = 50;
    
    public void SaveHybrid(string filename) {
        using (var writer = new BinaryWriter(File.Create(filename))) {
            // Save checkpoints
            writer.Write(checkpoints.Count);
            foreach (var checkpoint in checkpoints) {
                writer.Write(checkpoint.Key);  // Command index
                checkpoint.Value.Serialize(writer);  // State snapshot
            }
            
            // Save commands after last checkpoint
            var lastCheckpoint = checkpoints.Keys.Max();
            var remainingCommands = commandHistory.Skip(lastCheckpoint);
            
            writer.Write(remainingCommands.Count());
            foreach (var command in remainingCommands) {
                command.Serialize(writer);
            }
        }
    }
    
    public void LoadHybrid(string filename) {
        using (var reader = new BinaryReader(File.OpenRead(filename))) {
            // Load checkpoints
            var checkpointCount = reader.ReadInt32();
            for (int i = 0; i < checkpointCount; i++) {
                var index = reader.ReadInt32();
                var snapshot = GameStateSnapshot.Deserialize(reader);
                checkpoints[index] = snapshot;
            }
            
            // Find nearest checkpoint
            var targetCommand = reader.ReadInt32();
            var nearestCheckpoint = checkpoints.Keys
                .Where(k => k <= targetCommand)
                .Max();
            
            // Restore from checkpoint
            GameState.RestoreFromSnapshot(checkpoints[nearestCheckpoint]);
            
            // Replay remaining commands
            var remainingCount = reader.ReadInt32();
            for (int i = 0; i < remainingCount; i++) {
                var command = DeserializeCommand(reader);
                command.Execute(GameState.Current);
            }
        }
    }
}
```

## Autosave System

### Non-Blocking Autosaves
```csharp
public class AutosaveManager {
    private Thread autosaveThread;
    private GameStateSnapshot pendingSnapshot;
    private readonly object snapshotLock = new object();
    
    public void Initialize() {
        // Autosave every 5 minutes of real time
        Timer.Every(300, TriggerAutosave);
        
        // Also autosave on important events
        EventBus.Subscribe<WarDeclaredEvent>(_ => TriggerAutosave());
        EventBus.Subscribe<RulerDiedEvent>(_ => TriggerAutosave());
    }
    
    private void TriggerAutosave() {
        // Create snapshot on main thread (fast)
        lock (snapshotLock) {
            pendingSnapshot = GameState.Current.CreateSnapshot();
        }
        
        // Save on background thread (slow)
        if (autosaveThread == null || !autosaveThread.IsAlive) {
            autosaveThread = new Thread(SaveInBackground) {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            autosaveThread.Start();
        }
    }
    
    private void SaveInBackground() {
        GameStateSnapshot snapshot;
        lock (snapshotLock) {
            snapshot = pendingSnapshot;
        }
        
        // Rotate autosaves
        RotateAutosaves();
        
        // Save to disk (doesn't block game)
        snapshot.SaveToDisk("autosave_1.sav");
    }
    
    private void RotateAutosaves() {
        // Keep last 3 autosaves
        if (File.Exists("autosave_3.sav")) 
            File.Delete("autosave_3.sav");
        if (File.Exists("autosave_2.sav")) 
            File.Move("autosave_2.sav", "autosave_3.sav");
        if (File.Exists("autosave_1.sav")) 
            File.Move("autosave_1.sav", "autosave_2.sav");
    }
}
```

## Replay System

### Battle Replay
```csharp
public class BattleReplay {
    private List<GameCommand> battleCommands = new();
    private GameStateSnapshot preWareState;
    
    public void StartRecording(WarDeclaredEvent war) {
        // Snapshot state before war
        preWareState = GameState.Current.CreateSnapshot();
        battleCommands.Clear();
        
        // Start recording all commands
        CommandHistory.OnCommand += RecordCommand;
    }
    
    private void RecordCommand(GameCommand cmd) {
        // Only record military commands
        if (cmd is MoveArmyCommand || 
            cmd is EngageBattleCommand ||
            cmd is SiegeProvinceCommand) {
            battleCommands.Add(cmd);
        }
    }
    
    public void PlayReplay(float speed = 1.0f) {
        // Restore pre-war state
        GameState.RestoreFromSnapshot(preWareState);
        
        // Replay commands at specified speed
        foreach (var command in battleCommands) {
            // Wait for command time
            while (TimeManager.GameTime < command.gameTime * speed) {
                TimeManager.Tick(Time.DeltaTime * speed);
                Renderer.RenderFrame();  // Show replay
            }
            
            command.Execute(GameState.Current);
        }
    }
}
```

### Debug Time Travel
```csharp
public class TimeTravelDebugger {
    private CircularBuffer<GameStateSnapshot> history = new(100);
    
    #if DEBUG
    public void Update() {
        // Save snapshot every second for debugging
        if (Time.frameCount % 60 == 0) {
            history.Add(GameState.Current.CreateSnapshot());
        }
        
        // Alt+Left = go back in time
        if (Input.GetKey(KeyCode.Alt) && Input.GetKeyDown(KeyCode.Left)) {
            TimeTravel(-1);
        }
        
        // Alt+Right = go forward in time
        if (Input.GetKey(KeyCode.Alt) && Input.GetKeyDown(KeyCode.Right)) {
            TimeTravel(1);
        }
    }
    
    private void TimeTravel(int direction) {
        var snapshot = history.GetRelative(direction);
        if (snapshot != null) {
            GameState.RestoreFromSnapshot(snapshot);
            DominionLogger.Log($"Time traveled to {TimeManager.CurrentDate}");
        }
    }
    #endif
}
```

## Multiplayer Synchronization

### Using Commands for Multiplayer
```csharp
public class MultiplayerSync {
    // Commands enable perfect synchronization
    public class NetworkedCommand {
        public GameCommand command;
        public uint executionTick;  // When to execute
        public byte issuingPlayer;
        public uint checksum;       // Validate sync
    }
    
    public void BroadcastCommand(GameCommand cmd) {
        var networked = new NetworkedCommand {
            command = cmd,
            executionTick = TimeManager.CurrentTick + NETWORK_DELAY,
            issuingPlayer = LocalPlayer.Id,
            checksum = GameState.Current.CalculateChecksum()
        };
        
        Network.SendToAll(networked);
        
        // Queue for local execution at same tick
        QueueForExecution(networked);
    }
    
    public void OnReceiveCommand(NetworkedCommand netCmd) {
        // Validate checksum
        if (netCmd.checksum != GameState.Current.CalculateChecksum()) {
            // Desync detected! Request resync
            RequestResync(netCmd.issuingPlayer);
            return;
        }
        
        // Queue for execution at specified tick
        QueueForExecution(netCmd);
    }
    
    private void QueueForExecution(NetworkedCommand netCmd) {
        commandQueue.Add(netCmd.executionTick, netCmd.command);
    }
    
    // Resync from host's save
    private void RequestResync(byte fromPlayer) {
        if (IsHost) {
            // Host sends snapshot to desynced player
            var snapshot = GameState.Current.CreateSnapshot();
            Network.SendTo(fromPlayer, snapshot);
        }
    }
}
```

## Save File Optimization

### Compression Strategies

| Data Type | Algorithm | Rationale |
|-----------|-----------|-----------|
| Province ownership | Run Length Encoding | Most provinces owned by same nation (contiguous regions) |
| Diplomatic matrix | Sparse encoding | Most nation pairs have no relation |
| Commands | Delta encoding | Commands often similar (adjacent provinces) |
| Float values | Quantization | Don't need full precision for game data |

**Example**: Run-length encode ownership as `(value, count)` pairs. Implementation in codebase.

### Save File Structure
```
SAVE FILE FORMAT (.sav)
├── Header (64 bytes)
│   ├── Magic Number (4 bytes)
│   ├── Version (4 bytes)
│   ├── Checksum (8 bytes)
│   ├── Timestamp (8 bytes)
│   ├── Game Date (8 bytes)
│   ├── Scenario ID (16 bytes)
│   └── Flags (16 bytes)
│
├── Metadata (Variable, ~1KB)
│   ├── Player Nation
│   ├── Difficulty
│   ├── Game Rules
│   └── Mod List
│
├── Game State (Variable, 1-5MB)
│   ├── Province Data (Run-Length Encoded)
│   ├── Nation Data (Compressed)
│   ├── Diplomatic Web (Sparse Matrix)
│   ├── Economic Data (Quantized)
│   └── Military Data (Delta Encoded)
│
└── Command History (Optional, 100KB-1MB)
    ├── Checkpoint Indices
    ├── Checkpoints (Compressed States)
    └── Recent Commands (Since Last Checkpoint)
```

## Performance Metrics

### Save/Load Times
```
SNAPSHOT SAVE:
- Create snapshot: 10ms (copying arrays)
- Compress: 50ms (GZip)
- Write to disk: 40ms (SSD)
- Total: ~100ms

SNAPSHOT LOAD:
- Read from disk: 30ms
- Decompress: 40ms
- Restore state: 20ms
- Total: ~90ms

COMMAND SAVE:
- Serialize commands: 20ms
- Compress: 30ms
- Write: 10ms
- Total: ~60ms

COMMAND LOAD:
- Read: 10ms
- Decompress: 20ms
- Replay 10,000 commands: 500ms
- Total: ~530ms

AUTOSAVE (Background):
- Snapshot creation: 10ms (blocks game)
- Compression + Write: 90ms (background thread)
- Player impact: 10ms stutter every 5 minutes
```

## Best Practices

1. **Always version your saves** - Add new data at end, never change order
2. **Validate checksums** - Detect corruption and desyncs
3. **Compress intelligently** - Different strategies for different data
4. **Autosave before risky operations** - Wars, major events
5. **Keep command history bounded** - Checkpoint periodically
6. **Test save compatibility** - Automated tests for each version
7. **Background thread for autosave** - Don't block the game
8. **Support cloud saves** - Small command saves are perfect for cloud

## Error Recovery

### Corrupted Save Handling
```csharp
public class SaveRecovery {
    public GameState TryLoadSave(string filename) {
        try {
            return LoadNormal(filename);
        }
        catch (ChecksumMismatchException) {
            DominionLogger.LogWarning("Checksum mismatch, attempting recovery");
            return LoadWithRecovery(filename);
        }
        catch (Exception e) {
            DominionLogger.LogError($"Save corrupted: {e.Message}");
            
            // Try to load autosaves
            foreach (var autosave in GetAutosaves()) {
                try {
                    DominionLogger.Log($"Attempting to load {autosave}");
                    return LoadNormal(autosave);
                }
                catch { continue; }
            }
            
            throw new Exception("No valid saves found");
        }
    }
    
    private GameState LoadWithRecovery(string filename) {
        // Load what we can, use defaults for corrupted data
        var state = new GameState();
        
        using (var reader = new BinaryReader(File.OpenRead(filename))) {
            try { LoadProvinces(reader, state); } 
            catch { UseDefaultProvinces(state); }
            
            try { LoadNations(reader, state); }
            catch { UseDefaultNations(state); }
            
            // ... etc
        }
        
        return state;
    }
}
```

## Summary

The command pattern makes save/load almost trivial:

1. **Every change is a command** → Perfect history of what happened
2. **Deterministic simulation** → Replaying commands recreates exact state
3. **Snapshots for speed** → Don't always need to replay everything
4. **Compression is easy** → Commands are small and similar
5. **Multiplayer for free** → Just broadcast commands
6. **Debugging paradise** → Can replay any bug report

The result is a save system that is:
- **Fast** (<100ms to save)
- **Small** (1-5MB files)
- **Robust** (versioning, recovery)
- **Feature-rich** (replay, time travel, multiplayer sync)

All because we built on the command pattern from the start!

---

## Related Documents

- **[Multiplayer Architecture](multiplayer-architecture-guide.md)** - Command pattern shared between save and multiplayer systems
- **[Error Recovery Architecture](error-recovery-architecture.md)** - Save corruption recovery strategies
- **[Master Architecture](master-architecture-document.md)** - Overview of deterministic simulation architecture