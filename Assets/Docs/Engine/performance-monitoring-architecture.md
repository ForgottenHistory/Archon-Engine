# Grand Strategy Game - Performance Monitoring Architecture

## Executive Summary
**Goal**: Maintain 200+ FPS with 10,000 provinces throughout entire game lifecycle  
**Approach**: Measure everything, optimize bottlenecks, detect degradation early  
**Key Metrics**: Frame time, memory usage, cache misses, draw calls  
**Result**: Performance never degrades, even after 100+ hours of gameplay

## Core Performance Metrics

### The Key Performance Indicators (KPIs)
```csharp
public class PerformanceKPIs {
    // Frame Performance
    public float CurrentFPS;
    public float AverageFPS;           // Rolling 60-frame average
    public float MinFPS;               // Worst frame in last second
    public float FrameTime;            // Milliseconds per frame
    public float PercentileTime95;     // 95th percentile frame time
    
    // System Performance
    public float CPUTime;              // CPU milliseconds
    public float GPUTime;              // GPU milliseconds
    public float RenderThreadTime;     // Render thread wait
    public float MainThreadTime;       // Main thread time
    
    // Memory Metrics
    public long TotalMemory;           // Total allocated
    public long GCMemory;              // Managed heap size
    public long TextureMemory;         // VRAM usage
    public int GCCollections;          // GCs per minute
    
    // Game-Specific Metrics
    public int ProvincesUpdated;       // Per frame
    public int CommandsProcessed;      // Per frame
    public int PathsCalculated;        // Per frame
    public int AIDecisions;            // Per frame
    public int DrawCalls;              // Render statistics
    public int TriangleCount;          // Geometry rendered
}
```

## Performance Monitoring System

### Real-Time Performance Tracker
```csharp
public class PerformanceMonitor : MonoBehaviour {
    private CircularBuffer<FrameMetrics> frameHistory = new(1000);
    private Dictionary<string, SystemMetrics> systemMetrics = new();
    private PerformanceKPIs currentKPIs = new();
    
    // Profiling markers
    private Dictionary<string, ProfileMarker> markers = new();
    
    void Awake() {
        // Create profiling markers
        markers["Update"] = new ProfileMarker("GameUpdate");
        markers["Render"] = new ProfileMarker("GameRender");
        markers["AI"] = new ProfileMarker("AIUpdate");
        markers["Pathfinding"] = new ProfileMarker("Pathfinding");
        markers["Commands"] = new ProfileMarker("CommandProcessing");
    }
    
    void Update() {
        // Record frame start
        var frameStart = Time.realtimeSinceStartup;
        
        // Measure frame metrics
        var frame = new FrameMetrics {
            frameNumber = Time.frameCount,
            deltaTime = Time.deltaTime,
            timestamp = frameStart
        };
        
        // Update KPIs
        UpdateKPIs(frame);
        
        // Store history
        frameHistory.Add(frame);
        
        // Check for performance issues
        DetectPerformanceIssues(frame);
        
        // Log if requested
        if (ShouldLogThisFrame()) {
            LogPerformanceMetrics();
        }
    }
    
    private void UpdateKPIs(FrameMetrics frame) {
        currentKPIs.CurrentFPS = 1f / frame.deltaTime;
        currentKPIs.AverageFPS = CalculateAverageFPS();
        currentKPIs.FrameTime = frame.deltaTime * 1000f;
        
        // Memory
        currentKPIs.TotalMemory = Profiler.GetTotalAllocatedMemoryLong();
        currentKPIs.GCMemory = GC.GetTotalMemory(false);
        currentKPIs.TextureMemory = Profiler.GetAllocatedMemoryForGraphicsDriver();
        
        // Draw calls
        currentKPIs.DrawCalls = UnityStats.drawCalls;
        currentKPIs.TriangleCount = UnityStats.triangles;
    }
}
```

### System-Specific Profiling
```csharp
public class SystemProfiler {
    private Stopwatch stopwatch = new();
    private Dictionary<string, long> timings = new();
    
    public void BeginSample(string systemName) {
        stopwatch.Restart();
    }
    
    public void EndSample(string systemName) {
        var elapsed = stopwatch.ElapsedTicks;
        
        if (!timings.ContainsKey(systemName)) {
            timings[systemName] = 0;
        }
        
        timings[systemName] += elapsed;
    }
    
    // Use with using statement for automatic measurement
    public ProfileScope MeasureScope(string name) {
        return new ProfileScope(name, this);
    }
    
    public struct ProfileScope : IDisposable {
        private string name;
        private SystemProfiler profiler;
        private long startTime;
        
        public ProfileScope(string name, SystemProfiler profiler) {
            this.name = name;
            this.profiler = profiler;
            this.startTime = Stopwatch.GetTimestamp();
        }
        
        public void Dispose() {
            var elapsed = Stopwatch.GetTimestamp() - startTime;
            profiler.RecordTiming(name, elapsed);
        }
    }
}

// Usage example
public void UpdateProvinces() {
    using (profiler.MeasureScope("ProvinceUpdate")) {
        foreach (var province in dirtyProvinces) {
            UpdateProvince(province);
        }
    }
}
```

## Memory Monitoring

### Memory Profiler
```csharp
public class MemoryMonitor {
    private MemorySnapshot lastSnapshot;
    private readonly long MEMORY_WARNING_THRESHOLD = 2L * 1024 * 1024 * 1024; // 2GB
    private readonly long MEMORY_CRITICAL_THRESHOLD = 3L * 1024 * 1024 * 1024; // 3GB
    
    public class MemorySnapshot {
        public long totalMemory;
        public long managedMemory;
        public long nativeMemory;
        public long graphicsMemory;
        
        public Dictionary<string, long> systemMemory = new();
        
        public void Capture() {
            totalMemory = Profiler.GetTotalAllocatedMemoryLong();
            managedMemory = GC.GetTotalMemory(false);
            nativeMemory = Profiler.GetTotalReservedMemoryLong();
            graphicsMemory = Profiler.GetAllocatedMemoryForGraphicsDriver();
            
            // System-specific memory
            systemMemory["Provinces"] = ProvinceSystem.GetMemoryUsage();
            systemMemory["Nations"] = NationSystem.GetMemoryUsage();
            systemMemory["AI"] = AISystem.GetMemoryUsage();
            systemMemory["Pathfinding"] = PathfindingCache.GetMemoryUsage();
            systemMemory["Commands"] = CommandHistory.GetMemoryUsage();
            systemMemory["Effects"] = EffectSystem.GetMemoryUsage();
        }
        
        public string GenerateReport() {
            var report = new StringBuilder();
            report.AppendLine("=== Memory Report ===");
            report.AppendLine($"Total: {FormatBytes(totalMemory)}");
            report.AppendLine($"Managed: {FormatBytes(managedMemory)}");
            report.AppendLine($"Native: {FormatBytes(nativeMemory)}");
            report.AppendLine($"Graphics: {FormatBytes(graphicsMemory)}");
            report.AppendLine("\nSystem Breakdown:");
            
            foreach (var kvp in systemMemory.OrderByDescending(x => x.Value)) {
                report.AppendLine($"  {kvp.Key}: {FormatBytes(kvp.Value)}");
            }
            
            return report.ToString();
        }
    }
    
    public void CheckMemoryHealth() {
        var current = new MemorySnapshot();
        current.Capture();
        
        if (current.totalMemory > MEMORY_CRITICAL_THRESHOLD) {
            OnCriticalMemory(current);
        }
        else if (current.totalMemory > MEMORY_WARNING_THRESHOLD) {
            OnHighMemory(current);
        }
        
        // Check for leaks (growing memory)
        if (lastSnapshot != null) {
            var growth = current.totalMemory - lastSnapshot.totalMemory;
            if (growth > 100 * 1024 * 1024) { // 100MB growth
                LogWarning($"Memory grew by {FormatBytes(growth)}");
                LogMemoryGrowth(lastSnapshot, current);
            }
        }
        
        lastSnapshot = current;
    }
}
```

### Allocation Tracking
```csharp
public class AllocationTracker {
    private Dictionary<string, AllocationStats> allocations = new();
    
    public struct AllocationStats {
        public long totalAllocated;
        public long totalFreed;
        public long currentlyAllocated;
        public int allocationCount;
        public int largestAllocation;
        
        public long NetAllocation => totalAllocated - totalFreed;
    }
    
    public void TrackAllocation(string category, int size) {
        if (!allocations.ContainsKey(category)) {
            allocations[category] = new AllocationStats();
        }
        
        var stats = allocations[category];
        stats.totalAllocated += size;
        stats.currentlyAllocated += size;
        stats.allocationCount++;
        stats.largestAllocation = Math.Max(stats.largestAllocation, size);
        allocations[category] = stats;
    }
    
    public void TrackDeallocation(string category, int size) {
        if (allocations.ContainsKey(category)) {
            var stats = allocations[category];
            stats.totalFreed += size;
            stats.currentlyAllocated -= size;
            allocations[category] = stats;
        }
    }
    
    public void LogAllocationReport() {
        DominionLogger.Log("=== Allocation Report ===");
        
        foreach (var kvp in allocations.OrderByDescending(x => x.Value.currentlyAllocated)) {
            var stats = kvp.Value;
            DominionLogger.Log($"{kvp.Key}:");
            DominionLogger.Log($"  Currently Allocated: {FormatBytes(stats.currentlyAllocated)}");
            DominionLogger.Log($"  Total Allocated: {FormatBytes(stats.totalAllocated)}");
            DominionLogger.Log($"  Allocation Count: {stats.allocationCount}");
            DominionLogger.Log($"  Largest: {FormatBytes(stats.largestAllocation)}");
        }
    }
}
```

## Performance Bottleneck Detection

### Automatic Bottleneck Detector
```csharp
public class BottleneckDetector {
    private struct SystemTiming {
        public string name;
        public float averageMs;
        public float maxMs;
        public float percentage;  // Of frame time
        public int sampleCount;
    }
    
    private Dictionary<string, SystemTiming> systemTimings = new();
    private float frameTimeTarget = 5.0f; // 200 FPS = 5ms
    
    public void AnalyzeFrame(Dictionary<string, float> frameTimes) {
        float totalTime = frameTimes.Values.Sum();
        
        // Update system timings
        foreach (var kvp in frameTimes) {
            if (!systemTimings.ContainsKey(kvp.Key)) {
                systemTimings[kvp.Key] = new SystemTiming { name = kvp.Key };
            }
            
            var timing = systemTimings[kvp.Key];
            timing.sampleCount++;
            timing.averageMs = (timing.averageMs * (timing.sampleCount - 1) + kvp.Value) / timing.sampleCount;
            timing.maxMs = Math.Max(timing.maxMs, kvp.Value);
            timing.percentage = (kvp.Value / totalTime) * 100f;
            systemTimings[kvp.Key] = timing;
        }
        
        // Detect bottlenecks
        DetectBottlenecks(totalTime);
    }
    
    private void DetectBottlenecks(float totalTime) {
        if (totalTime <= frameTimeTarget) return; // Performance is fine
        
        var bottlenecks = systemTimings
            .Where(x => x.Value.averageMs > frameTimeTarget * 0.2f) // >20% of budget
            .OrderByDescending(x => x.Value.averageMs)
            .Take(3);
        
        foreach (var bottleneck in bottlenecks) {
            LogBottleneck(bottleneck.Value);
            SuggestOptimization(bottleneck.Value);
        }
    }
    
    private void SuggestOptimization(SystemTiming bottleneck) {
        string suggestion = bottleneck.name switch {
            "Pathfinding" => "Consider caching paths or using hierarchical pathfinding",
            "AI" => "Reduce AI update frequency or simplify decision making",
            "Rendering" => "Reduce draw calls or use LOD system",
            "Province Update" => "Use dirty flags to update only changed provinces",
            "Command Processing" => "Batch commands or use command queue",
            _ => "Profile this system in detail to find optimization opportunities"
        };
        
        LogSuggestion($"{bottleneck.name}: {suggestion}");
    }
}
```

## Cache Performance Monitoring

### Cache Hit Rate Tracker
```csharp
public class CacheMonitor {
    public class CacheStats {
        public string name;
        public long hits;
        public long misses;
        public long evictions;
        public float hitRate => hits / (float)(hits + misses);
        public long memoryUsage;
        public int itemCount;
        
        public void RecordHit() {
            Interlocked.Increment(ref hits);
        }
        
        public void RecordMiss() {
            Interlocked.Increment(ref misses);
        }
        
        public void RecordEviction() {
            Interlocked.Increment(ref evictions);
        }
    }
    
    private Dictionary<string, CacheStats> caches = new();
    
    public void RegisterCache(string name, ICache cache) {
        caches[name] = new CacheStats {
            name = name,
            memoryUsage = cache.GetMemoryUsage(),
            itemCount = cache.Count
        };
    }
    
    public void RecordCacheAccess(string cacheName, bool hit) {
        if (caches.TryGetValue(cacheName, out var stats)) {
            if (hit) stats.RecordHit();
            else stats.RecordMiss();
        }
    }
    
    public void GenerateCacheReport() {
        DominionLogger.Log("=== Cache Performance ===");
        
        foreach (var cache in caches.Values.OrderBy(x => x.hitRate)) {
            DominionLogger.Log($"{cache.name}:");
            DominionLogger.Log($"  Hit Rate: {cache.hitRate:P2}");
            DominionLogger.Log($"  Hits: {cache.hits:N0}, Misses: {cache.misses:N0}");
            DominionLogger.Log($"  Memory: {FormatBytes(cache.memoryUsage)}");
            DominionLogger.Log($"  Items: {cache.itemCount:N0}");
            DominionLogger.Log($"  Evictions: {cache.evictions:N0}");
            
            if (cache.hitRate < 0.8f) {
                DominionLogger.LogWarning($"  ⚠ Low hit rate! Consider increasing cache size");
            }
        }
    }
}
```

## Draw Call & GPU Monitoring

### Rendering Performance Monitor
```csharp
public class RenderingMonitor {
    private RenderStats currentStats;
    private RenderStats targetStats = new() {
        drawCalls = 100,
        triangles = 1_000_000,
        vertices = 2_000_000,
        setPassCalls = 50,
        textureMemory = 512 * 1024 * 1024, // 512MB
        renderTextureMemory = 256 * 1024 * 1024 // 256MB
    };
    
    public struct RenderStats {
        public int drawCalls;
        public int batches;
        public int triangles;
        public int vertices;
        public int setPassCalls;
        public long textureMemory;
        public long renderTextureMemory;
        public float gpuTime;
        public int shadowCasters;
        public int visibleObjects;
    }
    
    public void UpdateStats() {
        currentStats = new RenderStats {
            drawCalls = UnityStats.drawCalls,
            batches = UnityStats.batches,
            triangles = UnityStats.triangles,
            vertices = UnityStats.vertices,
            setPassCalls = UnityStats.setPassCalls,
            textureMemory = Profiler.GetAllocatedMemoryForGraphicsDriver(),
            renderTextureMemory = UnityStats.renderTextureBytes,
            shadowCasters = UnityStats.shadowCasters
        };
        
        CheckRenderingHealth();
    }
    
    private void CheckRenderingHealth() {
        if (currentStats.drawCalls > targetStats.drawCalls) {
            LogWarning($"High draw calls: {currentStats.drawCalls} (target: {targetStats.drawCalls})");
            SuggestRenderingOptimization();
        }
        
        if (currentStats.textureMemory > targetStats.textureMemory) {
            LogWarning($"High texture memory: {FormatBytes(currentStats.textureMemory)}");
        }
    }
    
    private void SuggestRenderingOptimization() {
        // Analyze what's causing high draw calls
        if (ProvinceRenderer.GetDrawCalls() > 50) {
            LogSuggestion("Province rendering using too many draw calls - check texture atlasing");
        }
        
        if (UIRenderer.GetDrawCalls() > 30) {
            LogSuggestion("UI using too many draw calls - enable UI batching");
        }
        
        if (ArmyRenderer.GetDrawCalls() > 20) {
            LogSuggestion("Army rendering not instanced properly");
        }
    }
}
```

## Late Game Performance Tracking

### Performance Degradation Detector
```csharp
public class DegradationDetector {
    private PerformanceBaseline baseline;
    private readonly float DEGRADATION_THRESHOLD = 0.8f; // 20% performance loss
    
    public class PerformanceBaseline {
        public float avgFrameTime;
        public float memoryUsage;
        public int entityCount;
        public float pathfindingTime;
        public float aiTime;
        public DateTime timestamp;
        
        public static PerformanceBaseline Capture() {
            return new PerformanceBaseline {
                avgFrameTime = Time.smoothDeltaTime,
                memoryUsage = GC.GetTotalMemory(false),
                entityCount = GetTotalEntityCount(),
                pathfindingTime = PathfindingSystem.AverageTime,
                aiTime = AISystem.AverageTime,
                timestamp = DateTime.Now
            };
        }
    }
    
    public void EstablishBaseline() {
        baseline = PerformanceBaseline.Capture();
        LogInfo($"Performance baseline established at {baseline.timestamp}");
    }
    
    public void CheckForDegradation() {
        if (baseline == null) {
            EstablishBaseline();
            return;
        }
        
        var current = PerformanceBaseline.Capture();
        
        // Check frame time degradation
        float frameTimeDegradation = current.avgFrameTime / baseline.avgFrameTime;
        if (frameTimeDegradation > 1.0f / DEGRADATION_THRESHOLD) {
            LogWarning($"Frame time degraded by {(frameTimeDegradation - 1) * 100:F1}%");
            AnalyzeDegradationCause(baseline, current);
        }
        
        // Check memory growth
        float memoryGrowth = current.memoryUsage / baseline.memoryUsage;
        if (memoryGrowth > 2.0f) {
            LogWarning($"Memory usage doubled since baseline: {FormatBytes(current.memoryUsage)}");
        }
        
        // Check system-specific degradation
        if (current.pathfindingTime > baseline.pathfindingTime * 2) {
            LogWarning("Pathfinding performance degraded - cache may need clearing");
        }
        
        if (current.aiTime > baseline.aiTime * 2) {
            LogWarning("AI performance degraded - too many active goals?");
        }
    }
    
    private void AnalyzeDegradationCause(PerformanceBaseline baseline, PerformanceBaseline current) {
        var report = new StringBuilder("=== Performance Degradation Analysis ===\n");
        
        // Entity growth
        float entityGrowth = current.entityCount / (float)baseline.entityCount;
        report.AppendLine($"Entity growth: {entityGrowth:F1}x ({baseline.entityCount} -> {current.entityCount})");
        
        // Time since baseline
        var timeElapsed = current.timestamp - baseline.timestamp;
        report.AppendLine($"Time elapsed: {timeElapsed.TotalHours:F1} hours");
        
        // Identify likely culprit
        if (entityGrowth > 2.0f) {
            report.AppendLine("⚠ Excessive entity growth detected");
            report.AppendLine("  Suggested fix: Review entity lifecycle and pooling");
        }
        
        if (CommandHistory.Count > 100000) {
            report.AppendLine("⚠ Command history very large");
            report.AppendLine("  Suggested fix: Create checkpoint and clear old commands");
        }
        
        DominionLogger.Log(report.ToString());
    }
}
```

## Performance Budget System

### Frame Time Budgeting
```csharp
public class PerformanceBudget {
    private readonly float TARGET_FRAME_TIME = 5.0f; // 200 FPS
    
    public class SystemBudget {
        public string name;
        public float allocatedMs;
        public float currentMs;
        public bool isOverBudget => currentMs > allocatedMs;
        public float budgetUsage => currentMs / allocatedMs;
    }
    
    private Dictionary<string, SystemBudget> budgets = new() {
        ["Simulation"] = new() { name = "Simulation", allocatedMs = 1.0f },
        ["AI"] = new() { name = "AI", allocatedMs = 1.0f },
        ["Pathfinding"] = new() { name = "Pathfinding", allocatedMs = 0.5f },
        ["Rendering"] = new() { name = "Rendering", allocatedMs = 2.0f },
        ["UI"] = new() { name = "UI", allocatedMs = 0.5f }
    };
    
    public void UpdateBudget(string system, float currentMs) {
        if (budgets.TryGetValue(system, out var budget)) {
            budget.currentMs = currentMs;
            
            if (budget.isOverBudget) {
                HandleBudgetOverrun(budget);
            }
        }
    }
    
    private void HandleBudgetOverrun(SystemBudget budget) {
        float overrun = budget.currentMs - budget.allocatedMs;
        LogWarning($"{budget.name} over budget by {overrun:F2}ms ({budget.budgetUsage:P0})");
        
        // Suggest optimizations based on system
        switch (budget.name) {
            case "AI":
                AI.ReduceUpdateFrequency();
                LogInfo("Reduced AI update frequency to stay within budget");
                break;
                
            case "Pathfinding":
                PathfindingCache.ReduceCacheSize();
                LogInfo("Reduced pathfinding cache to stay within budget");
                break;
                
            case "Rendering":
                QualitySettings.SetLODBias(2.0f);
                LogInfo("Increased LOD bias to reduce rendering cost");
                break;
        }
    }
    
    public string GetBudgetReport() {
        var report = new StringBuilder("=== Performance Budget ===\n");
        float totalAllocated = budgets.Values.Sum(b => b.allocatedMs);
        float totalUsed = budgets.Values.Sum(b => b.currentMs);
        
        report.AppendLine($"Frame Budget: {TARGET_FRAME_TIME:F1}ms");
        report.AppendLine($"Allocated: {totalAllocated:F1}ms");
        report.AppendLine($"Used: {totalUsed:F1}ms\n");
        
        foreach (var budget in budgets.Values.OrderByDescending(b => b.budgetUsage)) {
            string status = budget.isOverBudget ? "⚠ OVER" : "✓ OK";
            report.AppendLine($"{budget.name}: {budget.currentMs:F2}/{budget.allocatedMs:F2}ms [{budget.budgetUsage:P0}] {status}");
        }
        
        return report.ToString();
    }
}
```

## Automated Performance Testing

### Performance Regression Tests
```csharp
#if UNITY_EDITOR
public class PerformanceTests {
    [Test]
    public void TestProvinceUpdatePerformance() {
        // Setup
        var provinces = CreateTestProvinces(10000);
        MarkDirty(provinces, 0.1f); // 10% dirty
        
        // Measure
        var stopwatch = Stopwatch.StartNew();
        ProvinceSystem.UpdateDirtyProvinces();
        stopwatch.Stop();
        
        // Assert
        Assert.Less(stopwatch.ElapsedMilliseconds, 1.0f, 
            "Province update took too long");
    }
    
    [Test]
    public void TestPathfindingPerformance() {
        // Setup
        var map = CreateTestMap(10000);
        
        // Measure 100 random paths
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) {
            var start = Random.Range(0, 10000);
            var end = Random.Range(0, 10000);
            Pathfinding.FindPath(start, end);
        }
        stopwatch.Stop();
        
        // Assert average time
        float averageMs = stopwatch.ElapsedMilliseconds / 100f;
        Assert.Less(averageMs, 1.0f, 
            "Pathfinding average exceeded 1ms");
    }
    
    [Test]
    public void TestMemoryLeaks() {
        // Baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long baselineMemory = GC.GetTotalMemory(false);
        
        // Run game simulation
        for (int i = 0; i < 1000; i++) {
            TimeManager.Tick(1.0f);
            ProvinceSystem.Update();
            AISystem.Update();
        }
        
        // Cleanup and measure
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long finalMemory = GC.GetTotalMemory(false);
        
        // Assert no significant growth
        long growth = finalMemory - baselineMemory;
        Assert.Less(growth, 10 * 1024 * 1024, // 10MB tolerance
            $"Memory grew by {growth / 1024 / 1024}MB");
    }
}
#endif
```

## Performance Reporting

### Automatic Performance Reports
```csharp
public class PerformanceReporter {
    private readonly int REPORT_INTERVAL_FRAMES = 600; // Every 10 seconds at 60 FPS
    private int frameCounter = 0;
    
    public void Update() {
        frameCounter++;
        
        if (frameCounter >= REPORT_INTERVAL_FRAMES) {
            GenerateReport();
            frameCounter = 0;
        }
    }
    
    private void GenerateReport() {
        var report = new PerformanceReport {
            timestamp = DateTime.Now,
            gameTime = TimeManager.CurrentDate,
            fps = PerformanceMonitor.AverageFPS,
            frameTime = PerformanceMonitor.AverageFrameTime,
            memory = MemoryMonitor.CurrentUsage,
            drawCalls = RenderingMonitor.DrawCalls,
            systemTimings = GetSystemTimings(),
            cacheStats = GetCacheStats(),
            warnings = CollectWarnings()
        };
        
        // Log locally
        LogReport(report);
        
        // Send telemetry if enabled
        if (Settings.TelemetryEnabled) {
            TelemetryService.SendPerformanceReport(report);
        }
        
        // Show in-game if debug mode
        #if DEBUG
        ShowPerformanceOverlay(report);
        #endif
    }
    
    private List<PerformanceWarning> CollectWarnings() {
        var warnings = new List<PerformanceWarning>();
        
        if (PerformanceMonitor.MinFPS < 60) {
            warnings.Add(new PerformanceWarning {
                severity = WarningSeverity.High,
                message = $"FPS dropped below 60: {PerformanceMonitor.MinFPS}"
            });
        }
        
        if (MemoryMonitor.CurrentUsage > 2_000_000_000) {
            warnings.Add(new PerformanceWarning {
                severity = WarningSeverity.Medium,
                message = "Memory usage exceeds 2GB"
            });
        }
        
        if (RenderingMonitor.DrawCalls > 200) {
            warnings.Add(new PerformanceWarning {
                severity = WarningSeverity.Low,
                message = "Draw calls exceed target"
            });
        }
        
        return warnings;
    }
}
```

## Performance Optimization Helpers

### Automatic Quality Adjustment
```csharp
public class DynamicQualityAdjuster {
    private float targetFrameTime = 5.0f; // 200 FPS
    private float acceptableFrameTime = 16.67f; // 60 FPS
    
    private enum QualityLevel {
        Ultra = 0,
        High = 1,
        Medium = 2,
        Low = 3,
        Potato = 4  // For weak systems
    }
    
    private QualityLevel currentLevel = QualityLevel.High;
    
    public void AdjustQuality() {
        float avgFrameTime = PerformanceMonitor.AverageFrameTime;
        
        if (avgFrameTime > acceptableFrameTime) {
            // Performance is bad, reduce quality
            if (currentLevel < QualityLevel.Potato) {
                currentLevel++;
                ApplyQualityLevel(currentLevel);
                LogInfo($"Reduced quality to {currentLevel} due to performance");
            }
        }
        else if (avgFrameTime < targetFrameTime * 0.8f) {
            // Performance is great, can increase quality
            if (currentLevel > QualityLevel.Ultra) {
                currentLevel--;
                ApplyQualityLevel(currentLevel);
                LogInfo($"Increased quality to {currentLevel}");
            }
        }
    }
    
    private void ApplyQualityLevel(QualityLevel level) {
        switch (level) {
            case QualityLevel.Ultra:
                QualitySettings.SetQualityLevel(5);
                ProvinceRenderer.SetLODBias(0.5f);
                EnableAllEffects();
                break;
                
            case QualityLevel.High:
                QualitySettings.SetQualityLevel(3);
                ProvinceRenderer.SetLODBias(1.0f);
                EnableMostEffects();
                break;
                
            case QualityLevel.Medium:
                QualitySettings.SetQualityLevel(2);
                ProvinceRenderer.SetLODBias(1.5f);
                DisableSomeEffects();
                break;
                
            case QualityLevel.Low:
                QualitySettings.SetQualityLevel(1);
                ProvinceRenderer.SetLODBias(2.0f);
                DisableMostEffects();
                break;
                
            case QualityLevel.Potato:
                QualitySettings.SetQualityLevel(0);
                ProvinceRenderer.SetLODBias(3.0f);
                DisableAllEffects();
                ReduceTextureQuality();
                break;
        }
    }
}
```

## Debug Overlay

### In-Game Performance Display
```csharp
public class PerformanceOverlay : MonoBehaviour {
    private bool showOverlay = false;
    private GUIStyle style;
    
    void Start() {
        style = new GUIStyle {
            fontSize = 12,
            normal = { textColor = Color.white }
        };
    }
    
    void Update() {
        if (Input.GetKeyDown(KeyCode.F3)) {
            showOverlay = !showOverlay;
        }
    }
    
    void OnGUI() {
        if (!showOverlay) return;
        
        float y = 10;
        float lineHeight = 20;
        
        // FPS
        GUI.Label(new Rect(10, y, 300, lineHeight), 
            $"FPS: {PerformanceMonitor.CurrentFPS:F1} (avg: {PerformanceMonitor.AverageFPS:F1})", 
            style);
        y += lineHeight;
        
        // Frame time
        GUI.Label(new Rect(10, y, 300, lineHeight), 
            $"Frame: {PerformanceMonitor.FrameTime:F2}ms", 
            style);
        y += lineHeight;
        
        // Memory
        GUI.Label(new Rect(10, y, 300, lineHeight), 
            $"Memory: {FormatBytes(GC.GetTotalMemory(false))}", 
            style);
        y += lineHeight;
        
        // Draw calls
        GUI.Label(new Rect(10, y, 300, lineHeight), 
            $"Draw Calls: {UnityStats.drawCalls}", 
            style);
        y += lineHeight;
        
        // System breakdown
        y += 10;
        GUI.Label(new Rect(10, y, 300, lineHeight), "=== Systems ===", style);
        y += lineHeight;
        
        foreach (var system in SystemProfiler.GetTopSystems(5)) {
            GUI.Label(new Rect(10, y, 300, lineHeight), 
                $"{system.name}: {system.time:F2}ms", 
                style);
            y += lineHeight;
        }
    }
}
```

## Best Practices

1. **Profile early and often** - Don't wait until performance problems appear
2. **Establish baselines** - Know what "good" performance looks like
3. **Monitor the right metrics** - FPS alone isn't enough
4. **Automate testing** - Performance regression tests catch issues early
5. **Budget frame time** - Each system gets a slice, enforce limits
6. **Track cache efficiency** - Poor cache performance kills everything
7. **Watch for degradation** - Late game performance is critical
8. **Make data actionable** - Warnings should include suggested fixes

## Summary

This performance monitoring architecture provides:
- **Real-time monitoring** of all critical metrics
- **Automatic detection** of bottlenecks and issues
- **Performance budgeting** to maintain frame rate
- **Degradation tracking** to catch late-game slowdowns
- **Actionable insights** with specific optimization suggestions
- **Automated testing** to prevent performance regressions

The key is comprehensive monitoring that catches problems before players notice them, combined with automatic adjustments to maintain target performance even on weaker systems.