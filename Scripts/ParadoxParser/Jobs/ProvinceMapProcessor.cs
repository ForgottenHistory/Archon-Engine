using System;
using System.Threading.Tasks;
using Unity.Collections;
using ParadoxParser.Bitmap;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// High-performance province map processor that combines BMP loading with definition CSV
    /// Specializes the generic JobifiedBMPLoader for province map use cases
    /// </summary>
    public class ProvinceMapProcessor
    {
        public struct ProcessingProgress
        {
            public float ProgressPercentage;
            public string CurrentOperation;
        }

        public event System.Action<ProcessingProgress> OnProgressUpdate;

        private JobifiedBMPLoader bmpLoader;
        private JobifiedDefinitionLoader definitionLoader;

        public ProvinceMapProcessor()
        {
            bmpLoader = new JobifiedBMPLoader();
            definitionLoader = new JobifiedDefinitionLoader();

            // Forward progress events
            bmpLoader.OnProgressUpdate += OnBMPProgress;
            definitionLoader.OnProgressUpdate += OnDefinitionProgress;
        }

        /// <summary>
        /// Load and process a complete province map (BMP + optional definition CSV)
        /// </summary>
        public async Task<ProvinceMapResult> LoadProvinceMapAsync(string bmpFilePath, string definitionCsvPath = null)
        {
            try
            {
                ReportProgress(0.0f, "Starting province map loading...");

                // Step 1: Load BMP file (0% - 40%)
                ReportProgress(0.1f, "Loading province bitmap...");
                var bmpResult = await bmpLoader.LoadBMPAsync(bmpFilePath);

                if (!bmpResult.Success)
                {
                    return new ProvinceMapResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to load BMP: {bmpResult.ErrorMessage}"
                    };
                }

                // Step 2: Load definition CSV if provided (40% - 60%)
                ProvinceDefinitionMappings definitions = default;
                bool hasDefinitions = false;

                if (!string.IsNullOrEmpty(definitionCsvPath))
                {
                    ReportProgress(0.4f, "Loading province definitions...");
                    var defResult = await definitionLoader.LoadDefinitionAsync(definitionCsvPath);

                    if (defResult.Success)
                    {
                        definitions = defResult.Definitions;
                        hasDefinitions = true;
                    }
                    else
                    {
                        // Continue without definitions, but log warning
                        UnityEngine.Debug.LogWarning($"Failed to load definitions: {defResult.ErrorMessage}");
                    }
                }

                // Step 3: Process province mappings (60% - 80%)
                ReportProgress(0.6f, "Processing province mappings...");
                var provinceMappings = ProcessProvinceMappings(bmpResult, definitions, hasDefinitions);

                // Step 4: Generate statistics (80% - 100%)
                ReportProgress(0.8f, "Calculating province statistics...");
                var stats = CalculateProvinceStatistics(bmpResult, provinceMappings);

                ReportProgress(1.0f, "Province map loading complete!");

                return new ProvinceMapResult
                {
                    Success = true,
                    BMPData = bmpResult,
                    Definitions = hasDefinitions ? definitions : default,
                    HasDefinitions = hasDefinitions,
                    ProvinceMappings = provinceMappings,
                    ProvinceStats = stats
                };
            }
            catch (Exception e)
            {
                return new ProvinceMapResult
                {
                    Success = false,
                    ErrorMessage = $"Exception during province map processing: {e.Message}"
                };
            }
        }

        /// <summary>
        /// Process color-to-province mappings from BMP and optional definitions
        /// </summary>
        private ProvinceColorMappings ProcessProvinceMappings(BMPLoadResult bmpResult, ProvinceDefinitionMappings definitions, bool hasDefinitions)
        {
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var mappings = new ProvinceColorMappings
            {
                ColorToProvinceID = new NativeHashMap<int, int>(bmpResult.UniqueColors.Count, Allocator.Persistent)
            };

            if (hasDefinitions)
            {
                // Use definition file mappings
                var colorEnumerator = bmpResult.UniqueColors.GetEnumerator();
                while (colorEnumerator.MoveNext())
                {
                    int color = colorEnumerator.Current;
                    if (definitions.ColorToID.TryGetValue(color, out int provinceID))
                    {
                        mappings.ColorToProvinceID[color] = provinceID;
                    }
                    else
                    {
                        // Color not in definition file - assign sequential ID
                        int fallbackID = 65000 + mappings.ColorToProvinceID.Count;
                        mappings.ColorToProvinceID[color] = fallbackID;
                        UnityEngine.Debug.LogWarning($"Color 0x{color:X6} not found in definitions, assigned ID {fallbackID}");
                    }
                }
            }
            else
            {
                // No definitions - assign sequential IDs based on unique colors
                int provinceID = 1;
                var colorEnumerator = bmpResult.UniqueColors.GetEnumerator();
                while (colorEnumerator.MoveNext())
                {
                    int color = colorEnumerator.Current;
                    mappings.ColorToProvinceID[color] = provinceID++;
                }
            }

            return mappings;
        }

        /// <summary>
        /// Calculate basic province statistics (pixel counts, bounds, etc.)
        /// </summary>
        private NativeHashMap<int, ProvinceStats> CalculateProvinceStatistics(BMPLoadResult bmpResult, ProvinceColorMappings mappings)
        {
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var stats = new NativeHashMap<int, ProvinceStats>(mappings.ColorToProvinceID.Count, Allocator.Persistent);
            var pixelData = bmpResult.GetPixelData();

            // Initialize stats for each province
            var mappingEnumerator = mappings.ColorToProvinceID.GetEnumerator();
            while (mappingEnumerator.MoveNext())
            {
                int provinceID = mappingEnumerator.Current.Value;
                stats[provinceID] = new ProvinceStats
                {
                    ProvinceID = provinceID,
                    PixelCount = 0,
                    MinX = int.MaxValue,
                    MinY = int.MaxValue,
                    MaxX = int.MinValue,
                    MaxY = int.MinValue
                };
            }

            // Scan pixels and update statistics
            for (int y = 0; y < bmpResult.Height; y++)
            {
                for (int x = 0; x < bmpResult.Width; x++)
                {
                    if (BMPParser.TryGetPixelRGBPacked(pixelData, x, y, out int color))
                    {
                        if (mappings.ColorToProvinceID.TryGetValue(color, out int provinceID))
                        {
                            var currentStats = stats[provinceID];
                            currentStats.PixelCount++;
                            currentStats.MinX = x < currentStats.MinX ? x : currentStats.MinX;
                            currentStats.MinY = y < currentStats.MinY ? y : currentStats.MinY;
                            currentStats.MaxX = x > currentStats.MaxX ? x : currentStats.MaxX;
                            currentStats.MaxY = y > currentStats.MaxY ? y : currentStats.MaxY;
                            stats[provinceID] = currentStats;
                        }
                    }
                }
            }

            return stats;
        }

        private void OnBMPProgress(JobifiedBMPLoader.LoadingProgress progress)
        {
            // Map BMP progress to 0-40% of total
            float mappedProgress = progress.ProgressPercentage * 0.4f;
            ReportProgress(mappedProgress, $"BMP: {progress.CurrentOperation}");
        }

        private void OnDefinitionProgress(JobifiedDefinitionLoader.LoadingProgress progress)
        {
            // Map definition progress to 40-60% of total
            float mappedProgress = 0.4f + (progress.ProgressPercentage * 0.2f);
            ReportProgress(mappedProgress, $"Definitions: {progress.CurrentOperation}");
        }

        private void ReportProgress(float percentage, string operation)
        {
            OnProgressUpdate?.Invoke(new ProcessingProgress
            {
                ProgressPercentage = percentage,
                CurrentOperation = operation
            });
        }

        public void Dispose()
        {
            if (bmpLoader != null)
            {
                bmpLoader.OnProgressUpdate -= OnBMPProgress;
            }
            if (definitionLoader != null)
            {
                definitionLoader.OnProgressUpdate -= OnDefinitionProgress;
            }
        }
    }

    /// <summary>
    /// Complete province map processing result
    /// </summary>
    public struct ProvinceMapResult
    {
        public bool Success;
        public string ErrorMessage;
        public BMPLoadResult BMPData;
        public ProvinceDefinitionMappings Definitions;
        public bool HasDefinitions;
        public ProvinceColorMappings ProvinceMappings;
        public NativeHashMap<int, ProvinceStats> ProvinceStats;

        public void Dispose()
        {
            BMPData.Dispose();
            if (HasDefinitions) Definitions.Dispose();
            ProvinceMappings.Dispose();
            if (ProvinceStats.IsCreated) ProvinceStats.Dispose();
        }
    }

    /// <summary>
    /// Province color-to-ID mappings
    /// </summary>
    public struct ProvinceColorMappings
    {
        public NativeHashMap<int, int> ColorToProvinceID; // PackedRGB -> ProvinceID

        public void Dispose()
        {
            if (ColorToProvinceID.IsCreated) ColorToProvinceID.Dispose();
        }
    }

    /// <summary>
    /// Basic province statistics
    /// </summary>
    public struct ProvinceStats
    {
        public int ProvinceID;
        public int PixelCount;
        public int MinX, MinY, MaxX, MaxY;

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;
    }
}