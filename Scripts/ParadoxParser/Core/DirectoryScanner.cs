using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ParadoxParser.Core
{
    public static class DirectoryScanner
    {
        public static ScanResult ScanDirectory(string directoryPath, ScanOptions options = default)
        {
            var result = new ScanResult();

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    result.ErrorMessage = $"Directory not found: {directoryPath}";
                    return result;
                }

                var directory = new DirectoryInfo(directoryPath);
                var searchOption = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                // Get files matching patterns
                var allFiles = new List<FileInfo>();

                if (options.FilePatterns == null || options.FilePatterns.Length == 0)
                {
                    allFiles.AddRange(directory.GetFiles("*", searchOption));
                }
                else
                {
                    foreach (string pattern in options.FilePatterns)
                    {
                        try
                        {
                            var files = directory.GetFiles(pattern, searchOption);
                            allFiles.AddRange(files);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[DirectoryScanner] Error with pattern '{pattern}': {ex.Message}");
                        }
                    }
                }

                // Remove duplicates and apply filters
                var uniqueFiles = allFiles.Distinct().ToList();

                if (options.ExcludePatterns != null && options.ExcludePatterns.Length > 0)
                {
                    uniqueFiles = FilterExcludedFiles(uniqueFiles, options.ExcludePatterns);
                }

                if (options.MaxFileSize > 0)
                {
                    uniqueFiles = uniqueFiles.Where(f => f.Length <= options.MaxFileSize).ToList();
                }

                if (options.MinFileSize > 0)
                {
                    uniqueFiles = uniqueFiles.Where(f => f.Length >= options.MinFileSize).ToList();
                }

                if (options.MaxFiles > 0 && uniqueFiles.Count > options.MaxFiles)
                {
                    uniqueFiles = uniqueFiles.Take(options.MaxFiles).ToList();
                }

                // Sort files if requested
                if (options.SortBy != FileSortBy.None)
                {
                    uniqueFiles = SortFiles(uniqueFiles, options.SortBy, options.SortDescending);
                }

                result.Files = uniqueFiles.ToArray();
                result.Success = true;
                result.TotalFiles = uniqueFiles.Count;
                result.TotalSize = uniqueFiles.Sum(f => f.Length);
                result.ScanTime = DateTime.Now;

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public static ParadoxFileResult ScanForParadoxFiles(string gameDataPath)
        {
            var result = new ParadoxFileResult();

            try
            {
                if (!Directory.Exists(gameDataPath))
                {
                    result.ErrorMessage = $"Game data path not found: {gameDataPath}";
                    return result;
                }

                var categories = new Dictionary<string, List<FileInfo>>
                {
                    ["Map"] = new List<FileInfo>(),
                    ["History"] = new List<FileInfo>(),
                    ["Common"] = new List<FileInfo>(),
                    ["Localisation"] = new List<FileInfo>(),
                    ["Events"] = new List<FileInfo>(),
                    ["Decisions"] = new List<FileInfo>(),
                    ["Gfx"] = new List<FileInfo>(),
                    ["Music"] = new List<FileInfo>(),
                    ["Other"] = new List<FileInfo>()
                };

                // Define category mappings
                var categoryMappings = new Dictionary<string, string[]>
                {
                    ["Map"] = new[] { "map", "provinces.bmp", "terrain.bmp", "rivers.bmp" },
                    ["History"] = new[] { "history" },
                    ["Common"] = new[] { "common" },
                    ["Localisation"] = new[] { "localisation", "localization" },
                    ["Events"] = new[] { "events" },
                    ["Decisions"] = new[] { "decisions" },
                    ["Gfx"] = new[] { "gfx", "interface" },
                    ["Music"] = new[] { "music", "sound" }
                };

                // Scan each directory
                foreach (var mapping in categoryMappings)
                {
                    string category = mapping.Key;
                    string[] paths = mapping.Value;

                    foreach (string path in paths)
                    {
                        string fullPath = Path.Combine(gameDataPath, path);
                        if (Directory.Exists(fullPath))
                        {
                            var options = new ScanOptions
                            {
                                IncludeSubdirectories = true,
                                FilePatterns = GetPatternsForCategory(category),
                                MaxFiles = 10000
                            };

                            var scanResult = ScanDirectory(fullPath, options);
                            if (scanResult.Success)
                            {
                                categories[category].AddRange(scanResult.Files);
                            }
                        }
                    }
                }

                // Assign results
                result.MapFiles = categories["Map"].ToArray();
                result.HistoryFiles = categories["History"].ToArray();
                result.CommonFiles = categories["Common"].ToArray();
                result.LocalisationFiles = categories["Localisation"].ToArray();
                result.EventFiles = categories["Events"].ToArray();
                result.DecisionFiles = categories["Decisions"].ToArray();
                result.GfxFiles = categories["Gfx"].ToArray();
                result.MusicFiles = categories["Music"].ToArray();
                result.OtherFiles = categories["Other"].ToArray();

                result.Success = true;
                result.TotalFiles = categories.Values.SelectMany(files => files).Count();
                result.ScanTime = DateTime.Now;

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public static BatchScanResult ScanDirectoriesBatch(string[] directoryPaths, ScanOptions options = default)
        {
            var batchResult = new BatchScanResult
            {
                Results = new ScanResult[directoryPaths.Length],
                StartTime = DateTime.Now
            };

            for (int i = 0; i < directoryPaths.Length; i++)
            {
                batchResult.Results[i] = ScanDirectory(directoryPaths[i], options);
            }

            batchResult.EndTime = DateTime.Now;
            batchResult.Success = batchResult.Results.All(r => r.Success);
            batchResult.TotalFiles = batchResult.Results.Sum(r => r.TotalFiles);

            return batchResult;
        }

        private static List<FileInfo> FilterExcludedFiles(List<FileInfo> files, string[] excludePatterns)
        {
            var filteredFiles = new List<FileInfo>();

            foreach (var file in files)
            {
                bool shouldExclude = false;

                foreach (string pattern in excludePatterns)
                {
                    if (IsMatch(file.Name, pattern) || IsMatch(file.FullName, pattern))
                    {
                        shouldExclude = true;
                        break;
                    }
                }

                if (!shouldExclude)
                {
                    filteredFiles.Add(file);
                }
            }

            return filteredFiles;
        }

        private static List<FileInfo> SortFiles(List<FileInfo> files, FileSortBy sortBy, bool descending)
        {
            IOrderedEnumerable<FileInfo> sortedFiles;

            switch (sortBy)
            {
                case FileSortBy.Name:
                    sortedFiles = descending ? files.OrderByDescending(f => f.Name) : files.OrderBy(f => f.Name);
                    break;
                case FileSortBy.Size:
                    sortedFiles = descending ? files.OrderByDescending(f => f.Length) : files.OrderBy(f => f.Length);
                    break;
                case FileSortBy.Date:
                    sortedFiles = descending ? files.OrderByDescending(f => f.LastWriteTime) : files.OrderBy(f => f.LastWriteTime);
                    break;
                case FileSortBy.Extension:
                    sortedFiles = descending ? files.OrderByDescending(f => f.Extension) : files.OrderBy(f => f.Extension);
                    break;
                default:
                    return files;
            }

            return sortedFiles.ToList();
        }

        private static string[] GetPatternsForCategory(string category)
        {
            return category switch
            {
                "Map" => new[] { "*.txt", "*.bmp", "*.csv" },
                "History" => new[] { "*.txt" },
                "Common" => new[] { "*.txt" },
                "Localisation" => new[] { "*.yml", "*.csv" },
                "Events" => new[] { "*.txt" },
                "Decisions" => new[] { "*.txt" },
                "Gfx" => new[] { "*.gfx", "*.gui", "*.dds", "*.tga" },
                "Music" => new[] { "*.ogg", "*.wav" },
                _ => new[] { "*" }
            };
        }

        private static bool IsMatch(string text, string pattern)
        {
            if (pattern.Contains("*"))
            {
                return text.Contains(pattern.Replace("*", ""));
            }
            return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScanOptions
    {
        public bool IncludeSubdirectories;
        public string[] FilePatterns;
        public string[] ExcludePatterns;
        public long MaxFileSize;
        public long MinFileSize;
        public int MaxFiles;
        public FileSortBy SortBy;
        public bool SortDescending;

        public static ScanOptions Default => new ScanOptions
        {
            IncludeSubdirectories = true,
            FilePatterns = null,
            ExcludePatterns = null,
            MaxFileSize = 0,
            MinFileSize = 0,
            MaxFiles = 0,
            SortBy = FileSortBy.None,
            SortDescending = false
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScanResult
    {
        public bool Success;
        public string ErrorMessage;
        public FileInfo[] Files;
        public int TotalFiles;
        public long TotalSize;
        public DateTime ScanTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParadoxFileResult
    {
        public bool Success;
        public string ErrorMessage;
        public FileInfo[] MapFiles;
        public FileInfo[] HistoryFiles;
        public FileInfo[] CommonFiles;
        public FileInfo[] LocalisationFiles;
        public FileInfo[] EventFiles;
        public FileInfo[] DecisionFiles;
        public FileInfo[] GfxFiles;
        public FileInfo[] MusicFiles;
        public FileInfo[] OtherFiles;
        public int TotalFiles;
        public DateTime ScanTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BatchScanResult
    {
        public bool Success;
        public ScanResult[] Results;
        public int TotalFiles;
        public DateTime StartTime;
        public DateTime EndTime;

        public TimeSpan Duration => EndTime - StartTime;
    }

    public enum FileSortBy
    {
        None,
        Name,
        Size,
        Date,
        Extension
    }
}