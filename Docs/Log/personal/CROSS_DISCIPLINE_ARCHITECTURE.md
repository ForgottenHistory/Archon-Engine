# Cross-Discipline Architecture in Hegemon

**Date:** 2026-01-08
**Context:** Reflection on the advanced techniques embedded in this project

This project applies techniques from four distinct disciplines that rarely appear together in game development: database design, systems programming, graphics research, and competitive netcode. This document captures the cross-pollination of ideas.

---

## Database Design Patterns

A game with 10k provinces and 300 countries **is** a database. The same patterns that make PostgreSQL fast make games fast.

### Write-Ahead Log (Command Pattern)
```
Database: All writes go to WAL first → replay for recovery
Hegemon:  All changes go through Commands → replay for networking/saves
```
Every state change is a serializable command with checksum. You could replay the entire game from the command log. This is how Paradox does deterministic multiplayer.

### MVCC (Double-Buffer)
```
Database: Readers see snapshot, writers work on new version, atomic swap
Hegemon:  UI reads buffer A, simulation writes buffer B, O(1) pointer swap
```
Multi-Version Concurrency Control without the complexity. Readers never block writers. Victoria 3 uses locks instead → their "waiting" profiler bars.

### Materialized View Invalidation (Event-Driven Cache)
```
Database: View cached until underlying table changes → invalidate → lazy recompute
Hegemon:  neighborCache valid until ProvinceOwnershipChangedEvent → invalidate → recompute on next query
```
Don't recompute derived data every query - cache it, subscribe to source changes, invalidate surgically. This took AI evaluation from 10 FPS to 60+ FPS.

### Covering Index (Bidirectional Mapping)
```
Database: Index on both FK directions for fast lookups either way
Hegemon:  province→owner (ProvinceState) + owner→provinces (provincesByOwner) in same system
```
Single source of truth owns both directions. Never let them desync.

### Hot/Cold Storage Tiering
```
Database: Hot data in RAM/SSD, cold data in archive storage
Hegemon:  ProvinceState (8 bytes, every frame) vs ProvinceColdData (names, history, on-demand)
```
Separate by access frequency, not importance. Cache lines matter.

### Sparse Indexes (NativeParallelMultiHashMap)
```
Database: Only index rows that exist, not all possible rows
Hegemon:  Only store buildings that exist, not 10k provinces × 50 building types
```
Memory scales with actual data, not theoretical maximum. Prevents HOI4's equipment memory disaster.

### Transaction Log Rotation (Ring Buffers)
```
Database: Old log entries archived/compressed, bounded storage
Hegemon:  Province history in ring buffer, old events compressed, never unbounded growth
```
Late-game performance doesn't degrade because history doesn't grow forever.

---

## Systems Programming

Treat the computer as a machine, not an abstraction. Memory has layout. Caches have lines. Allocators have costs. CPUs have pipelines.

### Cache Line Awareness
```
CPU fetches 64 bytes at a time, not individual bytes
ProvinceState = 8 bytes → 8 provinces per cache line
Access pattern: sequential iteration = prefetcher happy
Random access = cache miss every time = 100x slower
```
This is why `NativeArray<ProvinceState>` beats `Dictionary<int, Province>`. Contiguous memory, predictable access.

### Zero-Allocation Runtime
```
Problem:  malloc() takes a global lock
          Multiple threads allocating = serialized execution
          HOI4's equipment system = parallelism destroyed

Solution: Pre-allocate at startup (Allocator.Persistent)
          Clear and reuse during gameplay (buffer.Clear())
          Never new/malloc in hot path
```
One allocation in a Burst job = falls back to slow path. We pre-allocate everything: NativeLists, HashMaps, buffers.

### Pointer Swap vs Memcpy
```
Memcpy 80KB: ~0.1ms (touch every byte)
Pointer swap: ~0.000001ms (flip one integer)

Double-buffer: simulation writes to A, UI reads from B
After tick: swap pointers, not data
```
This is why Victoria 3's lock-based approach loses. They synchronize; we isolate.

### Blittable Types for Native Code
```
Blittable:     struct with only primitive fields (int, float, ushort)
Non-blittable: anything with managed references (string, class, array)

Burst can only compile blittable types to SIMD
FixedPoint64 = struct { long RawValue } → blittable
ProvinceState = struct { ushort, ushort, ushort, ushort } → blittable
```
The entire hot path is blittable. No GC, no managed heap, native code all the way down.

### Allocator Lifetimes
```
Allocator.Temp:        Single frame, auto-disposed, fastest
Allocator.TempJob:     Job lifetime, disposed after job completes
Allocator.Persistent:  Manual lifetime, you dispose it, slowest to allocate

Pattern: Persistent for long-lived buffers, TempJob for job scratch space
```
Wrong allocator = memory leak or use-after-free. Unity's safety system catches this in editor.

### Lock-Free Concurrency
```
Traditional: Lock → Read/Write → Unlock (threads wait)
Double-buffer: No locks, readers/writers never touch same memory

UI thread reads buffer B (yesterday's state)
Simulation writes buffer A (today's state)
Never contend, never wait, never deadlock
```
This is how we get 60 FPS at 10x speed. No synchronization overhead.

### SIMD Auto-Vectorization
```
Burst sees:
  for (int i = 0; i < 1000; i++)
    distances[i] = MAX_DISTANCE;

Compiles to:
  Write 32 bytes at once using AVX2 (8 × 4-byte values)
  Loop runs 32x fewer iterations
```
Burst automatically vectorizes simple loops. Structuring code to enable this = free 4-8x speedup.

### Fixed-Point Determinism
```
IEEE 754 floats: CPU can fuse multiply-add (FMA) or not
                 x87 uses 80-bit intermediates, SSE uses 64-bit
                 Same code, different CPU = different results

FixedPoint64:    Just integer math (add, subtract, shift)
                 Same result on every CPU, every platform, forever
```
Multiplayer determinism requires this. One float operation in simulation = desync.

---

## Graphics Research

The GPU is a massively parallel machine, not a drawing API. Exploit parallelism, avoid divergence, use textures as data structures, minimize draw calls.

### Jump Flood Algorithm (JFA)
```
Problem:  Generate distance field (distance to nearest border for every pixel)
Naive:    For each pixel, search all border pixels → O(n²)
JFA:      O(log n) passes with exponentially decreasing step sizes

Pass 1: Step = 512, each pixel checks 8 neighbors at ±512
Pass 2: Step = 256, check neighbors at ±256
...
Pass 9: Step = 1, final refinement

Result: Every pixel knows distance to nearest border in 9 passes
```
This is a 2006 graphics research paper (Rong & Tan). Used for smooth fragment-shader borders without mesh generation.

### Signed Distance Fields for Borders
```
Traditional: Rasterize border as pixels (jagged, resolution-dependent)
SDF:         Store distance-to-edge, threshold in shader (smooth at any zoom)

float dist = texture(distanceField, uv).r;
float border = smoothstep(threshold - AA, threshold + AA, dist);
```
Resolution-independent borders. Zoom in = still smooth. Same technique as font rendering (SDF fonts).

### Imperator Rome 4-Channel Terrain Blending
```
Their technique (from decompiled shader):
- DetailIndexTexture: RGBA8 storing 4 terrain indices per pixel
- DetailMaskTexture:  RGBA8 storing 4 blend weights per pixel
- Manual bilinear: Sample 4 neighbors, interpolate manually

Why manual bilinear?
- GPU bilinear would interpolate indices (terrain 3 + terrain 7 = terrain 5? No.)
- Sample 4 corners at exact texel centers → blend weights in shader

Result: Watercolor-smooth transitions between terrain types
```
Figured this out from `375_pixel.txt` shader disassembly. Undocumented technique.

### GPU Compute Thread Optimization
```
GPU runs threads in warps/wavefronts (32 or 64 threads)
All threads in warp execute same instruction

Divergent branch:
  if (someCondition) doA(); else doB();
  → Half the warp does A, half waits
  → Then half does B, other half waits
  → 50% efficiency

Solution: Branchless code, use lerp/step instead of if
  float result = lerp(valueA, valueB, step(threshold, input));
```
Border detection compute shader avoids conditionals in inner loop.

### Texture-Based Province Rendering
```
Traditional (Civ): Each province = mesh = draw call
                   10,000 provinces = 10,000 draw calls = dead GPU

Texture-based:     Entire map = 1 quad + textures
                   Province ID texture (R16): pixel → province ID
                   Owner texture (R16): province ID → country ID
                   Palette texture (RGBA): country ID → color

                   1 draw call for entire map, any province count
```
This is the fundamental insight. Everything else builds on this.

### Point Filtering for ID Textures
```
Bilinear filtering: Average nearby pixels for smooth result
                    Province 5 + Province 9 = Province 7???

Point filtering:    Nearest pixel, no interpolation
                    Province ID = exactly what's in texture

Critical: Province ID, Owner ID textures MUST use point filtering
          Color/terrain textures CAN use bilinear
```
One wrong filter mode = entire selection system breaks.

### Indirect Lookup Chains
```
Fragment shader does:
  1. Sample provinceID = ProvinceTexture[uv]           // R16
  2. Sample ownerID = OwnerTexture[provinceID]         // R16
  3. Sample color = PaletteTexture[ownerID]            // RGBA

Three texture samples, complete flexibility
Change owner? Update one pixel in OwnerTexture
Change country color? Update one pixel in PaletteTexture
```
Data-driven rendering. Shader never changes, data updates.

### BorderMask Early-Out Optimization
```
Problem:  Border shader is expensive, but 95% of pixels aren't borders
Solution: R8 BorderMask texture, 1 = might be border, 0 = definitely not

if (BorderMask[uv] == 0)
  return baseColor;  // Early out, skip expensive border math

// Only 5% of pixels reach here
expensiveBorderCalculation();
```
Sparse mask for early rejection. 20x faster than checking every pixel.

### Bézier Curve Border Rendering
```
Pixel borders: Jagged, 1-pixel wide, looks bad zoomed in
Vector curves: Extract border points → fit Bézier curves → render as geometry

Pipeline:
  1. BorderCurveExtractor: Find border pixel chains
  2. RDP simplification: Reduce point count (Douglas-Peucker)
  3. Chaikin smoothing: Subdivide for smooth curves
  4. BorderMeshGenerator: Triangle strip from polyline
  5. Render with configurable thickness/AA
```
Resolution-independent borders that look good at any zoom.

---

## Competitive Netcode

The architecture assumes multiplayer even though it's not implemented. When networking is added, it's a transport layer - not a refactor.

### Lockstep Determinism
```
Problem:  Send game state every frame = massive bandwidth
          10k provinces × 8 bytes × 60 FPS = 4.8 MB/sec per player

Lockstep: Only send player inputs (commands)
          Every client simulates identically
          Bandwidth = just commands = ~1 KB/sec

Requirement: IDENTICAL simulation on every machine
             One float operation = desync = game over
```
This is why FixedPoint64 exists. Starcraft, Age of Empires, Paradox games all use lockstep.

### Command Pattern = Network Messages
```
Local play:   Command.Execute(gameState)
Network play: Serialize(command) → Send → Deserialize → Execute

Every command has:
  - Validate(): Can this action happen?
  - Execute():  Apply to game state
  - GetChecksum(): Hash for desync detection
  - Serialize(): Convert to bytes

Commands ARE the network protocol
```
We didn't build networking, but every state change goes through commands. Adding multiplayer = serialize commands over wire.

### Desync Detection via Checksums
```
After each tick:
  Client A: checksum = HashGameState() → 0xABCD1234
  Client B: checksum = HashGameState() → 0xABCD1234 ✓

If mismatch:
  DESYNC DETECTED
  Options: Resync from host, or debug which system diverged

Command checksums: Verify command itself wasn't corrupted
State checksums:   Verify simulation stayed in sync
```
Paradox games show "checksum mismatch" in multiplayer. Same system.

### Fixed Timestep Simulation
```
Variable timestep: deltaTime = 0.016 or 0.017 or 0.015...
                   Accumulates error, clients diverge

Fixed timestep:    Always exactly 1 tick = 1 game-hour
                   No deltaTime in simulation
                   Frame rate independent

TimeManager ticks in fixed increments, never uses deltaTime for game logic
```
Rendering interpolates between ticks. Simulation is discrete.

### Rollback Architecture (Designed)
```
Problem:  Network latency = 100ms
          Wait for server confirmation = 100ms input lag = unplayable

Client prediction:
  1. Player issues command
  2. Execute IMMEDIATELY locally (predict)
  3. Send to server
  4. Server broadcasts to all clients
  5. If prediction wrong → rollback → replay with correct state

Requires:
  - Command buffer (ring buffer of recent commands) ✓
  - State snapshots (double-buffer pattern) ✓
  - Deterministic replay (command pattern) ✓
```
The architecture supports this. CommandBuffer exists. Implementation = future work.

### Input Delay vs Rollback Tradeoff
```
Lockstep (Paradox): Wait for all players before simulating
                    Latency = slowest player's ping
                    No prediction, no rollback, simpler

Rollback (Fighting games): Predict locally, correct on mismatch
                           Feels responsive, complex to implement
                           Requires state snapshots every frame

Hegemon architecture: Lockstep-ready with rollback potential
                      Command pattern works for both
```
Strategic games typically use lockstep (turn-based feel anyway). Rollback optional.

### Bandwidth Optimization Patterns
```
Delta compression:
  Don't send: "Province 1 owner = 5, Province 2 owner = 5, ..."
  Send:       "Provinces [1,2,3,7,8] changed owner to 5"

Command batching:
  Don't send: Packet per command
  Send:       All commands this tick in one packet

Priority queue:
  Bandwidth limited? Send important commands first
  War declaration > opinion modifier update
```
These patterns are why Paradox games work on slow connections.

### Authoritative Server Model
```
Client:  "I want to move army to province 50"
Server:  Validate(command) → Legal? → Execute → Broadcast
Clients: Receive validated command → Execute locally

Server is authoritative:
  - Prevents cheating (server validates everything)
  - Single source of truth
  - Clients can predict, but server corrects

Command.Validate() exists for this reason
```
Even single-player runs through validation. Multiplayer = same code path.

### Current Multiplayer Readiness
```
Current state:  Single-player only
Multiplayer:    Add networking layer, serialize commands, done

Why it works:
  ✓ All state changes through commands
  ✓ Deterministic simulation (FixedPoint64)
  ✓ Fixed timestep (tick-based)
  ✓ Checksums for desync detection
  ✓ Double-buffer for state snapshots
  ✓ Ring buffer for command history
```

---

## The Synthesis

These four disciplines rarely appear together:
- **Database engineers** think about indexes, caching, and consistency
- **Systems programmers** think about memory layout, allocation, and concurrency
- **Graphics researchers** think about parallelism, shaders, and GPU architecture
- **Netcode engineers** think about determinism, latency, and bandwidth

Most game developers specialize in one, maybe two. This project applies all four because the problem demands it: a Paradox-scale grand strategy game requires database-level data management, systems-level performance, graphics-research rendering, and netcode-ready architecture.

The common thread: **respect the machine**. Understand how CPUs cache memory, how GPUs execute threads, how networks lose packets, how databases maintain consistency. Then apply those insights to game architecture.

The techniques are scattered across academic papers, GDC talks, database textbooks, and decompiled shaders. They're never taught together. But they compose beautifully when the goal is "build something that actually works at scale."

---

*Written: 2026-01-08*
*Context: Reflection on 553 commits, ~95k lines of code
