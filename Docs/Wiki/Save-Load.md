# Save/Load System

The save/load system provides persistent game state storage with atomic writes, version compatibility, and game-specific callbacks for layer separation.

## Architecture

```
SaveManager (MonoBehaviour)
├── SaveGameData           - Container for all save data
├── SaveFileSerializer     - Binary serialization to disk
├── SystemSerializer       - Helper for system serialization
└── Callbacks             - GAME layer hooks
```

**Key Principles:**
- Systems serialize their own data via OnSave/OnLoad
- Atomic writes (temp file → rename) prevent corruption
- ENGINE provides mechanism, GAME provides policy
- Version compatibility checks

## Basic Usage

### Saving

```csharp
var saveManager = GetComponent<SaveManager>();

// Named save
saveManager.SaveGame("my_save");

// Quick save (F6 by default)
saveManager.QuickSave();
```

### Loading

```csharp
// Named load
saveManager.LoadGame("my_save");

// Quick load (F7 by default)
saveManager.QuickLoad();
```

### Listing Saves

```csharp
// Get all save file names
string[] saves = saveManager.GetSaveFileNames();

foreach (string saveName in saves)
{
    Debug.Log($"Found save: {saveName}");
}

// Delete a save
saveManager.DeleteSave("old_save");
```

## SaveGameData Structure

The save container holds metadata and system-specific data:

```csharp
[Serializable]
public class SaveGameData
{
    // Metadata
    public string gameVersion;
    public int saveFormatVersion;
    public string saveName;
    public long saveDateTicks;
    public int currentTick;
    public int gameSpeed;
    public string scenarioName;

    // System data (key: system name, value: serialized data)
    public Dictionary<string, object> systemData;

    // Command log for determinism verification
    public List<byte[]> commandLog;
    public uint expectedChecksum;
}
```

### Accessing System Data

```csharp
// Store data
saveData.SetSystemData("MySystem", mySerializedData);

// Retrieve data
byte[] data = saveData.GetSystemData<byte[]>("MySystem");

// Check version compatibility
if (saveData.IsCompatibleVersion(Application.version))
{
    // Safe to load
}
```

## Implementing Save/Load in GameSystems

### Using GameSystem Base Class

GameSystem provides `OnSave` and `OnLoad` hooks:

```csharp
public class EconomySystem : GameSystem
{
    public override string SystemName => "Economy";

    private NativeArray<int> countryGold;

    protected override void OnSave(SaveGameData saveData)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            // Write gold for each country
            writer.Write(countryGold.Length);
            for (int i = 0; i < countryGold.Length; i++)
            {
                writer.Write(countryGold[i]);
            }

            saveData.SetSystemData(SystemName, stream.ToArray());
        }
    }

    protected override void OnLoad(SaveGameData saveData)
    {
        byte[] data = saveData.GetSystemData<byte[]>(SystemName);
        if (data == null) return;

        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                countryGold[i] = reader.ReadInt32();
            }
        }
    }
}
```

### Direct SaveState/LoadState Pattern

Core systems use direct methods:

```csharp
public class ProvinceSystem
{
    public void SaveState(BinaryWriter writer)
    {
        writer.Write(provinces.Length);
        for (int i = 0; i < provinces.Length; i++)
        {
            provinces[i].Serialize(writer);
        }
    }

    public void LoadState(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            provinces[i].Deserialize(reader);
        }
    }
}
```

## GAME Layer Callbacks

SaveManager provides callbacks for game-specific serialization:

### Player State

```csharp
// In your game initializer
saveManager.OnSerializePlayerState = () =>
{
    using (var stream = new MemoryStream())
    using (var writer = new BinaryWriter(stream))
    {
        writer.Write(playerState.PlayerCountryId);
        writer.Write(playerState.Gold);
        return stream.ToArray();
    }
};

saveManager.OnDeserializePlayerState = (data) =>
{
    using (var stream = new MemoryStream(data))
    using (var reader = new BinaryReader(stream))
    {
        playerState.PlayerCountryId = reader.ReadUInt16();
        playerState.Gold = reader.ReadInt32();
    }
};
```

### Post-Load Finalization

```csharp
// Rebuild caches, refresh GPU textures after load
saveManager.OnPostLoadFinalize = () =>
{
    // Refresh map textures
    mapRenderer.RefreshAllTextures();

    // Rebuild lookup caches
    buildingSystem.RebuildLookups();

    // Emit load complete event
    gameState.EventBus.Emit(new GameLoadedEvent());
};
```

## Configuration

```csharp
[Header("Configuration")]
public string saveFileExtension = "sav";
public bool logSaveLoadOperations = true;

[Header("Hotkeys")]
public KeyCode quickSaveKey = KeyCode.F6;
public KeyCode quickLoadKey = KeyCode.F7;
public bool enableHotkeys = true;
```

### Save Directory

Saves are stored in platform-specific persistent data path:
- Windows: `%USERPROFILE%/AppData/LocalLow/<company>/<product>/Saves/`
- macOS: `~/Library/Application Support/<company>/<product>/Saves/`
- Linux: `~/.config/unity3d/<company>/<product>/Saves/`

## Atomic Writes

Save files use atomic writes to prevent corruption:

```
1. Write to temp file: "save_name.sav.tmp"
2. Verify write succeeded
3. Delete old file: "save_name.sav"
4. Rename temp: "save_name.sav.tmp" → "save_name.sav"
```

If the game crashes during save, the temp file is ignored on next load.

## Version Compatibility

```csharp
public bool IsCompatibleVersion(string currentVersion)
{
    // Currently requires exact version match
    return gameVersion == currentVersion;
}
```

Future versions will support migration:

```csharp
// Migration pattern (future)
if (saveData.saveFormatVersion < 2)
{
    MigrateV1ToV2(saveData);
}
```

## Double-Buffer Synchronization

After load, double buffers must be synced to prevent UI reading stale data:

```csharp
private void SyncDoubleBuffers()
{
    gameState.Provinces?.SyncBuffersAfterLoad();
}
```

This is handled automatically by SaveManager.

## Serialization Helpers

### SerializationHelper

```csharp
// Write NativeArray
SerializationHelper.WriteNativeArray(writer, myNativeArray);

// Read NativeArray
SerializationHelper.ReadNativeArray(reader, ref myNativeArray);

// Write FixedPoint64
SerializationHelper.WriteFixedPoint64(writer, value);

// Read FixedPoint64
var value = SerializationHelper.ReadFixedPoint64(reader);
```

## Command Log (Multiplayer)

For determinism verification, save files can store recent commands:

```csharp
// Store command
saveData.commandLog.Add(command.Serialize());

// Store expected checksum
saveData.expectedChecksum = CalculateStateChecksum();

// Verify on load
if (ReplayCommands(saveData.commandLog) != saveData.expectedChecksum)
{
    Debug.LogError("Determinism verification failed!");
}
```

## API Reference

- [SaveManager](~/api/Core.SaveLoad.SaveManager.html) - Main orchestrator
- [SaveGameData](~/api/Core.SaveLoad.SaveGameData.html) - Save container
- [SaveFileSerializer](~/api/Core.SaveLoad.SaveFileSerializer.html) - Disk I/O
- [SerializationHelper](~/api/Core.SaveLoad.SerializationHelper.html) - Utilities
