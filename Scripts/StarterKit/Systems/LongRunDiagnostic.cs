using UnityEngine;
using Core;
using Core.Systems;
using Utils;

namespace StarterKit.Systems
{
    /// <summary>
    /// Diagnostic for long-run performance testing.
    /// Logs FPS, memory, and tick timing at year milestones.
    /// Attach to any GameObject in scene. Finds GameState automatically.
    /// </summary>
    public class LongRunDiagnostic : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private int reportIntervalYears = 25;

        private GameState gameState;
        private System.IDisposable yearSub;

        // Tracking
        private int startYear;
        private float startRealTime;
        private float lastReportRealTime;
        private int lastReportYear;
        private int frameCount;
        private float fpsAccumulator;
        private float minFps = float.MaxValue;
        private float maxFps;

        // Monthly tick timing
        private float worstMonthlyTickMs;
        private float monthlyTickAccumulator;
        private int monthlyTickCount;
        private System.IDisposable monthSub;

        private bool isRunning;

        void Start()
        {
            gameState = FindFirstObjectByType<GameState>();
            if (gameState == null || gameState.Time == null)
            {
                ArchonLogger.LogWarning("LongRunDiagnostic: GameState not ready, retrying in Update", "game_hegemon");
                return;
            }
            BeginTracking();
        }

        void Update()
        {
            if (!isRunning)
            {
                if (gameState == null)
                    gameState = FindFirstObjectByType<GameState>();
                if (gameState != null && gameState.Time != null && gameState.Time.IsInitialized)
                    BeginTracking();
                return;
            }

            // Track FPS
            float fps = 1f / Time.unscaledDeltaTime;
            fpsAccumulator += fps;
            frameCount++;
            if (fps < minFps) minFps = fps;
            if (fps > maxFps) maxFps = fps;
        }

        private void BeginTracking()
        {
            startYear = gameState.Time.CurrentYear;
            startRealTime = Time.realtimeSinceStartup;
            lastReportRealTime = startRealTime;
            lastReportYear = startYear;

            yearSub = gameState.EventBus.Subscribe<YearlyTickEvent>(OnYearlyTick);
            monthSub = gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTickStart);

            isRunning = true;
            ArchonLogger.Log($"[LongRunDiagnostic] Started tracking from year {startYear}", "game_hegemon");
        }

        private void OnMonthlyTickStart(MonthlyTickEvent evt)
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            monthlyTickAccumulator += frameMs;
            monthlyTickCount++;
            if (frameMs > worstMonthlyTickMs)
                worstMonthlyTickMs = frameMs;
        }

        private void OnYearlyTick(YearlyTickEvent evt)
        {
            int currentYear = evt.GameTime.Year;
            int yearsElapsed = currentYear - startYear;

            if (yearsElapsed > 0 && yearsElapsed % reportIntervalYears == 0)
            {
                LogReport(currentYear, yearsElapsed);
            }
        }

        private void LogReport(int currentYear, int totalYearsElapsed)
        {
            float now = Time.realtimeSinceStartup;
            float totalRealSeconds = now - startRealTime;
            float segmentRealSeconds = now - lastReportRealTime;
            int segmentYears = currentYear - lastReportYear;

            float avgFps = frameCount > 0 ? fpsAccumulator / frameCount : 0f;
            float avgMonthlyMs = monthlyTickCount > 0 ? monthlyTickAccumulator / monthlyTickCount : 0f;
            long memoryMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
            long totalAllocMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

            string report = $"\n=== LONG RUN DIAGNOSTIC — Year {currentYear} ({totalYearsElapsed}yr elapsed) ===\n" +
                $"  Real time: {totalRealSeconds:F1}s total, {segmentRealSeconds:F1}s this segment ({segmentYears}yr)\n" +
                $"  Speed: {segmentYears / Mathf.Max(segmentRealSeconds, 0.001f):F1} game-years/sec this segment\n" +
                $"  FPS: avg {avgFps:F0}, min {minFps:F0}, max {maxFps:F0} ({frameCount} frames)\n" +
                $"  Monthly tick frame: avg {avgMonthlyMs:F1}ms, worst {worstMonthlyTickMs:F1}ms ({monthlyTickCount} ticks)\n" +
                $"  Memory: {memoryMB}MB managed, {totalAllocMB}MB total Unity\n" +
                $"===\n";

            ArchonLogger.Log(report, "game_hegemon");
            Debug.Log(report);

            // Reset per-segment tracking
            lastReportRealTime = now;
            lastReportYear = currentYear;
            frameCount = 0;
            fpsAccumulator = 0f;
            minFps = float.MaxValue;
            maxFps = 0f;
            worstMonthlyTickMs = 0f;
            monthlyTickAccumulator = 0f;
            monthlyTickCount = 0;
        }

        void OnDestroy()
        {
            if (isRunning && gameState != null)
            {
                int currentYear = gameState.Time.CurrentYear;
                int totalYearsElapsed = currentYear - startYear;
                if (totalYearsElapsed > 0)
                {
                    LogReport(currentYear, totalYearsElapsed);
                    ArchonLogger.Log($"[LongRunDiagnostic] Final report at year {currentYear}", "game_hegemon");
                }

                yearSub?.Dispose();
                monthSub?.Dispose();
            }
        }
    }
}
