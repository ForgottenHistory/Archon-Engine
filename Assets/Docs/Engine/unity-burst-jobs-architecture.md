# Unity Burst Compiler & Job System Architecture Guide

## Executive Summary
**What**: Unity's high-performance compilation and multithreading system for CPU-intensive operations  
**Why**: Can achieve 10-100x performance improvements over regular C# code  
**When**: Use for computationally intensive, parallelizable work (pathfinding, physics, procedural generation)  
**Key Insight**: Write restricted C# that compiles to SIMD-optimized native code running on multiple cores

## Overview: The Performance Trinity

Unity's high-performance stack consists of three interconnected systems:

### 1. **Job System** - Safe Multithreading
- Distributes work across CPU cores
- Prevents race conditions through safety system
- Manages dependencies between jobs

### 2. **Burst Compiler** - Native Code Generation
- Compiles C# to highly optimized machine code
- Uses LLVM for platform-specific optimizations
- Generates SIMD instructions automatically

### 3. **Native Collections** - Unmanaged Memory
- Thread-safe data structures
- Zero garbage collection pressure
- Direct memory access for jobs

## When to Use Burst & Jobs

### Perfect Use Cases
```
✅ Pathfinding algorithms (A* across thousands of nodes)
✅ Physics simulations (particles, fluid dynamics)
✅ Procedural generation (terrain, vegetation placement)
✅ Mesh manipulation (deformation, LOD generation)
✅ Image/texture processing
✅ AI calculations (vision cones, influence maps)
✅ Large-scale data transformation
```

### When NOT to Use
```
❌ Simple operations that complete in <1ms
❌ Code that needs managed objects (classes, strings)
❌ Unity API calls (GameObject manipulation)
❌ File I/O operations
❌ Network operations
❌ Infrequent calculations (one-time setup)
```

## Core Concepts

### The Burst Subset of C# (HPC#)

Burst only compiles a subset of C# called "High Performance C#":

**Allowed:**
- Structs (value types)
- Primitive types (int, float, bool)
- Unity.Mathematics types (float3, int4, etc.)
- Pointers and unsafe code
- Static methods
- NativeArray and other Native collections

**NOT Allowed:**
- Classes (reference types)
- Managed arrays or strings
- try/catch/finally (exceptions)
- Delegates (except BurstCompiler.CompileFunctionPointer)
- Virtual methods or interfaces
- Most Unity API calls

### Job Types

```csharp
// 1. IJob - Single threaded work
[BurstCompile]
struct SimpleJob : IJob {
    public void Execute() {
        // Runs once on a worker thread
    }
}

// 2. IJobParallelFor - Parallel array processing
[BurstCompile]
struct ParallelJob : IJobParallelFor {
    public void Execute(int index) {
        // Called once per index, in parallel
    }
}

// 3. IJobParallelForTransform - Transform manipulation
[BurstCompile]
struct TransformJob : IJobParallelForTransform {
    public void Execute(int index, TransformAccess transform) {
        // Modify transforms in parallel
    }
}

// 4. IJobEntityBatch - ECS specific (if using DOTS)
```

## Native Collections

### Core Native Collections

```csharp
// 1. NativeArray<T> - Fixed size array
NativeArray<float> data = new NativeArray<float>(1000, Allocator.TempJob);

// 2. NativeList<T> - Resizable list (requires Collections package)
NativeList<int> list = new NativeList<int>(100, Allocator.Persistent);

// 3. NativeHashMap<K,V> - Dictionary equivalent
NativeHashMap<int, float> map = new NativeHashMap<int, float>(50, Allocator.Temp);

// 4. NativeMultiHashMap<K,V> - Multiple values per key
NativeMultiHashMap<int, float> multiMap = new NativeMultiHashMap<int, float>(50, Allocator.Temp);

// 5. NativeQueue<T> - FIFO queue
NativeQueue<int> queue = new NativeQueue<int>(Allocator.TempJob);
```

### Allocator Types

```csharp
public enum Allocator {
    Invalid = 0,      // Don't use
    None = 1,         // Don't use
    Temp = 2,         // Very fast, <1 frame lifetime, auto-disposed
    TempJob = 3,      // Fast, <4 frames lifetime, must dispose
    Persistent = 4    // Slow allocation, permanent, must dispose
}

// Usage patterns:
// Temp - for immediate use within a method
NativeArray<int> temp = new NativeArray<int>(100, Allocator.Temp);

// TempJob - for passing to jobs
NativeArray<float> jobData = new NativeArray<float>(1000, Allocator.TempJob);

// Persistent - for long-lived data
NativeList<Vector3> worldData = new NativeList<Vector3>(10000, Allocator.Persistent);
```

### Safety Attributes

```csharp
[BurstCompile]
struct SafeJob : IJobParallelFor {
    [ReadOnly]  // Can read but not write
    public NativeArray<float> input;
    
    [WriteOnly]  // Can write but not read (better performance)
    public NativeArray<float> output;
    
    [NativeDisableParallelForRestriction]  // Can write to any index (dangerous!)
    public NativeArray<float> shared;
    
    [NativeDisableContainerSafetyRestriction]  // Disable all safety (very dangerous!)
    public NativeArray<float> unsafe;
    
    public void Execute(int index) {
        output[index] = input[index] * 2f;
    }
}
```

## Basic Implementation Pattern

### Complete Example: Parallel Processing

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class BurstExample : MonoBehaviour {
    
    [BurstCompile(CompileSynchronously = true)]
    struct WaveCalculationJob : IJobParallelFor {
        [ReadOnly] public float time;
        [ReadOnly] public float waveSpeed;
        [ReadOnly] public float waveHeight;
        
        public NativeArray<float3> positions;
        
        public void Execute(int index) {
            float3 pos = positions[index];
            pos.y = math.sin(pos.x * waveSpeed + time) * waveHeight;
            positions[index] = pos;
        }
    }
    
    void Update() {
        int vertexCount = 10000;
        
        // Allocate native memory
        var positions = new NativeArray<float3>(vertexCount, Allocator.TempJob);
        
        // Initialize data
        for (int i = 0; i < vertexCount; i++) {
            positions[i] = new float3(i * 0.1f, 0, 0);
        }
        
        // Create and schedule job
        var job = new WaveCalculationJob {
            time = Time.time,
            waveSpeed = 2f,
            waveHeight = 1f,
            positions = positions
        };
        
        // Schedule with batch size (vertices per job)
        JobHandle handle = job.Schedule(vertexCount, 64);
        
        // Wait for completion
        handle.Complete();
        
        // Use results
        for (int i = 0; i < vertexCount; i++) {
            Debug.DrawRay(positions[i], Vector3.up * 0.1f);
        }
        
        // Cleanup
        positions.Dispose();
    }
}
```

## Advanced Patterns

### Job Dependencies

```csharp
// Chain jobs together
JobHandle firstHandle = firstJob.Schedule();
JobHandle secondHandle = secondJob.Schedule(firstHandle);  // Waits for first
JobHandle finalHandle = JobHandle.CombineDependencies(secondHandle, thirdHandle);
finalHandle.Complete();
```

### Burst Function Pointers

```csharp
[BurstCompile]
public static class BurstMath {
    // Delegate declaration
    public delegate float MathOperation(float a, float b);
    
    // Burst-compiled function
    [BurstCompile]
    public static float Add(float a, float b) => a + b;
    
    // Get function pointer
    public static FunctionPointer<MathOperation> GetAddPointer() {
        return BurstCompiler.CompileFunctionPointer<MathOperation>(Add);
    }
}

// Usage
var addPtr = BurstMath.GetAddPointer();
float result = addPtr.Invoke(5f, 3f);
```

### Using Unity.Mathematics

```csharp
using Unity.Mathematics;

[BurstCompile]
struct MathJob : IJob {
    public NativeArray<float3> positions;
    public NativeArray<quaternion> rotations;
    
    public void Execute() {
        for (int i = 0; i < positions.Length; i++) {
            // Unity.Mathematics uses lowercase types
            float3 pos = positions[i];
            quaternion rot = rotations[i];
            
            // SIMD-optimized operations
            pos = math.normalize(pos);
            pos = math.rotate(rot, pos);
            
            // Swizzling like shaders
            pos.xz = pos.zx;
            
            positions[i] = pos;
        }
    }
}
```

## Common Pitfalls & Solutions

### 1. Accessing Managed Objects

```csharp
// ❌ WRONG - Will not compile with Burst
[BurstCompile]
struct BadJob : IJob {
    public string text;  // Reference type!
    public List<int> list;  // Managed collection!
    
    public void Execute() {
        Debug.Log(text);  // Unity API call!
    }
}

// ✅ CORRECT - Burst compatible
[BurstCompile]
struct GoodJob : IJob {
    public NativeArray<int> data;
    public float value;
    
    public void Execute() {
        for (int i = 0; i < data.Length; i++) {
            data[i] = (int)(data[i] * value);
        }
    }
}
```

### 2. Container Aliasing

```csharp
// ❌ WRONG - Aliasing breaks auto-vectorization
[BurstCompile]
struct AliasingJob : IJob {
    public NativeArray<float> data;
    
    public void Execute() {
        var copy = data;  // Creates alias!
        for (int i = 0; i < copy.Length; i++) {
            copy[i] *= 2f;
        }
    }
}

// ✅ CORRECT - No aliasing
[BurstCompile]
struct NoAliasingJob : IJob {
    public NativeArray<float> data;
    
    public void Execute() {
        for (int i = 0; i < data.Length; i++) {
            data[i] *= 2f;  // Direct access
        }
    }
}
```

### 3. Memory Leaks

```csharp
// ❌ WRONG - Memory leak!
void LeakyMethod() {
    var array = new NativeArray<int>(1000, Allocator.TempJob);
    // Forgot to Dispose!
}

// ✅ CORRECT - Proper disposal
void ProperMethod() {
    var array = new NativeArray<int>(1000, Allocator.TempJob);
    try {
        // Use array
    } finally {
        array.Dispose();
    }
}

// ✅ BETTER - Using statement
void BetterMethod() {
    using (var array = new NativeArray<int>(1000, Allocator.TempJob)) {
        // Array automatically disposed
    }
}
```

### 4. Race Conditions

```csharp
// ❌ WRONG - Race condition!
[BurstCompile]
struct RaceJob : IJobParallelFor {
    public NativeArray<int> shared;
    
    public void Execute(int index) {
        shared[0] += 1;  // Multiple threads writing!
    }
}

// ✅ CORRECT - Use atomic operations
[BurstCompile]
struct AtomicJob : IJobParallelFor {
    [NativeDisableParallelForRestriction]
    public NativeArray<int> counter;
    
    public void Execute(int index) {
        // Thread-safe increment
        Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.AtomicIncrement(ref counter[0]);
    }
}
```

## Compilation & Safety Settings

### Burst Menu Settings

```
Jobs > Burst >:
├── Enable Compilation: Turn Burst on/off
├── Enable Safety Checks: Bounds checking (slower but safer)
│   ├── Off: No checks (fastest, dangerous)
│   ├── On: Normal checks (recommended for development)
│   └── Force On: Override all DisableSafetyChecks
├── Synchronous Compilation: Compile immediately (causes hitches)
├── Show Timings: Log compilation times
└── Open Inspector: View generated assembly
```

### BurstCompile Attributes

```csharp
[BurstCompile(
    // Compilation options
    CompileSynchronously = true,      // Don't compile async (causes hitch)
    FloatMode = FloatMode.Fast,       // Fast math (less precise)
    FloatPrecision = FloatPrecision.Low,  // Lower precision (faster)
    DisableSafetyChecks = true,       // No bounds checking (dangerous!)
    OptimizeFor = OptimizeFor.Performance  // Max performance
)]
struct OptimizedJob : IJob {
    public void Execute() { }
}
```

### Debugging Burst Code

```csharp
[BurstCompile]
struct DebugJob : IJob {
    public void Execute() {
        // Only runs in non-Burst (for debugging)
        #if !UNITY_BURST_ENABLED
        Debug.Log("This only runs without Burst");
        #endif
        
        // Conditional compilation
        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckValue(int value) {
            if (value < 0) throw new System.ArgumentException();
        }
    }
}
```

## Performance Tips

### 1. Batch Size Optimization
```csharp
// Too small = overhead, too large = poor load balancing
int batchSize = math.max(1, dataCount / (SystemInfo.processorCount * 4));
handle = job.Schedule(dataCount, batchSize);
```

### 2. Memory Access Patterns
```csharp
// ✅ GOOD - Sequential access (cache friendly)
for (int i = 0; i < data.Length; i++) {
    data[i] *= 2f;
}

// ❌ BAD - Random access (cache misses)
for (int i = 0; i < data.Length; i++) {
    data[randomIndices[i]] *= 2f;
}
```

### 3. Avoid Branches
```csharp
// ❌ Branching (breaks SIMD)
float result = value > 0 ? value * 2 : value * 3;

// ✅ Branchless (SIMD friendly)
float result = math.select(value * 3, value * 2, value > 0);
```

### 4. Use Unity.Mathematics
```csharp
// ❌ Regular math (not optimized)
Vector3 v = Vector3.Normalize(input);

// ✅ Unity.Mathematics (SIMD optimized)
float3 v = math.normalize(input);
```

## Integration with Grand Strategy Game

### Province Update System
```csharp
[BurstCompile]
public struct ProvinceUpdateJob : IJobParallelFor {
    [ReadOnly] public NativeArray<byte> owners;
    [ReadOnly] public NativeArray<float> baseTax;
    [ReadOnly] public float taxEfficiency;
    
    [WriteOnly] public NativeArray<float> income;
    
    public void Execute(int index) {
        income[index] = baseTax[index] * taxEfficiency;
    }
}

// Schedule for 10,000 provinces
var job = new ProvinceUpdateJob { /* ... */ };
var handle = job.Schedule(10000, 128);
```

### Pathfinding System
```csharp
[BurstCompile]
public struct PathfindingJob : IJob {
    [ReadOnly] public NativeArray<int> adjacency;
    [ReadOnly] public int start;
    [ReadOnly] public int goal;
    
    public NativeList<int> path;
    
    public void Execute() {
        // A* implementation using native collections
        var openSet = new NativeHeap<int>(Allocator.Temp);
        var cameFrom = new NativeHashMap<int, int>(1000, Allocator.Temp);
        
        // Pathfinding logic...
        
        openSet.Dispose();
        cameFrom.Dispose();
    }
}
```

## Common Errors & Solutions

### Error: "Not Blittable"
**Cause**: Using types that can't be copied as raw bytes  
**Solution**: Only use value types, no reference types

### Error: "BurstCompiler failed"
**Cause**: Using unsupported C# features  
**Solution**: Check for managed types, exceptions, delegates

### Error: "Safety system validation failed"
**Cause**: Concurrent access to same container  
**Solution**: Use [ReadOnly], proper job dependencies

### Error: "Memory leak detected"
**Cause**: Not disposing NativeContainers  
**Solution**: Always Dispose() or use using statement

## Best Practices Summary

1. **Profile Before Optimizing** - Ensure Burst is solving a real problem
2. **Start Simple** - Get basic job working before optimizing
3. **Dispose Everything** - Memory leaks crash builds
4. **Use Safety Checks** - During development, disable for release
5. **Batch Appropriately** - Balance between overhead and parallelism
6. **Cache Friendly Code** - Sequential access patterns
7. **Avoid Aliasing** - Direct access for auto-vectorization
8. **Unity.Mathematics** - Always prefer over Vector3/Quaternion
9. **Test Performance** - Burst isn't always faster for small data
10. **Read Assembly** - Use Burst Inspector to verify optimizations

## Conclusion

The Burst compiler and Job System are powerful tools for achieving massive performance improvements in Unity, but they require careful adherence to restrictions and patterns. When used correctly for appropriate workloads (large-scale parallel computations), they can provide 10-100x speedups. For your grand strategy game processing 10,000+ provinces, this system is essential for maintaining 200+ FPS throughout the game's lifecycle.