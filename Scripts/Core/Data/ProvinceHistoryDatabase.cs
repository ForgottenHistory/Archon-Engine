using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Utils;

namespace Core.Data
{
    /// <summary>
    /// Cold data storage for province historical events
    /// Implements tiered history with compression to prevent unbounded growth
    /// Follows performance architecture: recent events + compressed history + statistics
    /// </summary>
    public class ProvinceHistoryDatabase : IDisposable
    {
        private const int MAX_RECENT_EVENTS = 100;        // Last 100 events (full detail)
        private const int MAX_MEDIUM_EVENTS = 1000;       // Compressed older events
        private const int COMPRESSION_THRESHOLD = 50;     // Compress after 50 years

        // Recent history - full detail (last ~10 years)
        private Dictionary<int, Utils.CircularBuffer<HistoricalEvent>> recentHistory;

        // Medium-term history - compressed (10-50 years ago)
        private Dictionary<int, CompressedHistory> mediumHistory;

        // Long-term history - statistical summary only (50+ years ago)
        private Dictionary<int, HistoryStatistics> longTermHistory;

        public ProvinceHistoryDatabase()
        {
            recentHistory = new Dictionary<int, Utils.CircularBuffer<HistoricalEvent>>();
            mediumHistory = new Dictionary<int, CompressedHistory>();
            longTermHistory = new Dictionary<int, HistoryStatistics>();
        }

        /// <summary>
        /// Add historical event for province (bounded growth)
        /// </summary>
        public void AddEvent(int provinceID, HistoricalEvent evt)
        {
            // Get or create recent history buffer
            if (!recentHistory.TryGetValue(provinceID, out var recent))
            {
                recent = new Utils.CircularBuffer<HistoricalEvent>(MAX_RECENT_EVENTS);
                recentHistory[provinceID] = recent;
            }

            // Add to recent history
            if (recent.IsFull)
            {
                // Move oldest event to medium-term storage
                var oldest = recent.RemoveOldest();
                CompressOldEvent(provinceID, oldest);
            }

            recent.Add(evt);
        }

        /// <summary>
        /// Get recent events for province (bounded result)
        /// </summary>
        public List<HistoricalEvent> GetRecentEvents(int provinceID, int maxCount = 20)
        {
            if (!recentHistory.TryGetValue(provinceID, out var recent))
                return new List<HistoricalEvent>();

            var result = new List<HistoricalEvent>();
            var events = recent.GetMostRecent(maxCount);

            foreach (var evt in events)
            {
                result.Add(evt);
            }

            return result;
        }

        /// <summary>
        /// Get compressed historical summary for province
        /// </summary>
        public ProvinceHistorySummary GetHistorySummary(int provinceID)
        {
            var summary = new ProvinceHistorySummary { ProvinceID = provinceID };

            // Add recent events count
            if (recentHistory.TryGetValue(provinceID, out var recent))
            {
                summary.RecentEventCount = recent.Count;
            }

            // Add compressed history info
            if (mediumHistory.TryGetValue(provinceID, out var medium))
            {
                summary.CompressedEventCount = medium.EventCount;
                summary.OwnershipChanges = medium.OwnershipChanges;
            }

            // Add long-term statistics
            if (longTermHistory.TryGetValue(provinceID, out var longTerm))
            {
                summary.TotalOwnershipChanges = longTerm.TotalOwnershipChanges;
                summary.AverageDevelopment = longTerm.AverageDevelopment;
            }

            return summary;
        }

        /// <summary>
        /// Compress old event into medium-term storage
        /// </summary>
        private void CompressOldEvent(int provinceID, HistoricalEvent evt)
        {
            if (!mediumHistory.TryGetValue(provinceID, out var compressed))
            {
                compressed = new CompressedHistory();
                mediumHistory[provinceID] = compressed;
            }

            compressed.AddEvent(evt);

            // Check if we need to move to long-term statistics
            if (compressed.EventCount > MAX_MEDIUM_EVENTS)
            {
                CompressToLongTerm(provinceID, compressed);
            }
        }

        /// <summary>
        /// Compress medium-term history to long-term statistics
        /// </summary>
        private void CompressToLongTerm(int provinceID, CompressedHistory compressed)
        {
            if (!longTermHistory.TryGetValue(provinceID, out var longTerm))
            {
                longTerm = new HistoryStatistics();
                longTermHistory[provinceID] = longTerm;
            }

            // Extract statistics from compressed history
            longTerm.TotalOwnershipChanges += compressed.OwnershipChanges;
            longTerm.UpdateAverageDevelopment(compressed.AverageDevelopment, compressed.EventCount);

            // Clear oldest compressed events
            compressed.ClearOldest(MAX_MEDIUM_EVENTS / 2);
        }

        /// <summary>
        /// Get memory usage statistics
        /// </summary>
        public HistoryDatabaseStats GetStats()
        {
            return new HistoryDatabaseStats
            {
                ProvincesWithRecentHistory = recentHistory.Count,
                ProvincesWithCompressedHistory = mediumHistory.Count,
                ProvincesWithLongTermHistory = longTermHistory.Count,
                TotalRecentEvents = CountTotalRecentEvents(),
                EstimatedMemoryUsageKB = EstimateMemoryUsage()
            };
        }

        private int CountTotalRecentEvents()
        {
            int total = 0;
            foreach (var recent in recentHistory.Values)
            {
                total += recent.Count;
            }
            return total;
        }

        private int EstimateMemoryUsage()
        {
            // Rough estimate: 100 bytes per recent event, 20 bytes per compressed event
            int recentSize = CountTotalRecentEvents() * 100;
            int compressedSize = mediumHistory.Count * 20 * MAX_MEDIUM_EVENTS / 4;
            int longTermSize = longTermHistory.Count * 50;

            return (recentSize + compressedSize + longTermSize) / 1024;
        }

        public void Dispose()
        {
            recentHistory?.Clear();
            mediumHistory?.Clear();
            longTermHistory?.Clear();
        }
    }

    /// <summary>
    /// Historical event structure for recent history
    /// </summary>
    [System.Serializable]
    public struct HistoricalEvent
    {
        public DateTime Date;
        public HistoryEventType Type;
        public ushort OldOwnerID;
        public ushort NewOwnerID;
        public byte OldDevelopment;
        public byte NewDevelopment;
        public ushort RelatedProvinceID;

        public static HistoricalEvent CreateOwnershipChange(DateTime date, ushort oldOwner, ushort newOwner)
        {
            return new HistoricalEvent
            {
                Date = date,
                Type = HistoryEventType.OwnershipChange,
                OldOwnerID = oldOwner,
                NewOwnerID = newOwner
            };
        }
    }

    /// <summary>
    /// Compressed history for medium-term storage
    /// </summary>
    public class CompressedHistory
    {
        public int EventCount { get; private set; }
        public int OwnershipChanges { get; private set; }
        public float AverageDevelopment { get; private set; }
        private List<CompressedEvent> events;

        public CompressedHistory()
        {
            events = new List<CompressedEvent>();
        }

        public void AddEvent(HistoricalEvent evt)
        {
            var compressed = new CompressedEvent
            {
                Year = (ushort)evt.Date.Year,
                Type = evt.Type,
                NewOwnerID = evt.NewOwnerID
            };

            events.Add(compressed);
            EventCount++;

            if (evt.Type == HistoryEventType.OwnershipChange)
                OwnershipChanges++;

            // Update running average
            AverageDevelopment = (AverageDevelopment * (EventCount - 1) + evt.NewDevelopment) / EventCount;
        }

        public void ClearOldest(int count)
        {
            if (events.Count > count)
            {
                events.RemoveRange(0, count);
                EventCount -= count;
            }
        }
    }

    /// <summary>
    /// Long-term statistical summary
    /// </summary>
    public class HistoryStatistics
    {
        public int TotalOwnershipChanges { get; set; }
        public float AverageDevelopment { get; set; }
        public int SampleCount { get; private set; }

        public void UpdateAverageDevelopment(float newAverage, int newSampleCount)
        {
            if (SampleCount == 0)
            {
                AverageDevelopment = newAverage;
                SampleCount = newSampleCount;
            }
            else
            {
                float totalWeight = SampleCount + newSampleCount;
                AverageDevelopment = (AverageDevelopment * SampleCount + newAverage * newSampleCount) / totalWeight;
                SampleCount = (int)totalWeight;
            }
        }
    }

    /// <summary>
    /// Compressed event for medium-term storage (4 bytes)
    /// </summary>
    [System.Serializable]
    public struct CompressedEvent
    {
        public ushort Year;           // 2 bytes
        public HistoryEventType Type; // 1 byte
        public ushort NewOwnerID;     // 2 bytes - only store result, not transition
        // Total: 5 bytes (vs 20+ for full HistoricalEvent)
    }

    public enum HistoryEventType : byte
    {
        OwnershipChange = 1,
        DevelopmentChange = 2,
        CultureChange = 3,
        ReligionChange = 4,
        BuildingConstruction = 5
    }

    public struct ProvinceHistorySummary
    {
        public int ProvinceID;
        public int RecentEventCount;
        public int CompressedEventCount;
        public int TotalOwnershipChanges;
        public int OwnershipChanges;
        public float AverageDevelopment;
    }

    public struct HistoryDatabaseStats
    {
        public int ProvincesWithRecentHistory;
        public int ProvincesWithCompressedHistory;
        public int ProvincesWithLongTermHistory;
        public int TotalRecentEvents;
        public int EstimatedMemoryUsageKB;
    }
}