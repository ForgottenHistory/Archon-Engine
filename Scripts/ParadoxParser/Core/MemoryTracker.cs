using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public static class MemoryTracker
    {
        private static readonly Dictionary<string, MemoryRegion> s_Regions = new Dictionary<string, MemoryRegion>();
        private static long s_TotalTrackedMemory = 0;
        private static bool s_IsEnabled = true;

        public static bool IsEnabled
        {
            get => s_IsEnabled;
            set => s_IsEnabled = value;
        }

        public static long TotalTrackedMemory => s_TotalTrackedMemory;

        public static void BeginRegion(string regionName)
        {
            if (!s_IsEnabled) return;

            if (!s_Regions.ContainsKey(regionName))
            {
                s_Regions[regionName] = new MemoryRegion(regionName);
            }

            s_Regions[regionName].Begin();
        }

        public static void EndRegion(string regionName)
        {
            if (!s_IsEnabled) return;

            if (s_Regions.TryGetValue(regionName, out var region))
            {
                region.End();
            }
        }

        public static void TrackAllocation(string regionName, long bytes)
        {
            if (!s_IsEnabled) return;

            if (!s_Regions.ContainsKey(regionName))
            {
                s_Regions[regionName] = new MemoryRegion(regionName);
            }

            s_Regions[regionName].AddAllocation(bytes);
            s_TotalTrackedMemory += bytes;
        }

        public static void TrackDeallocation(string regionName, long bytes)
        {
            if (!s_IsEnabled) return;

            if (s_Regions.TryGetValue(regionName, out var region))
            {
                region.RemoveAllocation(bytes);
                s_TotalTrackedMemory -= bytes;
            }
        }

        public static MemoryRegionSnapshot GetRegionSnapshot(string regionName)
        {
            if (s_Regions.TryGetValue(regionName, out var region))
            {
                return region.GetSnapshot();
            }

            return new MemoryRegionSnapshot { Name = regionName, IsValid = false };
        }

        public static MemoryRegionSnapshot[] GetAllRegionSnapshots()
        {
            var snapshots = new MemoryRegionSnapshot[s_Regions.Count];
            int index = 0;

            foreach (var kvp in s_Regions)
            {
                snapshots[index++] = kvp.Value.GetSnapshot();
            }

            return snapshots;
        }

        public static void LogMemoryReport()
        {
            if (!s_IsEnabled) return;

            Debug.Log($"[MemoryTracker] Total Tracked Memory: {s_TotalTrackedMemory / (1024 * 1024):F2}MB");

            foreach (var kvp in s_Regions)
            {
                var snapshot = kvp.Value.GetSnapshot();
                Debug.Log($"[MemoryTracker] {snapshot.Name}: {snapshot.CurrentBytes / (1024 * 1024):F2}MB current, {snapshot.PeakBytes / (1024 * 1024):F2}MB peak, {snapshot.AllocationCount} allocations");
            }
        }

        public static void Reset()
        {
            foreach (var region in s_Regions.Values)
            {
                region.Reset();
            }
            s_TotalTrackedMemory = 0;
        }

        public static void Clear()
        {
            s_Regions.Clear();
            s_TotalTrackedMemory = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public class MemoryRegion
    {
        public string Name { get; private set; }
        public long CurrentBytes { get; private set; }
        public long PeakBytes { get; private set; }
        public int AllocationCount { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime LastActivity { get; private set; }
        public bool IsActive { get; private set; }

        public MemoryRegion(string name)
        {
            Name = name;
            Reset();
        }

        public void Begin()
        {
            if (!IsActive)
            {
                StartTime = DateTime.Now;
                IsActive = true;
            }
        }

        public void End()
        {
            IsActive = false;
            LastActivity = DateTime.Now;
        }

        public void AddAllocation(long bytes)
        {
            CurrentBytes += bytes;
            AllocationCount++;
            LastActivity = DateTime.Now;

            if (CurrentBytes > PeakBytes)
            {
                PeakBytes = CurrentBytes;
            }
        }

        public void RemoveAllocation(long bytes)
        {
            CurrentBytes -= bytes;
            LastActivity = DateTime.Now;

            if (CurrentBytes < 0)
            {
                Debug.LogWarning($"[MemoryTracker] Region '{Name}' has negative memory: {CurrentBytes}");
                CurrentBytes = 0;
            }
        }

        public MemoryRegionSnapshot GetSnapshot()
        {
            return new MemoryRegionSnapshot
            {
                Name = Name,
                CurrentBytes = CurrentBytes,
                PeakBytes = PeakBytes,
                AllocationCount = AllocationCount,
                StartTime = StartTime,
                LastActivity = LastActivity,
                IsActive = IsActive,
                IsValid = true
            };
        }

        public void Reset()
        {
            CurrentBytes = 0;
            PeakBytes = 0;
            AllocationCount = 0;
            StartTime = DateTime.Now;
            LastActivity = DateTime.Now;
            IsActive = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryRegionSnapshot
    {
        public string Name;
        public long CurrentBytes;
        public long PeakBytes;
        public int AllocationCount;
        public DateTime StartTime;
        public DateTime LastActivity;
        public bool IsActive;
        public bool IsValid;

        public override string ToString()
        {
            if (!IsValid) return $"Invalid region: {Name}";

            return $"{Name}: {CurrentBytes / (1024 * 1024):F2}MB current, {PeakBytes / (1024 * 1024):F2}MB peak, {AllocationCount} allocations, Active: {IsActive}";
        }
    }

    public struct MemoryScope : IDisposable
    {
        private readonly string m_RegionName;
        private readonly bool m_WasEnabled;

        public MemoryScope(string regionName)
        {
            m_RegionName = regionName;
            m_WasEnabled = MemoryTracker.IsEnabled;

            if (m_WasEnabled)
            {
                MemoryTracker.BeginRegion(regionName);
            }
        }

        public void Dispose()
        {
            if (m_WasEnabled)
            {
                MemoryTracker.EndRegion(m_RegionName);
            }
        }
    }
}