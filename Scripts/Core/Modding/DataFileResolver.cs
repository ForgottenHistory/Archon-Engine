using System.IO;
using UnityEngine;
using Utils;

namespace Core.Modding
{
    /// <summary>
    /// ENGINE LAYER: Resolves data file paths with override-first logic.
    ///
    /// Paradox-style file layering:
    /// 1. Override directory (StreamingAssets/Data/) — user/editor modifications
    /// 2. Base directory (Template-Data or game-specific data) — original read-only data
    ///
    /// When reading: returns override path if it exists, otherwise base path.
    /// When writing: always writes to override directory (never modifies base data).
    ///
    /// Usage:
    ///   // Reading — automatically picks override if available
    ///   string path = DataFileResolver.Resolve("map/terrain.json5");
    ///
    ///   // Writing — get the override path to write to
    ///   string writePath = DataFileResolver.GetWritePath("history/provinces/123-Roma.json5");
    ///
    ///   // Check if an override exists
    ///   bool overridden = DataFileResolver.HasOverride("map/terrain.json5");
    /// </summary>
    public static class DataFileResolver
    {
        private static string baseDirectory;
        private static string overrideDirectory;
        private static bool isInitialized;

        /// <summary>
        /// The base (read-only) data directory.
        /// </summary>
        public static string BaseDirectory => baseDirectory;

        /// <summary>
        /// The override (writable) data directory in StreamingAssets.
        /// </summary>
        public static string OverrideDirectory => overrideDirectory;

        /// <summary>
        /// Whether the resolver has been initialized.
        /// </summary>
        public static bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize with base and override directories.
        /// Call once during engine startup, after GameSettings is available.
        /// </summary>
        public static void Initialize(string baseDir, string overrideDir = null)
        {
            baseDirectory = baseDir;

            // Override directory MUST be outside Assets/ to avoid Unity's file watcher
            // triggering AssetDatabase.Refresh() on file changes (which disrupts GPU
            // resources during play mode, causing gray tiles).
            // Application.persistentDataPath → AppData/LocalLow/{company}/{product}/
            overrideDirectory = overrideDir ?? Path.Combine(Application.persistentDataPath, "DataOverrides");

            // Ensure override directory exists
            if (!Directory.Exists(overrideDirectory))
            {
                Directory.CreateDirectory(overrideDirectory);
                ArchonLogger.Log($"DataFileResolver: Created override directory: {overrideDirectory}", "core_modding");
            }

            isInitialized = true;
            ArchonLogger.Log($"DataFileResolver: Initialized\n  Base: {baseDirectory}\n  Override: {overrideDirectory}", "core_modding");
        }

        /// <summary>
        /// Resolve a relative path: returns override path if it exists, otherwise base path.
        /// Use this for all file reads.
        /// </summary>
        public static string Resolve(string relativePath)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("DataFileResolver: Not initialized, using relative path as-is", "core_modding");
                return relativePath;
            }

            // Check override first
            string overridePath = Path.Combine(overrideDirectory, relativePath);
            if (File.Exists(overridePath))
                return overridePath;

            // Fall back to base
            return Path.Combine(baseDirectory, relativePath);
        }

        /// <summary>
        /// Resolve a directory: returns override path if it exists, otherwise base path.
        /// </summary>
        public static string ResolveDirectory(string relativePath)
        {
            if (!isInitialized)
                return relativePath;

            string overridePath = Path.Combine(overrideDirectory, relativePath);
            if (Directory.Exists(overridePath))
                return overridePath;

            return Path.Combine(baseDirectory, relativePath);
        }

        /// <summary>
        /// Get the writable path for a file (always in override directory).
        /// Creates parent directories if needed.
        /// </summary>
        public static string GetWritePath(string relativePath)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("DataFileResolver: Not initialized", "core_modding");
                return relativePath;
            }

            string writePath = Path.Combine(overrideDirectory, relativePath);
            string dir = Path.GetDirectoryName(writePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return writePath;
        }

        /// <summary>
        /// Check if an override exists for the given relative path.
        /// </summary>
        public static bool HasOverride(string relativePath)
        {
            if (!isInitialized) return false;
            return File.Exists(Path.Combine(overrideDirectory, relativePath));
        }

        /// <summary>
        /// List all files matching a pattern, merging base and override directories.
        /// Override files replace base files with the same name.
        /// </summary>
        public static string[] ListFiles(string relativeDir, string searchPattern)
        {
            if (!isInitialized)
                return new string[0];

            var files = new System.Collections.Generic.Dictionary<string, string>();

            // Base files first
            string baseDir = Path.Combine(baseDirectory, relativeDir);
            if (Directory.Exists(baseDir))
            {
                foreach (string file in Directory.GetFiles(baseDir, searchPattern))
                {
                    string name = Path.GetFileName(file);
                    files[name] = file;
                }
            }

            // Override files replace base files with same name
            string overDir = Path.Combine(overrideDirectory, relativeDir);
            if (Directory.Exists(overDir))
            {
                foreach (string file in Directory.GetFiles(overDir, searchPattern))
                {
                    string name = Path.GetFileName(file);
                    files[name] = file; // Overrides base
                }
            }

            var result = new string[files.Count];
            files.Values.CopyTo(result, 0);
            return result;
        }
    }
}
