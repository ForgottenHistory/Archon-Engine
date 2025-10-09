# Player State Storage - Implementation Plan

**Goal**: Store the player's selected country without violating architecture principles

---

## Architecture Decision

**Player state is a GAME concept, not ENGINE concept**

- ✅ **GAME layer** (`Assets/Game/`) - Policy: "This country is controlled by the player"
- ❌ **ENGINE layer** (`Assets/Archon-Engine/`) - Mechanism: Manages all countries equally

The engine has no concept of "player" - it treats all countries the same. The game layer decides which country receives player input.

---

## What to Store

```csharp
public class PlayerState {
    // ✅ Store: Player's country ID (policy)
    private ushort playerCountryID;

    // ❌ Don't Store: Country data (use CountryQueries instead)
    // private Color32 playerColor;  // WRONG - duplicates Core data
}
```

**Principle**: Store only identifiers, query data on-demand from Core.

---

## Implementation

### 1. Create PlayerState (GAME Layer)

**File**: `Assets/Game/PlayerState.cs`

```csharp
using UnityEngine;
using Core;

namespace Game
{
    /// <summary>
    /// GAME LAYER - Tracks which country the player controls
    /// </summary>
    public class PlayerState : MonoBehaviour
    {
        [Header("Player Country")]
        [SerializeField] private ushort playerCountryID = 0;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = true;

        // References
        private GameState gameState;

        // Properties
        public ushort PlayerCountryID => playerCountryID;
        public bool HasPlayerCountry => playerCountryID != 0;

        void Awake()
        {
            // Singleton pattern (only one player)
            if (FindObjectsByType<PlayerState>(FindObjectsSortMode.None).Length > 1)
            {
                Debug.LogError("Multiple PlayerState instances found!");
                Destroy(gameObject);
                return;
            }
        }

        void Start()
        {
            gameState = FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                Debug.LogError("PlayerState: GameState not found!");
            }
        }

        /// <summary>
        /// Set the player's country (called by CountrySelectionUI)
        /// </summary>
        public void SetPlayerCountry(ushort countryID)
        {
            if (countryID == 0)
            {
                Debug.LogWarning("PlayerState: Attempted to set player country to 0 (invalid)");
                return;
            }

            playerCountryID = countryID;

            if (logStateChanges)
            {
                string tag = gameState?.CountryQueries?.GetTag(countryID) ?? countryID.ToString();
                Debug.Log($"PlayerState: Player country set to {tag} (ID: {countryID})");
            }

            // TODO: Emit PlayerCountrySelectedEvent via EventBus
        }

        /// <summary>
        /// Get player's country tag for display (uses engine query)
        /// </summary>
        public string GetPlayerCountryTag()
        {
            if (!HasPlayerCountry || gameState?.CountryQueries == null)
            {
                return "NONE";
            }

            return gameState.CountryQueries.GetTag(playerCountryID);
        }

        /// <summary>
        /// Get player's country color (uses engine query - no data duplication)
        /// </summary>
        public Color32 GetPlayerCountryColor()
        {
            if (!HasPlayerCountry || gameState?.CountryQueries == null)
            {
                return new Color32(128, 128, 128, 255);
            }

            return gameState.CountryQueries.GetColor(playerCountryID);
        }

        /// <summary>
        /// Check if a province is owned by the player (uses engine query)
        /// </summary>
        public bool IsPlayerProvince(ushort provinceID)
        {
            if (!HasPlayerCountry || gameState?.ProvinceQueries == null)
            {
                return false;
            }

            ushort owner = gameState.ProvinceQueries.GetOwner(provinceID);
            return owner == playerCountryID;
        }
    }
}
```

### 2. Integrate with CountrySelectionUI

**File**: `Assets/Game/UI/CountrySelectionUI.cs`

**Add to OnPlayButtonClicked():**
```csharp
private void OnPlayButtonClicked()
{
    // ... existing code ...

    // Store player's selected country
    var playerState = FindFirstObjectByType<PlayerState>();
    if (playerState != null)
    {
        playerState.SetPlayerCountry(selectedCountryID);
    }
    else
    {
        Debug.LogWarning("CountrySelectionUI: PlayerState not found!");
    }

    // ... existing code ...
}
```

### 3. Integrate with HegemonInitializer

**File**: `Assets/Game/HegemonInitializer.cs`

**Add PlayerState reference:**
```csharp
[SerializeField] private PlayerState playerState;
```

**Initialize PlayerState after engine initialization (direct injection):**
```csharp
// Initialize PlayerState now that GameState exists (after EngineInitializer completes)
var gameState = FindFirstObjectByType<GameState>();
if (gameState != null && playerState != null)
{
    playerState.Initialize(gameState);
    ArchonLogger.Log("HegemonInitializer: PlayerState initialized with GameState");
}
```

**Why this is elegant:**
- PlayerState stays **active from start** (no confusing inactive GameObjects)
- HegemonInitializer **explicitly injects** GameState reference when ready
- Clear, controlled initialization flow
- No "GameState not found" errors on startup

---

## Unity Setup

1. Create empty GameObject: "PlayerState" (active in scene)
2. Add `PlayerState` component
3. Assign to HegemonInitializer.playerState field

**Initialization Flow (Direct Injection Pattern):**
```
Scene Load → PlayerState active (but not initialized)
  ↓
HegemonInitializer starts
  ↓
EngineInitializer creates GameState ✓
  ↓
HegemonInitializer calls playerState.Initialize(gameState) ✓
  ↓
PlayerState ready to use
```

**Benefits:**
- No inactive GameObjects (less confusion)
- Explicit initialization control
- Clear dependency injection
- Clean error handling

---

## Architecture Compliance

### ✅ Follows Engine-Game Separation
- PlayerState is in `Game/` (policy, not mechanism)
- Uses engine APIs (CountryQueries, ProvinceQueries)
- No game logic in engine layer

### ✅ No Data Duplication
- Stores only country ID (ushort)
- Queries Core for real-time data
- Never caches Core state

### ✅ Event-Driven
- Can emit PlayerCountrySelectedEvent
- Other systems subscribe to player changes
- Decoupled communication

### ✅ Memory Efficient
- 2 bytes for country ID
- No persistent caching
- Single instance (singleton pattern)

---

## Future Extensions

### Save/Load System
```csharp
public byte[] Serialize()
{
    // Just 2 bytes!
    return BitConverter.GetBytes(playerCountryID);
}

public void Deserialize(byte[] data)
{
    playerCountryID = BitConverter.ToUInt16(data, 0);
}
```

### Multiplayer
```csharp
public class PlayerState {
    // Each client has their own PlayerState
    private ushort localPlayerCountryID;

    // Server tracks all players
    private Dictionary<int, ushort> playerCountries; // clientID → countryID
}
```

---

## Implementation Status

✅ **COMPLETE** - All components implemented and integrated

## Files Modified

- `Assets/Game/PlayerState.cs` (+168 lines, new file)
  - **Initialize(GameState)** method for direct dependency injection
  - **IsInitialized** property to prevent premature use
  - Queries Core for all data (no duplication)
  - Singleton pattern (network multiplayer ready)
  - Active from start (no confusing inactive GameObjects)

- `Assets/Game/UI/CountrySelectionUI.cs` (+10 lines)
  - Calls PlayerState.SetPlayerCountry() on Play button

- `Assets/Game/HegemonInitializer.cs` (+16 lines)
  - Injects GameState reference after engine initialization
  - Clean initialization flow with error handling

---

## Testing Checklist

1. ✅ Select country → PlayerState.PlayerCountryID updates
2. ✅ GetPlayerCountryTag() returns correct tag
3. ✅ GetPlayerCountryColor() returns correct color
4. ✅ IsPlayerProvince() correctly identifies player provinces
5. ✅ Multiple PlayerState instances rejected
6. ✅ Logs show state changes

---

*Simple, architecture-compliant player state storage*
