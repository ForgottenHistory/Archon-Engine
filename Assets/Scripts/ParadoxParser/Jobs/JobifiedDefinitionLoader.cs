using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ParadoxParser.CSV;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Jobs
{
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
        /// Load and process definition.csv file with Burst jobs for optimal performance
        /// </summary>
        public async Task<DefinitionLoadResult> LoadDefinitionAsync(string csvFilePath)
        {
            ReportProgress(0, 1, "Loading definition CSV...");

            // Load CSV file data
            var fileResult = await AsyncFileReader.ReadFileAsync(csvFilePath, Allocator.TempJob);
            if (!fileResult.Success)
            {
                return new DefinitionLoadResult { Success = false, ErrorMessage = "Failed to load definition CSV file" };
            }

            try
            {
                ReportProgress(0, 1, "Parsing CSV with Burst jobs...");

                // Parse CSV using high-performance Burst parser
                var csvResult = CSVParser.Parse(fileResult.Data, Allocator.TempJob);
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
            }
        }

        /// <summary>
        /// Process CSV rows into province definition mappings
        /// </summary>
        private ProvinceDefinitionMappings ProcessDefinitionRows(CSVParser.CSVParseResult csvResult)
        {
            if (!csvResult.Success || csvResult.RowCount == 0)
            {
                return new ProvinceDefinitionMappings { Success = false };
            }

            // Allocate native collections for mappings
            var idToDefinition = new NativeHashMap<int, ProvinceDefinition>(csvResult.RowCount, Allocator.TempJob);
            var colorToID = new NativeHashMap<int, int>(csvResult.RowCount, Allocator.TempJob);
            var definitions = new NativeList<ProvinceDefinition>(csvResult.RowCount, Allocator.TempJob);

            // Process each row (skip header row if present)
            int startRow = HasHeader(csvResult) ? 1 : 0;

            for (int i = startRow; i < csvResult.RowCount; i++)
            {
                var row = csvResult.Rows[i];
                if (row.FieldCount >= 4) // Minimum: ID, R, G, B
                {
                    var definition = ParseDefinitionRow(row, i);
                    if (definition.IsValid)
                    {
                        definitions.Add(definition);
                        idToDefinition[definition.ID] = definition;
                        colorToID[definition.PackedRGB] = definition.ID;
                    }
                }

                // Report progress periodically
                if (i % 100 == 0)
                {
                    ReportProgress(i - startRow, csvResult.RowCount - startRow, "Processing definitions...");
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
        /// Parse a single CSV row into a province definition
        /// </summary>
        private ProvinceDefinition ParseDefinitionRow(CSVParser.CSVRow row, int rowIndex)
        {
            try
            {
                // Parse required fields: ID, R, G, B
                if (!TryParseInt(row.Fields[0], out int id))
                    return ProvinceDefinition.Invalid;

                if (!TryParseInt(row.Fields[1], out int r))
                    return ProvinceDefinition.Invalid;

                if (!TryParseInt(row.Fields[2], out int g))
                    return ProvinceDefinition.Invalid;

                if (!TryParseInt(row.Fields[3], out int b))
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
            return !TryParseInt(csvResult.Rows[0].Fields[0], out _);
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
        /// Simple helper methods for parsing CSV data
        /// </summary>
        private static bool TryParseInt(NativeSlice<byte> data, out int result)
        {
            result = 0;
            if (data.Length == 0) return false;

            unsafe
            {
                byte* ptr = (byte*)NativeSliceUnsafeUtility.GetUnsafePtr(data);
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
                for (; i < data.Length; i++)
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

            return JobifiedDefinitionLoader.SliceToString(NameData);
        }
    }
}