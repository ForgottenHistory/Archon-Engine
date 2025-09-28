using System;
using System.Collections.Generic;
using System.IO;
using ParadoxParser.Core;
using ParadoxParser.Data;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;
using Unity.Collections;
using UnityEngine;
using Utils;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

namespace Core.Loaders
{
    /// <summary>
    /// Generic manifest loader implementing Pattern 2: Manifest/Reference
    /// Loads index files that point to actual data files
    /// Examples: country_tags → country files, bookmarks → bookmark definitions
    /// Following paradox-data-patterns-guide.md specifications
    /// </summary>
    public class ManifestLoader<T> where T : class
    {
        protected Dictionary<string, string> manifest = new();
        protected List<string> errors = new();

        /// <summary>
        /// Load manifest file and return tag → file path mapping
        /// Phase 1 of manifest pattern: discovery
        /// </summary>
        public virtual ManifestLoadResult LoadManifest(string manifestFilePath)
        {
            manifest.Clear();
            errors.Clear();

            if (!File.Exists(manifestFilePath))
            {
                var error = $"Manifest file not found: {manifestFilePath}";
                errors.Add(error);
                return ManifestLoadResult.CreateFailed(error);
            }

            try
            {
                DominionLogger.Log($"ManifestLoader: Loading manifest from {manifestFilePath}");

                // Read and parse file content directly (simple approach for small manifest files)
                var lines = File.ReadAllLines(manifestFilePath);
                ParseManifestLines(lines);

                DominionLogger.Log($"ManifestLoader: Loaded {manifest.Count} entries from manifest");

                return ManifestLoadResult.CreateSuccess(manifest, errors);
            }
            catch (Exception e)
            {
                var error = $"Failed to load manifest {manifestFilePath}: {e.Message}";
                errors.Add(error);
                DominionLogger.LogError(error);
                return ManifestLoadResult.CreateFailed(error);
            }
        }

        /// <summary>
        /// Parse manifest lines into key-value pairs
        /// Handles standard Paradox manifest format: KEY = "path/to/file.txt"
        /// </summary>
        protected virtual void ParseManifestLines(string[] lines)
        {
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                // Look for pattern: KEY = "value"
                var equalsIndex = trimmedLine.IndexOf('=');
                if (equalsIndex > 0)
                {
                    var key = trimmedLine.Substring(0, equalsIndex).Trim();
                    var value = trimmedLine.Substring(equalsIndex + 1).Trim();

                    // Skip comments and directives
                    if (IsComment(key) || IsDirective(key))
                        continue;

                    // Clean quotes from value
                    value = value.Trim('"');

                    if (!manifest.ContainsKey(key))
                    {
                        manifest[key] = value;
                    }
                    else
                    {
                        var warning = $"Duplicate key in manifest: {key}";
                        errors.Add(warning);
                        DominionLogger.LogWarning(warning);
                    }
                }
            }
        }

        /// <summary>
        /// Check if key is a comment line
        /// </summary>
        protected virtual bool IsComment(string key)
        {
            return key.StartsWith("#");
        }

        /// <summary>
        /// Check if key is a directive (e.g., @include)
        /// </summary>
        protected virtual bool IsDirective(string key)
        {
            return key.StartsWith("@");
        }

        /// <summary>
        /// Get all manifest entries
        /// </summary>
        public IReadOnlyDictionary<string, string> GetManifest()
        {
            return manifest;
        }

        /// <summary>
        /// Get all loading errors
        /// </summary>
        public IReadOnlyList<string> GetErrors()
        {
            return errors;
        }

        /// <summary>
        /// Check if manifest loaded successfully
        /// </summary>
        public bool HasErrors()
        {
            return errors.Count > 0;
        }
    }

    /// <summary>
    /// Result of manifest loading operation
    /// </summary>
    public struct ManifestLoadResult
    {
        public bool Success;
        public Dictionary<string, string> Manifest;
        public List<string> Errors;
        public string ErrorMessage;

        public static ManifestLoadResult CreateSuccess(Dictionary<string, string> manifest, List<string> errors)
        {
            return new ManifestLoadResult
            {
                Success = true,
                Manifest = new Dictionary<string, string>(manifest),
                Errors = new List<string>(errors)
            };
        }

        public static ManifestLoadResult CreateFailed(string error)
        {
            return new ManifestLoadResult
            {
                Success = false,
                ErrorMessage = error,
                Errors = new List<string> { error }
            };
        }
    }
}