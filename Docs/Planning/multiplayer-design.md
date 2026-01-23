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

See [Save/Load Architecture](save-load-architecture.md) - command pattern enables both multiplayer AND save/replay with the same code.

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

**Note**: This network-optimized memory layout aligns with the hot/cold data separation described in [Performance Architecture](performance-architecture-guide.md). The 8-byte ProvinceState struct represents the "hot data" that needs synchronization, while cold data remains local.

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

---

## Transport Layer Architecture (Implementation Plan)

### Session Model: Player-Hosted (Paradox Style)

One player acts as host (runs server + client), others connect as clients. This matches the multiplayer model used in EU4, CK3, HOI4, and Victoria 3.

```
┌─────────────────────────────────────────────────────────┐
│                      HOST PLAYER                        │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐ │
│  │   Client    │◄──►│   Server    │◄──►│  Transport  │ │
│  │  (local)    │    │   Logic     │    │   Layer     │ │
│  └─────────────┘    └─────────────┘    └──────┬──────┘ │
└───────────────────────────────────────────────┼────────┘
                                                │
                    ┌───────────────────────────┼───────────────────────────┐
                    │                           │                           │
            ┌───────▼───────┐           ┌───────▼───────┐           ┌───────▼───────┐
            │   CLIENT 1    │           │   CLIENT 2    │           │   CLIENT N    │
            │  ┌─────────┐  │           │  ┌─────────┐  │           │  ┌─────────┐  │
            │  │Transport│  │           │  │Transport│  │           │  │Transport│  │
            │  └────┬────┘  │           │  └────┬────┘  │           │  └────┬────┘  │
            │  ┌────▼────┐  │           │  ┌────▼────┐  │           │  ┌────▼────┐  │
            │  │ Client  │  │           │  │ Client  │  │           │  │ Client  │  │
            │  │  Logic  │  │           │  │  Logic  │  │           │  │  Logic  │  │
            │  └─────────┘  │           │  └─────────┘  │           │  └─────────┘  │
            └───────────────┘           └───────────────┘           └───────────────┘
```

### Transport Abstraction Layer

Abstract transport to support multiple backends without changing game logic:

```csharp
/// <summary>
/// Transport-agnostic network interface.
/// Implementations handle the actual network I/O.
/// </summary>
public interface INetworkTransport : IDisposable
{
    // Connection state
    bool IsRunning { get; }
    bool IsHost { get; }

    // Events
    event Action<int> OnClientConnected;      // peerId
    event Action<int> OnClientDisconnected;   // peerId
    event Action<int, byte[]> OnDataReceived; // peerId, data

    // Host operations
    void StartHost(int port);
    void StopHost();

    // Client operations
    void Connect(string address, int port);
    void Disconnect();

    // Data transmission
    void Send(int peerId, byte[] data, DeliveryMethod method);
    void SendToAll(byte[] data, DeliveryMethod method);
    void SendToAllExcept(int excludePeerId, byte[] data, DeliveryMethod method);

    // Must be called each frame to process incoming data
    void Poll();
}

public enum DeliveryMethod
{
    Unreliable,           // Fire and forget (UDP)
    ReliableUnordered,    // Guaranteed delivery, any order
    ReliableOrdered       // Guaranteed delivery, in order (commands)
}
```

### Transport Implementations

```
INetworkTransport
├── DirectTransport      (Development/LAN - Unity Transport Package)
└── SteamTransport       (Release - Steamworks.NET / SteamNetworkingSockets)
```

#### DirectTransport (Development)

Uses **Unity Transport Package** for direct IP:port connections.

```csharp
public class DirectTransport : INetworkTransport
{
    private NetworkDriver driver;
    private NativeList<NetworkConnection> connections;

    // For development: players connect via IP address
    // Requires port forwarding for internet play

    public void StartHost(int port)
    {
        var endpoint = NetworkEndpoint.AnyIpv4.WithPort((ushort)port);
        driver.Bind(endpoint);
        driver.Listen();
    }

    public void Connect(string address, int port)
    {
        var endpoint = NetworkEndpoint.Parse(address, (ushort)port);
        driver.Connect(endpoint);
    }
}
```

**Pros:**
- Simple, no external dependencies beyond Unity
- Burst-compatible (NativeArray-based)
- Good for LAN and development
- No account system required

**Cons:**
- Requires port forwarding for internet play
- No NAT punchthrough

#### SteamTransport (Release)

Uses **Steamworks.NET** with `SteamNetworkingSockets` for Steam-based connections.

```csharp
public class SteamTransport : INetworkTransport
{
    // Players connect via Steam ID, not IP
    // Steam handles NAT punchthrough via relay servers
    // Integrates with Steam lobbies for session discovery

    public void Connect(string steamId, int port)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(new CSteamID(ulong.Parse(steamId)));

        connection = SteamNetworkingSockets.ConnectP2P(
            ref identity,
            port,
            0,
            null
        );
    }

    // Steam lobby integration for "Join Game" via friends list
    public void CreateLobby(int maxPlayers)
    {
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
    }
}
```

**Pros:**
- Automatic NAT punchthrough (Steam relay)
- No port forwarding needed
- Steam friend integration (invite, join via profile)
- Steam authentication (anti-cheat foundation)
- Free for Steam developers

**Cons:**
- Requires Steam running
- Steam developer account needed
- Players must own game on Steam

### NetworkManager Integration

The `NetworkManager` sits between game logic and transport:

```csharp
public class NetworkManager : IDisposable
{
    private INetworkTransport transport;
    private CommandProcessor commandProcessor;
    private CommandSerializer commandSerializer;

    // Peer tracking
    private Dictionary<int, NetworkPeer> peers = new();
    private int localPeerId;

    public bool IsHost => transport?.IsHost ?? false;
    public bool IsConnected => transport?.IsRunning ?? false;

    public void Initialize(INetworkTransport transport)
    {
        this.transport = transport;
        transport.OnClientConnected += HandleClientConnected;
        transport.OnClientDisconnected += HandleClientDisconnected;
        transport.OnDataReceived += HandleDataReceived;
    }

    // Host: receive command from client, validate, broadcast to all
    private void HandleDataReceived(int peerId, byte[] data)
    {
        var messageType = (NetworkMessageType)data[0];

        switch (messageType)
        {
            case NetworkMessageType.CommandBatch:
                HandleCommandBatch(peerId, data);
                break;
            case NetworkMessageType.StateSync:
                HandleStateSync(data);
                break;
            case NetworkMessageType.Heartbeat:
                HandleHeartbeat(peerId);
                break;
        }
    }

    private void HandleCommandBatch(int peerId, byte[] data)
    {
        var commands = commandSerializer.DeserializeBatch(data.AsSpan(1));

        if (IsHost)
        {
            // Validate and execute on authoritative state
            foreach (var cmd in commands)
            {
                if (cmd.Validate(gameState))
                {
                    commandProcessor.Execute(cmd);
                    // Broadcast confirmed command to all clients
                    BroadcastCommand(cmd);
                }
            }
        }
        else
        {
            // Client: apply confirmed commands from host
            foreach (var cmd in commands)
            {
                commandProcessor.Execute(cmd);
            }
        }
    }

    // Client: send local command to host
    public void SendCommand(INetworkCommand command)
    {
        var data = commandSerializer.Serialize(command);

        if (IsHost)
        {
            // Execute locally and broadcast
            commandProcessor.Execute(command);
            transport.SendToAll(data, DeliveryMethod.ReliableOrdered);
        }
        else
        {
            // Send to host for validation
            transport.Send(hostPeerId, data, DeliveryMethod.ReliableOrdered);
            // Optionally: add to prediction buffer
            commandBuffer.AddPredicted(command);
        }
    }
}
```

### Network Message Protocol

```csharp
public enum NetworkMessageType : byte
{
    // Connection
    Handshake = 0x01,
    HandshakeResponse = 0x02,
    Disconnect = 0x03,
    Heartbeat = 0x04,

    // Game state
    CommandBatch = 0x10,        // Batch of commands (uses existing CommandSerializer)
    StateSync = 0x11,           // Full state for late joiners
    StateDelta = 0x12,          // Differential state update

    // Session management
    PlayerJoined = 0x20,
    PlayerLeft = 0x21,
    GameSpeedChange = 0x22,
    PauseRequest = 0x23,

    // Synchronization
    ChecksumRequest = 0x30,     // Request state checksum
    ChecksumResponse = 0x31,    // Return checksum for desync detection
    DesyncRecovery = 0x32       // Full state resync after desync
}

// Message header (all messages start with this)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NetworkMessageHeader
{
    public NetworkMessageType Type;  // 1 byte
    public uint Tick;                // 4 bytes - game tick this message relates to
    public ushort PayloadSize;       // 2 bytes
    // Total: 7 bytes header
}
```

### Late Joiner Synchronization

When a player joins mid-game, they need the full state:

```csharp
public class LateJoinHandler
{
    public void HandleNewClient(int peerId)
    {
        // 1. Pause command acceptance from new client
        peers[peerId].State = PeerState.Synchronizing;

        // 2. Serialize full game state
        var stateData = gameState.SerializeFull();

        // 3. Send in chunks (state may be large)
        const int CHUNK_SIZE = 32 * 1024; // 32KB chunks
        for (int i = 0; i < stateData.Length; i += CHUNK_SIZE)
        {
            var chunk = stateData.AsSpan(i, Math.Min(CHUNK_SIZE, stateData.Length - i));
            SendStateChunk(peerId, i, chunk);
        }

        // 4. Send current tick number
        SendSyncComplete(peerId, currentTick);

        // 5. Client loads state, begins accepting commands
        peers[peerId].State = PeerState.Connected;
    }
}
```

### Desync Detection & Automatic Recovery

**Philosophy:** Unlike Paradox games that treat desyncs as fatal errors requiring a rehost, we expect desyncs to happen occasionally and recover automatically. This is a key differentiator.

#### Why Desyncs Happen

Even with deterministic design, desyncs can occur:
- Uninitialized memory in a new struct field
- A `Dictionary<>` iteration sneaking into gameplay code
- Floating point math in a forgotten calculation
- Race condition in a Burst job
- Bug in command serialization (different state after save/load round-trip)

**The goal isn't to prevent all desyncs (impossible) but to detect and recover seamlessly.**

#### Detection: Periodic Checksum Verification

```csharp
public class DesyncDetector
{
    // Check every N ticks (balance between responsiveness and bandwidth)
    private const int CHECKSUM_INTERVAL_TICKS = 60;  // ~1 second at 60 ticks/sec

    // Track recent checksums for debugging
    private RingBuffer<(uint tick, uint checksum)> checksumHistory = new(256);

    public void OnTick(uint currentTick)
    {
        if (currentTick % CHECKSUM_INTERVAL_TICKS != 0)
            return;

        uint checksum = gameState.ComputeChecksum();
        checksumHistory.Add((currentTick, checksum));

        if (networkManager.IsHost)
        {
            // Host stores authoritative checksum
            authoritativeChecksums[currentTick] = checksum;

            // Broadcast to clients for comparison
            BroadcastChecksum(currentTick, checksum);
        }
        else
        {
            // Client sends checksum to host
            SendChecksumToHost(currentTick, checksum);
        }
    }

    // Host receives client checksum
    public void OnClientChecksum(int peerId, uint tick, uint clientChecksum)
    {
        if (!authoritativeChecksums.TryGetValue(tick, out uint hostChecksum))
            return;  // Too old, already pruned

        if (clientChecksum != hostChecksum)
        {
            Log.Warning($"Desync detected! Peer {peerId} at tick {tick}. " +
                        $"Host: {hostChecksum:X8}, Client: {clientChecksum:X8}");

            // Trigger recovery
            desyncRecovery.RecoverClient(peerId, tick);
        }
    }

    // Client receives host checksum
    public void OnHostChecksum(uint tick, uint hostChecksum)
    {
        var localEntry = checksumHistory.Find(e => e.tick == tick);
        if (localEntry == default)
            return;  // We don't have this tick anymore

        if (localEntry.checksum != hostChecksum)
        {
            Log.Warning($"Local desync detected at tick {tick}. " +
                        $"Host: {hostChecksum:X8}, Local: {localEntry.checksum:X8}");

            // Request recovery from host
            RequestStateResync();
        }
    }
}
```

#### Recovery: Automatic State Resync

Reuses the same mechanism as late-joiner sync:

```csharp
public class DesyncRecovery
{
    public event Action OnDesyncDetected;      // For UI notification
    public event Action OnResyncComplete;      // For UI notification

    // Host-side: resync a desynced client
    public void RecoverClient(int peerId, uint desyncTick)
    {
        var peer = peers[peerId];

        // 1. Mark client as resyncing (pause their input temporarily)
        peer.State = PeerState.Resyncing;
        SendResyncStarting(peerId);

        // 2. Use existing late-join infrastructure
        lateJoinHandler.HandleNewClient(peerId);

        // 3. Log for debugging (helps identify patterns)
        LogDesyncEvent(peerId, desyncTick);
    }

    // Client-side: receive and apply resynced state
    public void OnResyncStateReceived(byte[] stateData, uint hostTick)
    {
        OnDesyncDetected?.Invoke();  // Show "Resyncing..." UI

        // 1. Clear local state
        gameState.Clear();

        // 2. Load authoritative state from host
        gameState.DeserializeFull(stateData);

        // 3. Update all visuals
        renderState.ForceFullUpdate(gameState);

        // 4. Resume normal operation
        currentTick = hostTick;

        OnResyncComplete?.Invoke();  // Hide "Resyncing..." UI

        Log.Info($"Resync complete. Now at tick {hostTick}");
    }
}
```

#### Player Experience

```
┌─────────────────────────────────────────────────────────────────┐
│                     DESYNC RECOVERY TIMELINE                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Normal play     Desync          Recovery        Back to normal │
│  ───────────────►│◄─────────────►│◄─────────────►───────────── │
│                  │               │               │              │
│  Players         │ Brief pause   │ "Resyncing"   │ Game         │
│  playing         │ (< 100ms)     │ text shown    │ continues    │
│                  │               │ (1-3 sec)     │              │
│                                                                 │
│  Total disruption: 1-3 seconds (vs Paradox: 5+ minutes rehost) │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**UI Treatment:**
```csharp
public class DesyncUI : MonoBehaviour
{
    [SerializeField] private GameObject resyncingPanel;
    [SerializeField] private TMP_Text resyncingText;

    void Start()
    {
        desyncRecovery.OnDesyncDetected += () => {
            resyncingPanel.SetActive(true);
            resyncingText.text = "Resyncing...";
        };

        desyncRecovery.OnResyncComplete += () => {
            resyncingPanel.SetActive(false);
        };
    }
}
```

**What players see:**
- Small, non-intrusive notification: "Resyncing..." (not a scary error dialog)
- Game pauses briefly (1-3 seconds)
- Game continues normally
- No rehost, no lobby, no lost progress

#### Debugging: Finding Desync Causes

In development builds, capture detailed state for comparison:

```csharp
public class DesyncDebugger
{
    [Conditional("DEBUG")]
    public void OnDesyncDetected(uint tick, uint localChecksum, uint hostChecksum)
    {
        // Dump full state to file for comparison
        var stateDump = new DesyncDump {
            Tick = tick,
            LocalChecksum = localChecksum,
            HostChecksum = hostChecksum,
            ProvinceStates = gameState.Provinces.ToArray(),
            CountryStates = gameState.Countries.ToArray(),
            UnitStates = gameState.Units.ToArray(),
            // Add other relevant state...
        };

        string path = $"Logs/desync_{tick}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        File.WriteAllText(path, JsonUtility.ToJson(stateDump, prettyPrint: true));

        Log.Error($"Desync dump written to {path}");
    }

    // Tool to compare two desync dumps
    public static void CompareDesyncDumps(string hostDump, string clientDump)
    {
        var host = JsonUtility.FromJson<DesyncDump>(File.ReadAllText(hostDump));
        var client = JsonUtility.FromJson<DesyncDump>(File.ReadAllText(clientDump));

        // Find first difference
        for (int i = 0; i < host.ProvinceStates.Length; i++)
        {
            if (!host.ProvinceStates[i].Equals(client.ProvinceStates[i]))
            {
                Log.Error($"Province {i} differs: Host={host.ProvinceStates[i]}, Client={client.ProvinceStates[i]}");
                return;
            }
        }
        // Continue for other state...
    }
}
```

#### Checksum Implementation

Fast, deterministic checksum using existing data layout:

```csharp
public partial class GameState
{
    public uint ComputeChecksum()
    {
        uint hash = 2166136261;  // FNV-1a offset basis

        // Province state (already contiguous NativeArray)
        hash = HashNativeArray(provinces, hash);

        // Country state
        hash = HashNativeArray(countries, hash);

        // Unit state
        hash = HashNativeArray(units, hash);

        // Current tick (ensures time sync)
        hash = HashValue(currentTick, hash);

        return hash;
    }

    private static uint HashNativeArray<T>(NativeArray<T> array, uint hash)
        where T : unmanaged
    {
        var bytes = array.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= 16777619;  // FNV-1a prime
        }
        return hash;
    }

    private static uint HashValue<T>(T value, uint hash) where T : unmanaged
    {
        byte* ptr = (byte*)&value;
        for (int i = 0; i < sizeof(T); i++)
        {
            hash ^= ptr[i];
            hash *= 16777619;
        }
        return hash;
    }
}
```

#### Comparison: Archon vs Paradox

| Aspect | Paradox Games | Archon Engine |
|--------|---------------|---------------|
| Desync detection | Manual (player notices) or late | Automatic every ~1 second |
| Recovery method | Save, quit, rehost, rejoin | Automatic state resync |
| Player disruption | 5-10 minutes, kills momentum | 1-3 seconds, barely noticeable |
| Data for debugging | "Checksum mismatch" (useless) | Full state dumps with diff tool |
| Philosophy | "Shouldn't happen" | "Will happen, handle gracefully" |

This approach turns a game-breaking issue into a minor hiccup.

### Implementation Phases

#### Phase 1: Core Transport (Development)
- [ ] Implement `INetworkTransport` interface
- [ ] Implement `DirectTransport` using Unity Transport Package
- [ ] Basic `NetworkManager` with host/client modes
- [ ] Wire up to existing `CommandSerializer`
- [ ] Simple lobby UI (host game / join by IP)

#### Phase 2: Game Integration
- [ ] Player assignment (which client controls which country)
- [ ] Game speed synchronization (host controls speed)
- [ ] Pause/resume synchronization
- [ ] Late joiner state sync
- [ ] `GameState.ComputeChecksum()` implementation
- [ ] `GameState.SerializeFull()` / `DeserializeFull()` for state sync

#### Phase 2.5: Desync Detection & Recovery (Key Differentiator)
- [ ] Periodic checksum broadcasting (every N ticks)
- [ ] `DesyncDetector` - checksum comparison logic
- [ ] `DesyncRecovery` - automatic state resync
- [ ] Resync UI ("Resyncing..." notification)
- [ ] Debug: State dump on desync for comparison
- [ ] Debug: Diff tool to find first divergence

#### Phase 3: Steam Integration
- [ ] Implement `SteamTransport` using Steamworks.NET
- [ ] Steam lobby creation/discovery
- [ ] Join via Steam friends list
- [ ] Steam authentication

#### Phase 4: Polish
- [ ] Reconnection handling
- [ ] Host migration (optional, complex)
- [ ] Network statistics UI (ping, bandwidth)
- [ ] Improved desync recovery

### File Structure

```
Scripts/
└── Core/
    └── Network/
        ├── INetworkTransport.cs        # Transport interface
        ├── DirectTransport.cs          # Unity Transport implementation
        ├── SteamTransport.cs           # Steamworks implementation (Phase 3)
        ├── NetworkManager.cs           # High-level network coordinator
        ├── NetworkPeer.cs              # Per-connection state
        ├── NetworkMessages.cs          # Message types and headers
        ├── LateJoinHandler.cs          # State sync for joining players
        ├── DesyncDetector.cs           # Periodic checksum verification
        ├── DesyncRecovery.cs           # Automatic state resync
        └── DesyncDebugger.cs           # Debug tools for finding desync causes
```

---

## Related Documents

- **[Save/Load Architecture](save-load-architecture.md)** - Command pattern shared between multiplayer and save systems
- **[Performance Architecture](performance-architecture-guide.md)** - Hot/cold data separation for network sync
- **[Time System Architecture](time-system-architecture.md)** - Update frequencies aligned with network ticks
- **[Master Architecture](master-architecture-document.md)** - Overview of dual-layer architecture