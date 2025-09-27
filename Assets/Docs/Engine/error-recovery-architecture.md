# Grand Strategy Game - Error Recovery Architecture

## Executive Summary
**Philosophy**: Errors are inevitable - plan for them, don't just hope they won't happen  
**Goal**: Never crash, always recover, maintain player progress  
**Key Principle**: Fail gracefully, log everything, auto-recover when possible  
**Result**: Players never lose progress, developers get actionable bug reports

## Error Categories & Severity Levels

### Error Severity Classification
```csharp
public enum ErrorSeverity {
    Trivial = 0,    // Log and continue (missing tooltip)
    Minor = 1,      // Fix and continue (invalid color)
    Major = 2,      // Recover with fallback (corrupted texture)
    Critical = 3,   // Need intervention (save corruption)
    Fatal = 4       // Cannot continue (out of memory)
}

public class ErrorClassification {
    // Non-critical errors - game continues
    public static readonly HashSet<Type> RecoverableErrors = new() {
        typeof(MissingLocalizationException),
        typeof(InvalidModifierException),
        typeof(TextureLoadException),
        typeof(AudioClipMissingException),
        typeof(InvalidScriptException)
    };
    
    // Critical but salvageable
    public static readonly HashSet<Type> CriticalErrors = new() {
        typeof(SaveCorruptedException),
        typeof(DesyncException),
        typeof(CommandValidationException)
    };
    
    // Unrecoverable - must restart
    public static readonly HashSet<Type> FatalErrors = new() {
        typeof(OutOfMemoryException),
        typeof(StackOverflowException),
        typeof(AccessViolationException)
    };
}
```

## Global Error Handler

### Top-Level Exception Handling
```csharp
public class GlobalErrorHandler : MonoBehaviour {
    private Queue<ErrorReport> errorQueue = new();
    private bool isRecovering = false;
    
    void Awake() {
        // Catch all unhandled exceptions
        Application.logMessageReceived += HandleLog;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleTaskException;
    }
    
    private void HandleLog(string logString, string stackTrace, LogType type) {
        if (type == LogType.Exception) {
            var error = new ErrorReport {
                message = logString,
                stackTrace = stackTrace,
                timestamp = DateTime.Now,
                gameTime = TimeManager.CurrentDate,
                severity = ClassifyError(logString)
            };
            
            errorQueue.Enqueue(error);
            
            // Attempt recovery based on severity
            if (error.severity >= ErrorSeverity.Major && !isRecovering) {
                StartCoroutine(AttemptRecovery(error));
            }
        }
    }
    
    private IEnumerator AttemptRecovery(ErrorReport error) {
        isRecovering = true;
        
        // Log error for debugging
        ErrorLogger.LogError(error);
        
        // Show user-friendly message
        UI.ShowErrorNotification(GetUserMessage(error));
        
        // Attempt recovery
        bool recovered = false;
        switch (error.severity) {
            case ErrorSeverity.Major:
                recovered = AttemptMajorRecovery(error);
                break;
            case ErrorSeverity.Critical:
                recovered = AttemptCriticalRecovery(error);
                break;
            case ErrorSeverity.Fatal:
                AttemptFatalRecovery(error);
                break;
        }
        
        if (recovered) {
            UI.ShowNotification("Error recovered successfully");
        }
        
        isRecovering = false;
        yield return null;
    }
}
```

## System-Specific Error Handling

### Province System Errors
```csharp
public class ProvinceErrorHandler {
    // Handle invalid province access
    public byte GetOwnerSafe(ushort provinceId) {
        if (provinceId >= ProvinceCount) {
            LogError($"Invalid province ID: {provinceId}");
            return 0;  // Unowned
        }
        
        byte owner = provinceOwners[provinceId];
        if (owner >= NationCount) {
            LogError($"Province {provinceId} has invalid owner {owner}");
            provinceOwners[provinceId] = 0;  // Fix corruption
            return 0;
        }
        
        return owner;
    }
    
    // Handle province data corruption
    public void ValidateProvinceData() {
        for (ushort i = 0; i < ProvinceCount; i++) {
            // Check ownership
            if (provinceOwners[i] >= NationCount) {
                LogWarning($"Province {i} has invalid owner, resetting");
                provinceOwners[i] = 0;
            }
            
            // Check development (shouldn't be negative or huge)
            if (provinceDevelopment[i] < 0 || provinceDevelopment[i] > 100) {
                LogWarning($"Province {i} has invalid development, resetting");
                provinceDevelopment[i] = 1;
            }
            
            // Check controller matches owner if not at war
            if (!IsAtWar(provinceOwners[i]) && 
                provinceControllers[i] != provinceOwners[i]) {
                provinceControllers[i] = provinceOwners[i];
            }
        }
    }
}
```

### Command Validation & Recovery
```csharp
public class CommandErrorHandler {
    // Validate command before execution
    public bool TryExecuteCommand(GameCommand command) {
        try {
            // Pre-validation
            if (!command.Validate(GameState.Current)) {
                LogInvalidCommand(command, "Failed validation");
                return false;
            }
            
            // Create recovery point
            var recoveryPoint = GameState.Current.CreateQuickSnapshot();
            
            // Execute with error handling
            command.Execute(GameState.Current);
            
            // Post-validation
            if (!ValidateGameState()) {
                // Command broke something, rollback
                GameState.RestoreFromSnapshot(recoveryPoint);
                LogInvalidCommand(command, "Broke game state");
                return false;
            }
            
            return true;
        }
        catch (Exception e) {
            LogError($"Command execution failed: {e.Message}");
            
            // Try to recover
            if (recoveryPoint != null) {
                GameState.RestoreFromSnapshot(recoveryPoint);
            }
            
            return false;
        }
    }
    
    private bool ValidateGameState() {
        // Quick sanity checks
        foreach (byte nation in ActiveNations) {
            if (GetTreasury(nation) < -1000000) return false;  // Absurd debt
            if (GetArmyCount(nation) > 10000) return false;    // Too many armies
            if (GetProvinceCount(nation) > ProvinceCount) return false;  // Impossible
        }
        
        return true;
    }
}
```

### AI Error Recovery
```csharp
public class AIErrorHandler {
    private Dictionary<byte, int> errorCounts = new();
    private const int MAX_ERRORS_PER_NATION = 10;
    
    public void ExecuteAIDecision(byte nation, AIDecision decision) {
        try {
            decision.Execute(nation);
            errorCounts[nation] = 0;  // Reset on success
        }
        catch (Exception e) {
            errorCounts[nation]++;
            
            LogError($"AI error for nation {nation}: {e.Message}");
            
            if (errorCounts[nation] >= MAX_ERRORS_PER_NATION) {
                // AI is broken, switch to safe mode
                DisableAI(nation);
                LogError($"AI for nation {nation} disabled due to repeated errors");
            }
            else {
                // Try fallback behavior
                ExecuteFallbackAI(nation);
            }
        }
    }
    
    private void ExecuteFallbackAI(byte nation) {
        // Super simple, safe AI behavior
        try {
            // Just maintain armies and build economy
            if (GetTreasury(nation) > 100) {
                var capital = GetCapital(nation);
                if (CanBuildBuilding(capital, BuildingType.Workshop)) {
                    BuildBuilding(capital, BuildingType.Workshop);
                }
            }
            
            // Move armies to capital if idle
            foreach (var army in GetArmies(nation)) {
                if (!army.HasOrders) {
                    army.MoveTo(GetCapital(nation));
                }
            }
        }
        catch {
            // Even fallback failed, just skip this nation
            LogError($"Even fallback AI failed for nation {nation}");
        }
    }
}
```

## Save System Error Recovery

### Corrupted Save Handling
```csharp
public class SaveErrorRecovery {
    public SaveGame LoadWithRecovery(string filepath) {
        SaveGame save = null;
        
        // Try normal load
        try {
            save = LoadNormal(filepath);
            ValidateSave(save);
            return save;
        }
        catch (Exception e) {
            LogError($"Normal load failed: {e.Message}");
        }
        
        // Try recovery strategies in order
        var strategies = new List<Func<string, SaveGame>> {
            LoadWithPartialRecovery,
            LoadPreviousAutosave,
            LoadFromBackup,
            CreateRecoverySave
        };
        
        foreach (var strategy in strategies) {
            try {
                save = strategy(filepath);
                if (save != null && ValidateSave(save)) {
                    UI.ShowWarning("Save was corrupted but recovered successfully");
                    return save;
                }
            }
            catch (Exception e) {
                LogError($"Recovery strategy failed: {e.Message}");
            }
        }
        
        // All recovery failed
        throw new UnrecoverableSaveException();
    }
    
    private SaveGame LoadWithPartialRecovery(string filepath) {
        var save = new SaveGame();
        
        using (var reader = new BinaryReader(File.OpenRead(filepath))) {
            // Try to load each section independently
            var sections = new List<(string name, Action<BinaryReader> loader)> {
                ("Header", r => save.header = LoadHeader(r)),
                ("Provinces", r => save.provinces = LoadProvinces(r)),
                ("Nations", r => save.nations = LoadNations(r)),
                ("Diplomacy", r => save.diplomacy = LoadDiplomacy(r)),
                ("Economy", r => save.economy = LoadEconomy(r))
            };
            
            foreach (var (name, loader) in sections) {
                try {
                    loader(reader);
                    LogInfo($"Loaded {name} successfully");
                }
                catch (Exception e) {
                    LogWarning($"Failed to load {name}, using defaults: {e.Message}");
                    UseDefaults(save, name);
                }
            }
        }
        
        return save;
    }
    
    private void UseDefaults(SaveGame save, string section) {
        switch (section) {
            case "Provinces":
                // Reset to scenario start
                save.provinces = LoadScenarioProvinces();
                break;
            case "Nations":
                // Recreate from province ownership
                save.nations = RecreateNationsFromProvinces(save.provinces);
                break;
            case "Economy":
                // Start with default treasury
                save.economy = CreateDefaultEconomy(save.nations);
                break;
        }
    }
}
```

## Multiplayer Desync Recovery

### Detecting and Fixing Desyncs
```csharp
public class DesyncRecovery {
    private uint lastValidChecksum;
    private GameStateSnapshot lastValidState;
    private int desyncCount = 0;
    
    public void ValidateSync() {
        uint localChecksum = GameState.CalculateChecksum();
        
        // Exchange checksums with other players
        var remoteChecksums = Network.ExchangeChecksums(localChecksum);
        
        if (!AllChecksumsMatch(localChecksum, remoteChecksums)) {
            HandleDesync(localChecksum, remoteChecksums);
        }
        else {
            // Valid state, save it
            lastValidChecksum = localChecksum;
            lastValidState = GameState.CreateQuickSnapshot();
            desyncCount = 0;
        }
    }
    
    private void HandleDesync(uint local, Dictionary<byte, uint> remote) {
        desyncCount++;
        
        LogError($"Desync detected! Local: {local}, Remote: {string.Join(",", remote)}");
        
        if (desyncCount > 3) {
            // Too many desyncs, need full resync
            RequestFullResync();
        }
        else {
            // Try quick recovery
            AttemptQuickResync();
        }
    }
    
    private void AttemptQuickResync() {
        // Rollback to last valid state
        GameState.RestoreFromSnapshot(lastValidState);
        
        // Request commands since last valid
        var missedCommands = Network.RequestCommandsSince(lastValidChecksum);
        
        // Replay missed commands
        foreach (var cmd in missedCommands) {
            if (cmd.Validate(GameState.Current)) {
                cmd.Execute(GameState.Current);
            }
        }
        
        // Verify sync restored
        ValidateSync();
    }
    
    private void RequestFullResync() {
        UI.ShowMessage("Synchronization lost, resyncing with host...");
        
        if (Network.IsHost) {
            // Host sends authoritative state
            var state = GameState.CreateFullSnapshot();
            Network.BroadcastState(state);
        }
        else {
            // Request state from host
            Network.RequestStateFromHost((state) => {
                GameState.RestoreFromSnapshot(state);
                UI.ShowMessage("Resync complete");
            });
        }
    }
}
```

## Memory Error Handling

### Out of Memory Recovery
```csharp
public class MemoryErrorHandler {
    private bool lowMemoryMode = false;
    
    public void Initialize() {
        // Monitor memory pressure
        Application.lowMemory += OnLowMemory;
    }
    
    private void OnLowMemory() {
        LogWarning("Low memory detected, entering conservation mode");
        
        if (!lowMemoryMode) {
            EnterLowMemoryMode();
        }
        else {
            // Already in low memory mode, do emergency cleanup
            EmergencyMemoryCleanup();
        }
    }
    
    private void EnterLowMemoryMode() {
        lowMemoryMode = true;
        
        // Reduce texture quality
        QualitySettings.masterTextureLimit = 2;
        
        // Clear caches
        PathfindingCache.Clear();
        ModifierCache.Clear();
        AICache.Clear();
        
        // Reduce AI thinking
        AI.ReduceComplexity();
        
        // Disable non-essential features
        DisableParticleEffects();
        DisableMusicSystem();
        
        // Force garbage collection
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        
        UI.ShowWarning("Entered low memory mode - some features disabled");
    }
    
    private void EmergencyMemoryCleanup() {
        // Last resort - save game and restart
        try {
            // Emergency save
            var emergencySave = GameState.CreateMinimalSnapshot();
            emergencySave.SaveToDisk("emergency_recovery.sav");
            
            UI.ShowCriticalMessage(
                "Critical memory shortage. Game saved and will restart.",
                onOK: () => {
                    Application.Quit();
                    System.Diagnostics.Process.Start(Application.dataPath);
                }
            );
        }
        catch {
            // Can't even save, just crash gracefully
            CrashReporter.ReportCrash("Out of memory, unable to save");
            Application.Quit(-1);
        }
    }
}
```

## Script & Mod Error Handling

### Invalid Script Recovery
```csharp
public class ScriptErrorHandler {
    private Dictionary<string, object> fallbackValues = new();
    
    public T LoadScriptValue<T>(ParsedNode node, string key, T defaultValue) {
        try {
            if (!node.HasKey(key)) {
                LogWarning($"Missing key '{key}', using default: {defaultValue}");
                return defaultValue;
            }
            
            var value = node.GetValue<T>(key);
            
            // Validate based on type
            if (!ValidateValue(value)) {
                LogWarning($"Invalid value for '{key}': {value}, using default");
                return defaultValue;
            }
            
            return value;
        }
        catch (Exception e) {
            LogError($"Failed to load '{key}': {e.Message}");
            return defaultValue;
        }
    }
    
    private bool ValidateValue<T>(T value) {
        switch (value) {
            case float f:
                return !float.IsNaN(f) && !float.IsInfinity(f) && f > -1000000 && f < 1000000;
            case int i:
                return i > -1000000 && i < 1000000;
            case string s:
                return !string.IsNullOrEmpty(s) && s.Length < 1000;
            default:
                return value != null;
        }
    }
    
    public void HandleModConflict(ModConflict conflict) {
        LogWarning($"Mod conflict detected: {conflict}");
        
        switch (conflict.Type) {
            case ConflictType.DuplicateID:
                // Use first, ignore second
                LogWarning($"Duplicate ID {conflict.ID}, keeping first definition");
                break;
                
            case ConflictType.MissingDependency:
                // Disable dependent mod
                DisableMod(conflict.Mod);
                UI.ShowWarning($"Mod {conflict.Mod} disabled - missing dependency");
                break;
                
            case ConflictType.VersionMismatch:
                // Try to load anyway with warning
                UI.ShowWarning($"Mod {conflict.Mod} may not be compatible with this version");
                break;
        }
    }
}
```

## Error Reporting & Telemetry

### Automatic Error Reporting
```csharp
public class ErrorReporter {
    private Queue<ErrorReport> pendingReports = new();
    private readonly int MAX_REPORTS_PER_SESSION = 10;
    private int reportsSent = 0;
    
    public void ReportError(ErrorReport error) {
        // Don't spam reports
        if (reportsSent >= MAX_REPORTS_PER_SESSION) return;
        
        // Sanitize personal data
        error.SanitizePersonalData();
        
        // Add context
        error.gameVersion = Application.version;
        error.platform = Application.platform.ToString();
        error.systemInfo = GetSystemInfo();
        error.gameState = GetGameStateContext();
        
        // Queue for sending
        pendingReports.Enqueue(error);
        
        // Send in background
        Task.Run(() => SendErrorReport(error));
    }
    
    private GameStateContext GetGameStateContext() {
        return new GameStateContext {
            currentDate = TimeManager.CurrentDate,
            nationCount = Nations.ActiveCount,
            provinceCount = Provinces.Count,
            saveSize = GetCurrentSaveSize(),
            modsActive = GetActiveMods(),
            memoryUsage = GC.GetTotalMemory(false),
            fps = GetAverageFPS()
        };
    }
    
    private async Task SendErrorReport(ErrorReport error) {
        try {
            var json = JsonConvert.SerializeObject(error);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await httpClient.PostAsync(
                "https://telemetry.yourgame.com/errors",
                content
            );
            
            if (response.IsSuccessStatusCode) {
                reportsSent++;
                LogInfo("Error report sent successfully");
            }
        }
        catch {
            // Don't error while reporting errors!
            LogInfo("Failed to send error report");
        }
    }
}
```

## User-Facing Error Messages

### Friendly Error Translation
```csharp
public class ErrorMessageTranslator {
    private Dictionary<Type, string> userMessages = new() {
        [typeof(OutOfMemoryException)] = "The game is running low on memory. Some features have been disabled to continue playing.",
        [typeof(SaveCorruptedException)] = "Your save file appears damaged, but we've recovered most of your progress.",
        [typeof(DesyncException)] = "Lost sync with other players. Resyncing now...",
        [typeof(ModLoadException)] = "A mod failed to load properly and has been disabled.",
        [typeof(CommandValidationException)] = "That action cannot be performed right now.",
    };
    
    public string GetUserMessage(Exception error) {
        // Check for known error types
        if (userMessages.TryGetValue(error.GetType(), out string message)) {
            return message;
        }
        
        // Generic messages based on severity
        var severity = ClassifyError(error);
        return severity switch {
            ErrorSeverity.Trivial => null, // Don't show
            ErrorSeverity.Minor => "A minor issue occurred but has been fixed automatically.",
            ErrorSeverity.Major => "An error occurred. The game has recovered but some data may be lost.",
            ErrorSeverity.Critical => "A serious error occurred. Please save your game and restart.",
            ErrorSeverity.Fatal => "A critical error has occurred. The game must close.",
            _ => "An unexpected error occurred."
        };
    }
}
```

## Testing Error Recovery

### Automated Error Testing
```csharp
#if UNITY_EDITOR
public class ErrorRecoveryTests {
    [Test]
    public void TestSaveCorruption() {
        // Create valid save
        var save = CreateTestSave();
        save.Save("test.sav");
        
        // Corrupt it
        CorruptFile("test.sav", corruption: 0.1f); // 10% corruption
        
        // Try to load
        var loaded = SaveSystem.LoadWithRecovery("test.sav");
        
        // Should recover
        Assert.NotNull(loaded);
        Assert.AreEqual(save.nations.Count, loaded.nations.Count);
    }
    
    [Test]
    public void TestCommandValidation() {
        // Create invalid command
        var cmd = new MoveArmyCommand {
            armyId = 99999,  // Invalid ID
            targetProvince = 99999  // Invalid province
        };
        
        // Should not crash
        bool result = CommandHandler.TryExecute(cmd);
        
        Assert.False(result);
        Assert.True(GameState.IsValid());
    }
    
    [Test]
    public void TestMemoryPressure() {
        // Simulate low memory
        var arrays = new List<byte[]>();
        
        try {
            // Allocate until low memory
            while (GC.GetTotalMemory(false) < GC.MaxGeneration) {
                arrays.Add(new byte[10_000_000]); // 10MB chunks
            }
        }
        catch (OutOfMemoryException) {
            // Should handle gracefully
            Assert.True(MemoryHandler.IsInLowMemoryMode);
            Assert.True(GameState.IsValid());
        }
    }
}
#endif
```

## Recovery Strategies Priority

### Strategy Selection
```csharp
public class RecoveryStrategySelector {
    public IRecoveryStrategy SelectStrategy(Exception error) {
        return error switch {
            // Data errors - try to fix
            DataCorruptionException => new DataRepairStrategy(),
            
            // State errors - rollback
            InvalidStateException => new RollbackStrategy(),
            
            // Sync errors - resynchronize
            DesyncException => new ResyncStrategy(),
            
            // Memory errors - reduce usage
            OutOfMemoryException => new MemoryReductionStrategy(),
            
            // Script errors - use defaults
            ScriptException => new DefaultValueStrategy(),
            
            // Network errors - reconnect
            NetworkException => new ReconnectionStrategy(),
            
            // Unknown - safe mode
            _ => new SafeModeStrategy()
        };
    }
}
```

## Performance Impact

### Error Handling Overhead
```
Normal Operation:
- Validation checks: <0.01ms per command
- Checksum validation: 0.1ms per frame
- Error logging: <0.001ms (async)
- Total overhead: <1% of frame time

During Recovery:
- State snapshot: 10ms
- Validation sweep: 50ms  
- Recovery attempt: 100-500ms
- User notification: 1ms
- Acceptable for rare events

Memory Overhead:
- Recovery snapshots: 5MB
- Error log buffer: 100KB
- Validation cache: 50KB
- Total: <6MB
```

## Best Practices

1. **Validate at boundaries** - Check input as early as possible
2. **Fail fast, recover gracefully** - Detect errors quickly, fix quietly
3. **Log everything** - You can't fix what you can't see
4. **Test error paths** - Error handling code needs testing too
5. **Provide fallbacks** - Always have a Plan B (and C)
6. **Keep user informed** - But don't overwhelm with technical details
7. **Auto-report critical errors** - Get telemetry on real-world failures
8. **Version everything** - Makes debugging user reports much easier

## Summary

This error recovery architecture ensures:
- **Game never crashes** - Always tries to recover
- **Progress never lost** - Multiple backup strategies  
- **Clear error reporting** - Developers get actionable data
- **Smooth user experience** - Errors handled transparently
- **Multiplayer resilience** - Automatic resync on problems
- **Mod compatibility** - Graceful handling of bad scripts

The key principle: **Expect failure, design for recovery**. Every system has fallbacks, every operation can be rolled back, and the game always tries to continue running even in degraded mode rather than crashing.