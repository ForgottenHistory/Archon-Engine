using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public unsafe struct FilePriorityQueue : IDisposable
    {
        private NativeList<FileQueueEntry> m_Queue;
        private NativeHashMap<int, int> m_FileIdToIndex;
        private int m_NextFileId;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        public bool IsCreated => m_IsCreated;
        public int Count => m_Queue.IsCreated ? m_Queue.Length : 0;

        public FilePriorityQueue(int initialCapacity, Allocator allocator)
        {
            m_Queue = new NativeList<FileQueueEntry>(initialCapacity, allocator);
            m_FileIdToIndex = new NativeHashMap<int, int>(initialCapacity, allocator);
            m_NextFileId = 1;
            m_IsCreated = true;
            m_IsDisposed = false;
        }

        public int EnqueueFile(string filePath, FilePriority priority, FileCategory category = FileCategory.Other)
        {
            if (!m_IsCreated || m_IsDisposed) return -1;

            var fileId = m_NextFileId++;
            var entry = new FileQueueEntry
            {
                FileId = fileId,
                Priority = priority,
                Category = category,
                QueueTime = DateTime.Now.Ticks,
                FileSize = GetFileSize(filePath),
                Status = FileQueueStatus.Pending
            };

            // Store file path (simplified for blittable struct)
            entry.FilePathHash = filePath.GetHashCode();

            // Insert in priority order
            int insertIndex = FindInsertPosition(entry);

            // Shift existing entries
            m_Queue.Add(default); // Add space
            for (int i = m_Queue.Length - 1; i > insertIndex; i--)
            {
                m_Queue[i] = m_Queue[i - 1];

                // Update index mapping
                var movedEntry = m_Queue[i];
                m_FileIdToIndex[movedEntry.FileId] = i;
            }

            // Insert new entry
            m_Queue[insertIndex] = entry;
            m_FileIdToIndex[fileId] = insertIndex;

            return fileId;
        }

        public bool TryDequeue(out FileQueueEntry entry)
        {
            entry = default;

            if (!m_IsCreated || m_IsDisposed || !m_Queue.IsCreated || m_Queue.Length == 0) return false;

            // Get highest priority entry (first in queue)
            entry = m_Queue[0];

            // Remove from queue
            RemoveAt(0);

            return true;
        }

        public bool TryPeek(out FileQueueEntry entry)
        {
            entry = default;

            if (!m_IsCreated || m_IsDisposed || !m_Queue.IsCreated || m_Queue.Length == 0) return false;

            entry = m_Queue[0];
            return true;
        }

        public bool UpdateFilePriority(int fileId, FilePriority newPriority)
        {
            if (!m_FileIdToIndex.TryGetValue(fileId, out int currentIndex))
                return false;

            var entry = m_Queue[currentIndex];
            if (entry.Priority == newPriority) return true;

            // Remove from current position
            RemoveAt(currentIndex);

            // Update priority and re-insert
            entry.Priority = newPriority;
            int newIndex = FindInsertPosition(entry);

            m_Queue.Add(default);
            for (int i = m_Queue.Length - 1; i > newIndex; i--)
            {
                m_Queue[i] = m_Queue[i - 1];
                var movedEntry = m_Queue[i];
                m_FileIdToIndex[movedEntry.FileId] = i;
            }

            m_Queue[newIndex] = entry;
            m_FileIdToIndex[fileId] = newIndex;

            return true;
        }

        public bool UpdateFileStatus(int fileId, FileQueueStatus status)
        {
            if (!m_FileIdToIndex.TryGetValue(fileId, out int index))
                return false;

            var entry = m_Queue[index];
            entry.Status = status;
            m_Queue[index] = entry;

            return true;
        }

        public bool RemoveFile(int fileId)
        {
            if (!m_FileIdToIndex.TryGetValue(fileId, out int index))
                return false;

            RemoveAt(index);
            return true;
        }

        public FileQueueEntry[] GetQueueSnapshot()
        {
            if (!m_IsCreated || m_Queue.Length == 0)
                return new FileQueueEntry[0];

            var snapshot = new FileQueueEntry[m_Queue.Length];
            for (int i = 0; i < m_Queue.Length; i++)
            {
                snapshot[i] = m_Queue[i];
            }

            return snapshot;
        }

        public void Clear()
        {
            if (m_IsCreated)
            {
                m_Queue.Clear();
                m_FileIdToIndex.Clear();
            }
        }

        private int FindInsertPosition(FileQueueEntry entry)
        {
            int left = 0;
            int right = m_Queue.Length;

            while (left < right)
            {
                int mid = (left + right) / 2;
                if (ComparePriority(entry, m_Queue[mid]) < 0)
                {
                    right = mid;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return left;
        }

        private int ComparePriority(FileQueueEntry a, FileQueueEntry b)
        {
            // Higher priority values come first (reverse comparison for descending order)
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            if (priorityCompare != 0) return priorityCompare;

            // Then by queue time (earlier first)
            return a.QueueTime.CompareTo(b.QueueTime);
        }

        private void RemoveAt(int index)
        {
            if (index < 0 || index >= m_Queue.Length) return;

            var removedEntry = m_Queue[index];
            m_FileIdToIndex.Remove(removedEntry.FileId);

            // Shift entries
            for (int i = index; i < m_Queue.Length - 1; i++)
            {
                m_Queue[i] = m_Queue[i + 1];
                var movedEntry = m_Queue[i];
                m_FileIdToIndex[movedEntry.FileId] = i;
            }

            m_Queue.RemoveAt(m_Queue.Length - 1);
        }

        private static long GetFileSize(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    return fileInfo.Length;
                }
            }
            catch
            {
                // Ignore errors
            }
            return 0;
        }

        public void Dispose()
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;
            m_IsCreated = false;

            try
            {
                if (m_Queue.IsCreated)
                {
                    m_Queue.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_FileIdToIndex.IsCreated)
                {
                    m_FileIdToIndex.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileQueueEntry : IEquatable<FileQueueEntry>
    {
        public int FileId;
        public FilePriority Priority;
        public FileCategory Category;
        public long QueueTime;
        public long FileSize;
        public FileQueueStatus Status;
        public int FilePathHash;

        public bool Equals(FileQueueEntry other)
        {
            return FileId == other.FileId;
        }

        public override bool Equals(object obj)
        {
            return obj is FileQueueEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return FileId.GetHashCode();
        }

        public override string ToString()
        {
            return $"File {FileId}: {Priority} priority, {Category} category, {Status} status";
        }
    }

    public enum FilePriority : byte
    {
        Lowest = 0,
        Low = 1,
        Normal = 2,
        High = 3,
        Highest = 4,
        Critical = 5
    }

    public enum FileCategory : byte
    {
        Other = 0,
        Map = 1,
        History = 2,
        Common = 3,
        Localisation = 4,
        Events = 5,
        Decisions = 6,
        Gfx = 7,
        Music = 8
    }

    public enum FileQueueStatus : byte
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public static class FilePriorityUtilities
    {
        public static FilePriority GetPriorityForCategory(FileCategory category)
        {
            return category switch
            {
                FileCategory.Map => FilePriority.Highest,
                FileCategory.Common => FilePriority.High,
                FileCategory.History => FilePriority.High,
                FileCategory.Localisation => FilePriority.Normal,
                FileCategory.Events => FilePriority.Normal,
                FileCategory.Decisions => FilePriority.Normal,
                FileCategory.Gfx => FilePriority.Low,
                FileCategory.Music => FilePriority.Lowest,
                _ => FilePriority.Normal
            };
        }

        public static FileCategory GetCategoryFromPath(string filePath)
        {
            string path = filePath.ToLowerInvariant();

            if (path.Contains("/map/") || path.Contains("\\map\\") || path.EndsWith(".bmp"))
                return FileCategory.Map;
            if (path.Contains("/history/") || path.Contains("\\history\\"))
                return FileCategory.History;
            if (path.Contains("/common/") || path.Contains("\\common\\"))
                return FileCategory.Common;
            if (path.Contains("/localisation/") || path.Contains("\\localisation\\") ||
                path.Contains("/localization/") || path.Contains("\\localization\\"))
                return FileCategory.Localisation;
            if (path.Contains("/events/") || path.Contains("\\events\\"))
                return FileCategory.Events;
            if (path.Contains("/decisions/") || path.Contains("\\decisions\\"))
                return FileCategory.Decisions;
            if (path.Contains("/gfx/") || path.Contains("\\gfx\\") ||
                path.Contains("/interface/") || path.Contains("\\interface\\"))
                return FileCategory.Gfx;
            if (path.Contains("/music/") || path.Contains("\\music\\") ||
                path.Contains("/sound/") || path.Contains("\\sound\\"))
                return FileCategory.Music;

            return FileCategory.Other;
        }

        public static FilePriority GetPriorityForFileSize(long fileSize)
        {
            // Prioritize smaller files for faster initial loading
            if (fileSize < 1024) return FilePriority.High;           // < 1KB
            if (fileSize < 10 * 1024) return FilePriority.Normal;    // < 10KB
            if (fileSize < 100 * 1024) return FilePriority.Low;      // < 100KB
            return FilePriority.Lowest;                              // >= 100KB
        }
    }
}