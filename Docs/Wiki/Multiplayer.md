# Multiplayer

Archon uses lockstep command synchronization for multiplayer. All clients run identical simulation, synced via commands. One player hosts, others connect as clients.

## Why Lockstep?

```
❌ WRONG - State sync (too much bandwidth)
Send entire game state every tick → megabytes per second

✅ CORRECT - Command sync
Send player commands → kilobytes per second
All clients execute same commands → identical state
```

Benefits:
- **Minimal bandwidth** - Only commands sent, not state
- **Deterministic** - Same commands produce same results
- **Replay support** - Store commands, replay later
- **Desync recovery** - Detect via checksums, resync automatically

## Quick Start

### Hosting a Game

```csharp
// In your lobby UI
networkInitializer.StartHost(port: 7777);

// Players join, select countries, click ready
// Host clicks "Start Game" when all ready
```

### Joining a Game

```csharp
// Client connects via IP
networkInitializer.Connect("192.168.1.100", port: 7777);

// Select country, click ready
// Wait for host to start
```

## Command Synchronization

All commands sync automatically via `CommandProcessor`:

```csharp
// UI creates command with explicit CountryId
var cmd = new CreateUnitCommand
{
    CountryId = playerState.PlayerCountryId,  // Explicit!
    ProvinceId = selectedProvince,
    UnitTypeId = unitType
};

// Submit through CommandProcessor (or gameState.TryExecuteCommand)
gameState.TryExecuteCommand(cmd, out string result);
```

### Command Flow

```
Client UI → Command → CommandProcessor.SubmitCommand()
    ↓
Send to Host (if multiplayer client)
    ↓
Host validates and executes
    ↓
Host broadcasts to all clients
    ↓
All clients execute → identical state
```

### Critical Rules

1. **Explicit CountryId** - Commands must carry the acting country's ID
2. **Never use playerState in Execute()** - Different on each client
3. **All state changes through commands** - Direct modification breaks sync

```csharp
// ❌ WRONG - Uses local player, breaks on receiving client
public override void Execute(GameState gameState)
{
    unitSystem.CreateUnit(provinceId, unitType, playerState.PlayerCountryId);
}

// ✅ CORRECT - Uses command's explicit CountryId
public override void Execute(GameState gameState)
{
    unitSystem.CreateUnit(provinceId, unitType, CountryId);
}
```

## Creating Multiplayer Commands

Extend `BaseCommand` with serialization:

```csharp
public class MyCommand : BaseCommand
{
    public ushort CountryId { get; set; }
    public ushort TargetId { get; set; }
    public int Amount { get; set; }

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write(CountryId);
        writer.Write(TargetId);
        writer.Write(Amount);
    }

    public override void Deserialize(BinaryReader reader)
    {
        CountryId = reader.ReadUInt16();
        TargetId = reader.ReadUInt16();
        Amount = reader.ReadInt32();
    }

    public override void Execute(GameState gameState)
    {
        // Use CountryId from command, not playerState
        mySystem.DoAction(CountryId, TargetId, Amount);
    }
}
```

### Custom Serialization (Lists, Arrays)

For variable-length data like paths:

```csharp
public class MoveUnitCommand : BaseCommand
{
    public ushort UnitId { get; set; }
    public List<ushort> Path { get; set; }

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write(UnitId);
        writer.Write((ushort)(Path?.Count ?? 0));
        if (Path != null)
        {
            foreach (var provinceId in Path)
                writer.Write(provinceId);
        }
    }

    public override void Deserialize(BinaryReader reader)
    {
        UnitId = reader.ReadUInt16();
        int count = reader.ReadUInt16();
        Path = new List<ushort>(count);
        for (int i = 0; i < count; i++)
            Path.Add(reader.ReadUInt16());
    }
}
```

## AI in Multiplayer

AI runs only on host to prevent divergent decisions:

```csharp
public void ProcessAI()
{
    // Skip AI on clients
    if (networkInitializer.IsMultiplayer && !networkInitializer.IsHost)
        return;

    foreach (var countryId in countries)
    {
        // Skip human-controlled countries
        if (networkInitializer.IsCountryHumanControlled(countryId))
            continue;

        // AI creates commands like players
        var cmd = new BuildCommand { CountryId = countryId, ... };
        gameCommandProcessor.SubmitCommand(cmd, out _);
    }
}
```

## Time Synchronization

`NetworkTimeSync` keeps game time aligned:

- Host controls game speed
- Speed changes broadcast to clients
- Pause state synchronized
- Clients follow host's time

```csharp
// Host changes speed
timeManager.SetSpeed(GameSpeed.Fast);
// NetworkTimeSync broadcasts to all clients
// All clients update to Fast speed
```

## Lobby System

### NetworkInitializer

Manages multiplayer session state:

```csharp
// Check multiplayer state
if (networkInitializer.IsMultiplayer)
{
    if (networkInitializer.IsHost)
        // We're the host
    else
        // We're a client
}

// Check if country is human-controlled
if (networkInitializer.IsCountryHumanControlled(countryId))
    // Skip AI for this country
```

### Lobby Flow

1. Host starts lobby, selects country
2. Clients connect, select their countries
3. All players click "Ready"
4. Host clicks "Start Game"
5. Game begins with synchronized state

## Desync Detection

Periodic checksum verification catches desyncs:

1. All clients compute state checksum
2. Host compares checksums
3. Mismatch detected → trigger recovery
4. Client receives full state from host
5. Brief pause (1-3 seconds), then continue

This is automatic - no player action required.

## Determinism Requirements

Multiplayer requires deterministic simulation:

| Do | Don't |
|---|-------|
| FixedPoint64 for math | float/double in simulation |
| Sorted iteration order | Dictionary.Keys iteration |
| Explicit random seeds | System.Random without seed |
| Command-based changes | Direct state mutation |

See [Commands](Commands.md) for command pattern details.

## Testing Multiplayer

### Local Testing

1. Build the game
2. Run build as Host
3. Run Editor as Client (connect to 127.0.0.1)
4. Or run two builds

### Common Issues

**Commands not syncing:**
- Check command has `Serialize()`/`Deserialize()`
- Verify command is registered with `CommandProcessor.RegisterCommandType<T>()`
- Ensure command is in `StarterKit.Commands` namespace (for auto-registration)

**State diverges:**
- Check for float math in simulation
- Verify all state changes go through commands
- Check AI only runs on host

**Units/buildings not visible on client:**
- Commands must use explicit CountryId
- System methods must accept countryId parameter

## StarterKit Examples

Working multiplayer commands:
- `CreateUnitCommand` - Unit creation with explicit CountryId
- `QueueUnitMovementCommand` - Path serialization example
- `ColonizeCommand` - Multi-system command (economy + province)
- `ConstructBuildingCommand` - Building with validation

See `Scripts/StarterKit/Commands/` for implementations.

## Best Practices

1. **Always include CountryId** - Commands must know who's acting
2. **Test both host and client** - Bugs often only appear on one side
3. **Use deterministic math** - FixedPoint64, not float
4. **Keep commands small** - Serialize only what's needed
5. **Validate on host** - Clients send, host validates
6. **AI on host only** - Prevents divergent decisions
7. **Log command execution** - Helps debug sync issues
