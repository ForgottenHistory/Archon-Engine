# Multiplayer-Ready Performance Architecture Guide

## Core Multiplayer Principles

### The Fundamental Challenge
In single-player, you control everything. In multiplayer, you must maintain **deterministic simulation** across all clients while keeping high performance.

```
Single-Player:
GPU has all data → Renders immediately → 200 FPS

Multiplayer:
Game state must be synchronized → Validated → Then rendered → ???FPS
```

## Architecture Decision: Client-Server vs Peer-to-Peer vs Hybrid

### Option 1: Lockstep (Paradox Style)
**How it works**: All clients run identical simulation, only share inputs
```
Client A: "I clicked province 547" → All clients
Client B: "I moved army to 122" → All clients  
Everyone simulates the same result
```

**Pros:**
- Minimal network traffic (only sending commands)
- No host advantage
- Works with our GPU architecture

**Cons:**
- Slowest player sets the speed
- Desyncs are catastrophic
- Pause-heavy gameplay

### Option 2: Client-Server (Modern Approach)
**How it works**: Authoritative server, clients predict and correct
```
Client: "I want to own province 547" → Server
Server: "Approved, you now own 547" → All clients
Clients: Update texture and render
```

**Pros:**
- No desyncs possible
- Cheating prevention
- Speed independent per player

**Cons:**
- Higher bandwidth
- Needs rollback/prediction
- Server costs

### Recommended: Hybrid Approach
**Authoritative server for state, client-side rendering optimizations**

## Separating Simulation from Presentation

### The Key Pattern: Dual-Layer Architecture

```csharp
// LAYER 1: Authoritative Game State (CPU, synchronized)
public class GameState {
    // Minimal, deterministic data
    struct ProvinceState {
        public ushort ownerID;
        public ushort controllerID;
        public byte development;
        public byte fortLevel;
    }
    
    // Fixed-size, deterministic
    NativeArray<ProvinceState> provinces;
    
    // This is what gets synchronized
    public byte[] Serialize() {
        // Efficient binary serialization
        return provinces.Reinterpret<byte>().ToArray();
    }
}

// LAYER 2: Presentation State (GPU, local)
public class RenderState {
    // GPU textures for rendering
    Texture2D provinceOwners;
    Texture2D provinceColors;
    RenderTexture borders;
    
    // Updated FROM GameState, but can have local additions
    public void UpdateFromGameState(GameState state) {
        // Convert authoritative state to GPU format
        UpdateOwnershipTexture(state.provinces);
        UpdateBorderTexture(state.provinces);
    }
    
    // Local-only visual effects
    public void AddLocalEffects() {
        // Hovering, animations, etc. - not synchronized
        AddHoverEffect(localMousePosition);
        AnimateBorderPulse(selectedProvince);
    }
}
```

## Network-Optimized Data Structures

### Province Updates - Differential Synchronization

```csharp
public class ProvinceNetworkUpdate {
    // Don't send all 10,000 provinces every tick!
    // Only send changes
    
    public struct ProvinceDelta {
        public ushort provinceID;
        public byte fieldMask;  // Which fields changed
        public ushort newValue;  // The new value
    }
    
    // Typical frame: 0-10 provinces change
    // Network data: 10 × 4 bytes = 40 bytes
    // Instead of: 10,000 × 8 bytes = 80,000 bytes!
    
    public void SendUpdate(List<ProvinceDelta> deltas) {
        // Pack into smallest possible packet
        using (var writer = new NetworkWriter()) {
            writer.WriteByte((byte)deltas.Count);
            foreach (var delta in deltas) {
                writer.WriteUShort(delta.provinceID);
                writer.WriteByte(delta.fieldMask);
                writer.WriteUShort(delta.newValue);
            }
            Send(writer.ToArray());  // ~40-100 bytes typically
        }
    }
}
```

### Texture Updates - Client-Side Prediction

```csharp
public class PredictiveRenderer {
    // Immediately update visual, correct later if needed
    
    public void OnLocalAction(int provinceID, int newOwner) {
        // 1. Immediately update GPU texture (instant feedback)
        UpdateProvinceTexture(provinceID, newOwner);
        
        // 2. Send to server
        SendOwnerChangeRequest(provinceID, newOwner);
        
        // 3. Store prediction
        predictions.Add(new Prediction {
            provinceID = provinceID,
            predictedOwner = newOwner,
            timestamp = NetworkTime.time
        });
    }
    
    public void OnServerResponse(int provinceID, int actualOwner) {
        if (actualOwner != GetPredictedOwner(provinceID)) {
            // Prediction was wrong, correct it
            UpdateProvinceTexture(provinceID, actualOwner);
            // Maybe add visual indicator of correction
        }
        predictions.Remove(provinceID);
    }
}
```

## Multiplayer-Specific Optimizations

### 1. Temporal Coherence Across Network

```csharp
public class NetworkTemporalCache {
    // Cache province states to detect changes
    ProvinceState[] lastSentState;
    ProvinceState[] currentState;
    
    public List<int> GetChangedProvinces() {
        var changed = new List<int>();
        
        // Parallel comparison using Burst
        var job = new CompareStatesJob {
            previous = lastSentState,
            current = currentState,
            changed = changed
        };
        job.Schedule(10000, 64).Complete();
        
        return changed;
    }
}
```

### 2. LOD for Network Updates

```csharp
public class NetworkLOD {
    // Not all provinces need same update rate
    
    enum UpdatePriority {
        Critical = 1,    // Every tick (combat, player provinces)
        Important = 5,   // Every 5 ticks (nearby provinces)
        Background = 20  // Every 20 ticks (far away provinces)
    }
    
    public UpdatePriority GetProvincePriority(int provinceID) {
        if (IsPlayerProvince(provinceID)) return UpdatePriority.Critical;
        if (IsVisibleToPlayer(provinceID)) return UpdatePriority.Important;
        return UpdatePriority.Background;
    }
}
```

### 3. Batched GPU Updates

```csharp
public class BatchedTextureUpdater {
    // Collect all network updates, apply once per frame
    Queue<ProvinceUpdate> pendingUpdates = new();
    
    void OnNetworkUpdate(ProvinceUpdate update) {
        pendingUpdates.Enqueue(update);
        // DON'T apply immediately
    }
    
    void LateUpdate() {
        if (pendingUpdates.Count > 0) {
            // Apply all at once
            var pixels = provinceTexture.GetPixels32();
            
            while (pendingUpdates.Count > 0) {
                var update = pendingUpdates.Dequeue();
                ApplyToPixelArray(pixels, update);
            }
            
            provinceTexture.SetPixels32(pixels);
            provinceTexture.Apply();  // Single GPU upload
        }
    }
}
```

## Determinism Requirements

### What Must Be Deterministic (Synchronized)

```csharp
public class DeterministicState {
    // These MUST be identical across all clients
    FixedPoint64 provinceWealth;  // Not float!
    int32 provinceDevelopment;    // Exact integers
    uint16 ownerID;              // No ambiguity
    
    // Use fixed-point math, not floating point
    FixedPoint64 CalculateTax(FixedPoint64 baseTax) {
        return baseTax * FixedPoint64.FromFloat(1.1f);
    }
}
```

### What Can Be Non-Deterministic (Local)

```csharp
public class LocalOnlyState {
    // These can differ between clients
    float animationTime;         // Visual only
    Color highlightColor;        // UI preference
    bool showingTooltip;         // UI state
    ParticleSystem effects;      // Visual flair
    
    // GPU textures for rendering (derived from deterministic state)
    Texture2D visualRepresentation;
}
```

## Network Architecture Patterns

### Pattern 1: Command Pattern for Province Changes

```csharp
public interface IProvinceCommand {
    void Execute(GameState state);
    byte[] Serialize();
}

public class ChangeOwnerCommand : IProvinceCommand {
    ushort provinceID;
    ushort newOwnerID;
    
    public void Execute(GameState state) {
        state.provinces[provinceID].ownerID = newOwnerID;
    }
    
    public byte[] Serialize() {
        // 5 bytes: [CommandType(1)][ProvinceID(2)][OwnerID(2)]
        return new byte[] {
            (byte)CommandType.ChangeOwner,
            (byte)(provinceID & 0xFF),
            (byte)(provinceID >> 8),
            (byte)(newOwnerID & 0xFF),
            (byte)(newOwnerID >> 8)
        };
    }
}
```

### Pattern 2: Ring Buffer for Rollback

```csharp
public class RollbackBuffer {
    const int ROLLBACK_FRAMES = 30;  // 0.5 seconds at 60fps
    
    GameState[] stateHistory = new GameState[ROLLBACK_FRAMES];
    int currentFrame = 0;
    
    public void SaveState(GameState state) {
        stateHistory[currentFrame % ROLLBACK_FRAMES] = state.Clone();
        currentFrame++;
    }
    
    public void Rollback(int toFrame) {
        // Restore state
        var targetState = stateHistory[toFrame % ROLLBACK_FRAMES];
        currentState = targetState.Clone();
        
        // Re-apply commands from that point
        for (int f = toFrame + 1; f <= currentFrame; f++) {
            ApplyCommandsForFrame(f);
        }
        
        // Update GPU textures once
        UpdateAllTextures(currentState);
    }
}
```

### Pattern 3: Interpolation for Smooth Visuals

```csharp
public class InterpolatedRenderer {
    // Server updates at 10Hz, render at 144Hz
    
    ProvinceState previousState;
    ProvinceState targetState;
    float interpolationTime;
    
    void OnServerUpdate(ProvinceState newState) {
        previousState = currentState;
        targetState = newState;
        interpolationTime = 0;
    }
    
    void Update() {
        interpolationTime += Time.deltaTime * 10f;  // 10Hz server rate
        interpolationTime = Mathf.Min(interpolationTime, 1f);
        
        // Smooth visual interpolation
        var interpolated = ProvinceState.Lerp(
            previousState, 
            targetState, 
            interpolationTime
        );
        
        UpdateGPUTextures(interpolated);
    }
}
```

## Performance Considerations for Multiplayer

### Memory Layout for Network Efficiency

```csharp
// BAD: Scattered, hard to serialize
public class Province {
    string name;           // Variable size
    List<int> neighbors;   // Variable size
    Dictionary<string, float> modifiers;  // Variable size
}

// GOOD: Fixed size, contiguous, efficient
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceNetworkData {
    public ushort id;
    public ushort owner;
    public byte development;
    public byte terrain;
    // Fixed 6 bytes, easy to serialize
}
```

### Bandwidth Optimization

```csharp
public class BandwidthOptimizer {
    // Typical Paradox game: ~10-50 KB/s per client
    // Our target: <5 KB/s
    
    // Technique 1: Delta compression
    byte[] CompressDelta(ProvinceState[] oldState, ProvinceState[] newState) {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream)) {
            for (int i = 0; i < oldState.Length; i++) {
                if (!oldState[i].Equals(newState[i])) {
                    writer.Write((ushort)i);  // Province ID
                    writer.Write(newState[i].Pack());  // New state
                }
            }
            return stream.ToArray();
        }
    }
    
    // Technique 2: Bit packing
    uint PackProvinceFlags(Province p) {
        uint packed = 0;
        packed |= (uint)(p.isCoastal ? 1 : 0) << 0;
        packed |= (uint)(p.hasFort ? 1 : 0) << 1;
        packed |= (uint)(p.isOccupied ? 1 : 0) << 2;
        // ... 29 more boolean flags in 4 bytes
        return packed;
    }
}
```

## Testing Multiplayer Performance

```csharp
public class MultiplayerStressTest {
    [Test]
    public void TestBandwidthUnder5KB() {
        // Simulate 1 hour of gameplay
        var updates = SimulateGameplay(3600);
        var bandwidth = CalculateBandwidth(updates);
        Assert.Less(bandwidth, 5000);  // Under 5 KB/s
    }
    
    [Test]
    public void TestDeterminism() {
        var client1 = new GameState(seed: 12345);
        var client2 = new GameState(seed: 12345);
        
        // Apply same commands
        var commands = GenerateCommands(100);
        foreach (var cmd in commands) {
            client1.Execute(cmd);
            client2.Execute(cmd);
        }
        
        // States must be identical
        Assert.AreEqual(client1.Hash(), client2.Hash());
    }
    
    [Test]
    public void TestRollbackPerformance() {
        // Should handle 30 frame rollback in <16ms
        var stopwatch = Stopwatch.StartNew();
        RollbackFrames(30);
        stopwatch.Stop();
        Assert.Less(stopwatch.ElapsedMilliseconds, 16);
    }
}
```

## Architecture Decision Matrix

| Aspect | Single-Player Only | MP-Ready (Lockstep) | MP-Ready (Client-Server) |
|--------|-------------------|---------------------|------------------------|
| GPU Textures | Direct update | Must sync first | Predict + correct |
| Province Data | All on GPU | GPU + CPU mirror | GPU + CPU mirror |
| Memory Layout | Optimized for GPU | Fixed-size for network | Fixed-size for network |
| Update Frequency | Every frame | Fixed tick rate | Variable with interpolation |
| Bandwidth | 0 | ~5 KB/s | ~10 KB/s |
| Complexity | Low | Medium | High |
| Performance | 200+ FPS | 144+ FPS | 144+ FPS |
| Cheating | N/A | Possible | Protected |

## Recommended Approach

1. **Start with MP-ready architecture** even if you don't implement multiplayer immediately
2. **Separate simulation and presentation** layers from day one
3. **Use fixed-size data structures** throughout
4. **Keep authoritative state minimal** (<10 bytes per province)
5. **Let GPU handle all visual representation** (not synchronized)
6. **Design for 10Hz network updates** with 144Hz visual interpolation

This architecture gives you:
- Single-player: 200+ FPS
- Multiplayer: 144+ FPS with <5KB/s bandwidth
- Flexibility to add multiplayer later without rewriting