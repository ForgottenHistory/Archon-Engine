using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public class FileChangeWatcher : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> m_Watchers;
        private readonly Dictionary<string, FileChangeInfo> m_TrackedFiles;
        private readonly Queue<FileChangeEvent> m_ChangeEvents;
        private bool m_IsDisposed;

        public event Action<FileChangeEvent> OnFileChanged;

        public bool IsEnabled { get; set; } = true;
        public int QueuedEvents => m_ChangeEvents.Count;

        public FileChangeWatcher()
        {
            m_Watchers = new Dictionary<string, FileSystemWatcher>();
            m_TrackedFiles = new Dictionary<string, FileChangeInfo>();
            m_ChangeEvents = new Queue<FileChangeEvent>();
        }

        public void WatchDirectory(string directoryPath, string filter = "*", bool includeSubdirectories = false)
        {
            if (m_IsDisposed || !Directory.Exists(directoryPath))
            {
                Debug.LogWarning($"[FileChangeWatcher] Cannot watch directory: {directoryPath}");
                return;
            }

            string normalizedPath = Path.GetFullPath(directoryPath);

            if (m_Watchers.ContainsKey(normalizedPath))
            {
                Debug.LogWarning($"[FileChangeWatcher] Directory already being watched: {normalizedPath}");
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(normalizedPath, filter)
                {
                    IncludeSubdirectories = includeSubdirectories,
                    EnableRaisingEvents = false
                };

                watcher.Changed += OnFileSystemChanged;
                watcher.Created += OnFileSystemCreated;
                watcher.Deleted += OnFileSystemDeleted;
                watcher.Renamed += OnFileSystemRenamed;

                m_Watchers[normalizedPath] = watcher;
                watcher.EnableRaisingEvents = true;

                Debug.Log($"[FileChangeWatcher] Now watching: {normalizedPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileChangeWatcher] Failed to watch directory {directoryPath}: {ex.Message}");
            }
        }

        public void WatchFile(string filePath)
        {
            if (m_IsDisposed || !File.Exists(filePath))
            {
                Debug.LogWarning($"[FileChangeWatcher] Cannot watch file: {filePath}");
                return;
            }

            string normalizedPath = Path.GetFullPath(filePath);
            string directoryPath = Path.GetDirectoryName(normalizedPath);
            string fileName = Path.GetFileName(normalizedPath);

            if (!m_TrackedFiles.ContainsKey(normalizedPath))
            {
                var fileInfo = new FileInfo(normalizedPath);
                m_TrackedFiles[normalizedPath] = new FileChangeInfo
                {
                    FilePath = normalizedPath,
                    LastWriteTime = fileInfo.LastWriteTime,
                    Size = fileInfo.Length
                };
            }

            // Watch the directory if not already watching
            if (!m_Watchers.ContainsKey(directoryPath))
            {
                WatchDirectory(directoryPath, fileName, false);
            }
        }

        public void StopWatching(string path)
        {
            string normalizedPath = Path.GetFullPath(path);

            if (m_Watchers.TryGetValue(normalizedPath, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                m_Watchers.Remove(normalizedPath);
                Debug.Log($"[FileChangeWatcher] Stopped watching: {normalizedPath}");
            }

            // Remove tracked files in this directory
            var filesToRemove = new List<string>();
            foreach (var kvp in m_TrackedFiles)
            {
                if (kvp.Key.StartsWith(normalizedPath))
                {
                    filesToRemove.Add(kvp.Key);
                }
            }

            foreach (var file in filesToRemove)
            {
                m_TrackedFiles.Remove(file);
            }
        }

        public FileChangeEvent[] ProcessQueuedEvents()
        {
            if (m_ChangeEvents.Count == 0) return new FileChangeEvent[0];

            var events = new FileChangeEvent[m_ChangeEvents.Count];
            int index = 0;

            while (m_ChangeEvents.Count > 0)
            {
                events[index++] = m_ChangeEvents.Dequeue();
            }

            return events;
        }

        public bool HasFileChanged(string filePath)
        {
            string normalizedPath = Path.GetFullPath(filePath);

            if (!m_TrackedFiles.TryGetValue(normalizedPath, out var trackedInfo))
            {
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                return true; // File was deleted
            }

            var fileInfo = new FileInfo(normalizedPath);
            return fileInfo.LastWriteTime != trackedInfo.LastWriteTime ||
                   fileInfo.Length != trackedInfo.Size;
        }

        public void RefreshFileInfo(string filePath)
        {
            string normalizedPath = Path.GetFullPath(filePath);

            if (File.Exists(normalizedPath))
            {
                var fileInfo = new FileInfo(normalizedPath);
                m_TrackedFiles[normalizedPath] = new FileChangeInfo
                {
                    FilePath = normalizedPath,
                    LastWriteTime = fileInfo.LastWriteTime,
                    Size = fileInfo.Length
                };
            }
            else
            {
                m_TrackedFiles.Remove(normalizedPath);
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsEnabled) return;
            QueueChangeEvent(e.FullPath, FileChangeType.Modified);
        }

        private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
        {
            if (!IsEnabled) return;
            QueueChangeEvent(e.FullPath, FileChangeType.Created);
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            if (!IsEnabled) return;
            QueueChangeEvent(e.FullPath, FileChangeType.Deleted);
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            if (!IsEnabled) return;

            QueueChangeEvent(e.OldFullPath, FileChangeType.Deleted);
            QueueChangeEvent(e.FullPath, FileChangeType.Created);
        }

        private void QueueChangeEvent(string filePath, FileChangeType changeType)
        {
            var changeEvent = new FileChangeEvent
            {
                FilePath = filePath,
                ChangeType = changeType,
                Timestamp = DateTime.Now
            };

            m_ChangeEvents.Enqueue(changeEvent);

            // Update tracked file info if it's being tracked
            if (m_TrackedFiles.ContainsKey(filePath))
            {
                RefreshFileInfo(filePath);
            }

            // Invoke event on main thread (Unity will handle this)
            OnFileChanged?.Invoke(changeEvent);
        }

        public void Dispose()
        {
            if (m_IsDisposed) return;

            foreach (var watcher in m_Watchers.Values)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileChangeWatcher] Error disposing watcher: {ex.Message}");
                }
            }

            m_Watchers.Clear();
            m_TrackedFiles.Clear();
            m_ChangeEvents.Clear();
            m_IsDisposed = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileChangeEvent
    {
        public string FilePath;
        public FileChangeType ChangeType;
        public DateTime Timestamp;

        public override string ToString()
        {
            return $"{ChangeType}: {FilePath} at {Timestamp:HH:mm:ss}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileChangeInfo
    {
        public string FilePath;
        public DateTime LastWriteTime;
        public long Size;
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted,
        Renamed
    }

    public static class HotReloadUtilities
    {
        private static FileChangeWatcher s_GlobalWatcher;

        public static FileChangeWatcher GlobalWatcher
        {
            get
            {
                if (s_GlobalWatcher == null)
                {
                    s_GlobalWatcher = new FileChangeWatcher();
                }
                return s_GlobalWatcher;
            }
        }

        public static void InitializeHotReload(string gameDataPath)
        {
            var watcher = GlobalWatcher;

            // Watch common Paradox data directories
            string[] watchPaths = {
                Path.Combine(gameDataPath, "map"),
                Path.Combine(gameDataPath, "history"),
                Path.Combine(gameDataPath, "common"),
                Path.Combine(gameDataPath, "localisation")
            };

            foreach (string path in watchPaths)
            {
                if (Directory.Exists(path))
                {
                    watcher.WatchDirectory(path, "*", true);
                    Debug.Log($"[HotReload] Watching: {path}");
                }
            }
        }

        public static void ShutdownHotReload()
        {
            s_GlobalWatcher?.Dispose();
            s_GlobalWatcher = null;
        }

        public static bool IsParadoxDataFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".txt" || extension == ".csv" || extension == ".yml" || extension == ".bmp";
        }
    }
}