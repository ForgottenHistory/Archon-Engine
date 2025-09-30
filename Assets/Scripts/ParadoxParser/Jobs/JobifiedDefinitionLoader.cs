using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Jobs;
using ParadoxParser.CSV;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// Job-safe province definition without nested native containers
    /// </summary>
    public struct JobSafeProvinceDefinition
    {
        public int ID;
        public byte R, G, B;
        public int PackedRGB;
        public bool IsValid;

        public static JobSafeProvinceDefinition Invalid => new JobSafeProvinceDefinition { IsValid = false };
    }

    /// <summary>
    /// Job-safe CSV field data (pre-extracted from CSVRow to avoid nested containers)
    /// </summary>
    public struct JobSafeCSVFieldData
    {
        public NativeSlice<byte> id;
        public NativeSlice<byte> r;
        public NativeSlice<byte> g;
        public NativeSlice<byte> b;
        public bool isValid;
    }

    /// <summary>
    /// Burst-compiled job for parsing CSV field data into province definitions in parallel
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    struct ProcessDefinitionRowsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<JobSafeCSVFieldData> fieldData;

        [WriteOnly] public NativeArray<JobSafeProvinceDefinition> definitions;

        public void Execute(int index)
        {
            var fields = fieldData[index];

            // Initialize as invalid
            definitions[index] = JobSafeProvinceDefinition.Invalid;

            if (fields.isValid)
            {
                var definition = ParseDefinitionRowBurst(fields);
                if (definition.IsValid)
                {
                    definitions[index] = definition;
                }
            }
        }

        /// <summary>
        /// Parse definition row (Burst disabled due to struct parameter limitations)
        /// </summary>
        // [BurstCompile] - Disabled: structs cannot be passed to Burst external functions
        private static JobSafeProvinceDefinition ParseDefinitionRowBurst(JobSafeCSVFieldData fields)
        {
            // Parse required fields: ID, R, G, B
            unsafe
            {
                byte* idPtr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(fields.id);
                byte* rPtr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(fields.r);
                byte* gPtr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(fields.g);
                byte* bPtr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(fields.b);

                if (!JobifiedDefinitionLoader.TryParseIntBurst(idPtr, fields.id.Length, out int id))
                    return JobSafeProvinceDefinition.Invalid;

                if (!JobifiedDefinitionLoader.TryParseIntBurst(rPtr, fields.r.Length, out int r))
                    return JobSafeProvinceDefinition.Invalid;

                if (!JobifiedDefinitionLoader.TryParseIntBurst(gPtr, fields.g.Length, out int g))
                    return JobSafeProvinceDefinition.Invalid;

                if (!JobifiedDefinitionLoader.TryParseIntBurst(bPtr, fields.b.Length, out int b))
                    return JobSafeProvinceDefinition.Invalid;

                // Validate RGB values
                if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                    return JobSafeProvinceDefinition.Invalid;

                return new JobSafeProvinceDefinition
                {
                    ID = id,
                    R = (byte)r,
                    G = (byte)g,
                    B = (byte)b,
                    PackedRGB = (r << 16) | (g << 8) | b,
                    IsValid = true
                };
            }

        }
    }

    /// <summary>
    /// High-performance definition.csv loader using Unity's Burst job system
    /// Handles province ID, RGB color, and name mappings from Paradox definition files
    /// </summary>
    public class JobifiedDefinitionLoader
        {
            public struct LoadingProgress
            {
                public int RowsProcessed;
                public int TotalRows;
                public float ProgressPercentage;
                public string CurrentOperation;
            }

            public event System.Action<LoadingProgress> OnProgressUpdate;

            /// <summary>
            /// Burst-optimized integer parsing
            /// </summary>
            [BurstCompile]
            public static unsafe bool TryParseIntBurst(byte* ptr, int length, out int result)
            {
                result = 0;
                if (length == 0) return false;

                int value = 0;
                bool negative = false;
                int i = 0;

                // Handle negative sign
                if (ptr[0] == 45) // '-'
                {
                    negative = true;
                    i = 1;
                }

                // Parse digits
                for (; i < length; i++)
                {
                    byte b = ptr[i];
                    if (b >= 48 && b <= 57) // '0' to '9'
                    {
                        value = value * 10 + (b - 48);
                    }
                    else if (b == 32 || b == 9) // space or tab - end of number
                    {
                        break;
                    }
                    else
                    {
                        return false; // Invalid character
                    }
                }

                result = negative ? -value : value;
                return true;
            }

            /// <summary>
            /// Safe wrapper for NativeSlice input
            /// </summary>
            public static bool TryParseIntSafe(NativeSlice<byte> data, out int result)
            {
                result = 0;
                if (data.Length == 0) return false;

                unsafe
                {
                    byte* ptr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(data);
                    return TryParseIntBurst(ptr, data.Length, out result);
                }
            }

            /// <summary>
            /// Load and process definition.csv file with Burst jobs for optimal performance
            /// </summary>
            public async Task<DefinitionLoadResult> LoadDefinitionAsync(string csvFilePath)
            {
                ReportProgress(0, 1, "Loading definition CSV...");

                // Load CSV file data
                // Use Allocator.Persistent because data survives >4 frames in async processing
                var fileResult = await AsyncFileReader.ReadFileAsync(csvFilePath, Allocator.Persistent);
                if (!fileResult.Success)
                {
                    return new DefinitionLoadResult { Success = false, ErrorMessage = "Failed to load definition CSV file" };
                }

                CSVParser.CSVParseResult csvResult = default;
                try
                {
                    ReportProgress(0, 1, "Parsing CSV with Burst jobs...");

                    // Parse CSV using high-performance Burst parser
                    // Use Allocator.Persistent because data survives >4 frames in async processing
                    csvResult = CSVParser.Parse(fileResult.Data, Allocator.Persistent);
                    if (!csvResult.Success)
                    {
                        return new DefinitionLoadResult { Success = false, ErrorMessage = "Failed to parse CSV data" };
                    }

                    ReportProgress(0, 1, "Processing province definitions...");

                    // Process CSV data into province definitions
                    var definitions = ProcessDefinitionRows(csvResult);

                    return new DefinitionLoadResult
                    {
                        Success = true,
                        Definitions = definitions,
                        ProvinceCount = definitions.Success ? definitions.AllDefinitions.Length : 0
                    };
                }
                finally
                {
                    fileResult.Dispose();
                    csvResult.Dispose();
                }
            }

            /// <summary>
            /// Process CSV rows into province definition mappings
            /// </summary>
            private ProvinceDefinitionMappings ProcessDefinitionRows(CSVParser.CSVParseResult csvResult)
            {
                // Use simple processing for now to avoid job system complexity
                return ProcessDefinitionRowsSimple(csvResult);
            }

            /// <summary>
            /// Simple, fast CSV processing without jobs (temporary solution)
            /// </summary>
            private ProvinceDefinitionMappings ProcessDefinitionRowsSimple(CSVParser.CSVParseResult csvResult)
            {
                if (!csvResult.Success || csvResult.RowCount == 0)
                {
                    return new ProvinceDefinitionMappings { Success = false };
                }

                // Determine processing parameters
                int startRow = HasHeader(csvResult) ? 1 : 0;
                int dataRowCount = csvResult.RowCount - startRow;

                if (dataRowCount <= 0)
                {
                    return new ProvinceDefinitionMappings { Success = false };
                }

                // Allocate native collections for mappings
                // Use Allocator.Persistent because data survives >4 frames in coroutine processing
                var idToDefinition = new NativeHashMap<int, ProvinceDefinition>(dataRowCount, Allocator.Persistent);
                var colorToID = new NativeHashMap<int, int>(dataRowCount, Allocator.Persistent);
                var definitions = new NativeList<ProvinceDefinition>(dataRowCount, Allocator.Persistent);

                // Simple processing on main thread
                for (int i = 0; i < dataRowCount; i++)
                {
                    var rowIndex = startRow + i;
                    var row = csvResult.Rows[rowIndex];

                    if (row.FieldCount >= 4) // Minimum: ID, R, G, B
                    {
                        var definition = ParseDefinitionRowSimple(row);
                        if (definition.IsValid)
                        {
                            definitions.Add(definition);
                            idToDefinition[definition.ID] = definition;
                            colorToID[definition.PackedRGB] = definition.ID;
                        }
                    }

                    // Report progress less frequently
                    if (i % 1000 == 0)
                    {
                        ReportProgress(i, dataRowCount, "Processing definitions...");
                    }
                }

                return new ProvinceDefinitionMappings
                {
                    Success = true,
                    IDToDefinition = idToDefinition,
                    ColorToID = colorToID,
                    AllDefinitions = definitions
                };
            }

            /// <summary>
            /// Simple definition row parsing
            /// </summary>
            private ProvinceDefinition ParseDefinitionRowSimple(CSVParser.CSVRow row)
            {
                try
                {
                    // Parse required fields: ID, R, G, B using the Burst version for speed
                    if (!TryParseIntSafe(row.Fields[0], out int id))
                        return ProvinceDefinition.Invalid;

                    if (!TryParseIntSafe(row.Fields[1], out int r))
                        return ProvinceDefinition.Invalid;

                    if (!TryParseIntSafe(row.Fields[2], out int g))
                        return ProvinceDefinition.Invalid;

                    if (!TryParseIntSafe(row.Fields[3], out int b))
                        return ProvinceDefinition.Invalid;

                    // Validate RGB values
                    if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                        return ProvinceDefinition.Invalid;

                    // Parse optional name field
                    var nameSlice = row.FieldCount > 4 ? row.Fields[4] : new NativeSlice<byte>();

                    return new ProvinceDefinition
                    {
                        ID = id,
                        R = (byte)r,
                        G = (byte)g,
                        B = (byte)b,
                        PackedRGB = (r << 16) | (g << 8) | b,
                        NameData = nameSlice,
                        IsValid = true
                    };
                }
                catch
                {
                    return ProvinceDefinition.Invalid;
                }
            }

            /// <summary>
            /// Check if CSV has a header row by looking for non-numeric first field
            /// </summary>
            private bool HasHeader(CSVParser.CSVParseResult csvResult)
            {
                if (csvResult.RowCount == 0 || csvResult.Rows[0].FieldCount == 0)
                    return false;

                // Check if first field of first row is numeric (ID) or text (header)
                return !TryParseIntSafe(csvResult.Rows[0].Fields[0], out _);
            }

            private void ReportProgress(int current, int total, string operation)
            {
                OnProgressUpdate?.Invoke(new LoadingProgress
                {
                    RowsProcessed = current,
                    TotalRows = total,
                    ProgressPercentage = total > 0 ? (float)current / total : 0f,
                    CurrentOperation = operation
                });
            }


            /// <summary>
            /// Convert NativeSlice<byte> to string
            /// </summary>
            public static string SliceToString(NativeSlice<byte> data)
            {
                if (data.Length == 0) return string.Empty;

                unsafe
                {
                    byte* ptr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(data);
                    return System.Text.Encoding.UTF8.GetString(ptr, data.Length);
                }
            }
        }
    }

    /// <summary>
    /// Result of definition file loading
    /// </summary>
    public struct DefinitionLoadResult
    {
        public bool Success;
        public string ErrorMessage;
        public ProvinceDefinitionMappings Definitions;
        public int ProvinceCount;

        public void Dispose()
        {
            Definitions.Dispose();
        }
    }

    /// <summary>
    /// Complete province definition mappings
    /// </summary>
    public struct ProvinceDefinitionMappings
    {
        public bool Success;
        public NativeHashMap<int, ProvinceDefinition> IDToDefinition;       // ID -> Definition
        public NativeHashMap<int, int> ColorToID;                          // PackedRGB -> ID
        public NativeList<ProvinceDefinition> AllDefinitions;              // All definitions

        public void Dispose()
        {
            if (IDToDefinition.IsCreated) IDToDefinition.Dispose();
            if (ColorToID.IsCreated) ColorToID.Dispose();
            if (AllDefinitions.IsCreated) AllDefinitions.Dispose();
        }
    }

    /// <summary>
    /// Single province definition from CSV
    /// </summary>
    public struct ProvinceDefinition
    {
        public int ID;
        public byte R, G, B;
        public int PackedRGB;
        public NativeSlice<byte> NameData;  // Raw name bytes from CSV
        public bool IsValid;

        public static ProvinceDefinition Invalid => new ProvinceDefinition { IsValid = false };

        /// <summary>
        /// Get province name as string (converts from NativeSlice)
        /// </summary>
        public string GetName()
        {
            if (!IsValid || NameData.Length == 0)
                return $"Province_{ID}";

            unsafe
            {
                byte* ptr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(NameData);
                return System.Text.Encoding.UTF8.GetString(ptr, NameData.Length);
            }
        }
    }