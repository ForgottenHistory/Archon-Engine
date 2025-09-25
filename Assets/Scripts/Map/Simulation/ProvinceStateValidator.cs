using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System.Collections.Generic;
using System.Text;

namespace Map.Simulation
{
    /// <summary>
    /// Comprehensive validation system for province simulation state
    /// Ensures data integrity, architectural compliance, and multiplayer determinism
    /// </summary>
    public static class ProvinceStateValidator
    {
        public struct ValidationResult
        {
            public bool IsValid;
            public List<ValidationError> Errors;
            public List<ValidationWarning> Warnings;
            public ValidationStats Stats;

            public ValidationResult(bool isValid)
            {
                IsValid = isValid;
                Errors = new List<ValidationError>();
                Warnings = new List<ValidationWarning>();
                Stats = new ValidationStats();
            }

            public void AddError(ValidationErrorType type, string message, int provinceIndex = -1)
            {
                Errors.Add(new ValidationError(type, message, provinceIndex));
                IsValid = false;
            }

            public void AddWarning(ValidationWarningType type, string message, int provinceIndex = -1)
            {
                Warnings.Add(new ValidationWarning(type, message, provinceIndex));
            }

            public string GetSummary()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Validation Result: {(IsValid ? "PASSED" : "FAILED")}");
                sb.AppendLine($"Errors: {Errors.Count}, Warnings: {Warnings.Count}");
                sb.AppendLine($"Provinces: {Stats.ProvinceCount}, Memory: {Stats.MemoryUsageBytes / 1024f:F1} KB");

                if (Errors.Count > 0)
                {
                    sb.AppendLine("\nErrors:");
                    foreach (var error in Errors)
                    {
                        sb.AppendLine($"  - {error.Type}: {error.Message}");
                    }
                }

                if (Warnings.Count > 0)
                {
                    sb.AppendLine("\nWarnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  - {warning.Type}: {warning.Message}");
                    }
                }

                return sb.ToString();
            }
        }

        public struct ValidationError
        {
            public ValidationErrorType Type;
            public string Message;
            public int ProvinceIndex;

            public ValidationError(ValidationErrorType type, string message, int provinceIndex = -1)
            {
                Type = type;
                Message = message;
                ProvinceIndex = provinceIndex;
            }
        }

        public struct ValidationWarning
        {
            public ValidationWarningType Type;
            public string Message;
            public int ProvinceIndex;

            public ValidationWarning(ValidationWarningType type, string message, int provinceIndex = -1)
            {
                Type = type;
                Message = message;
                ProvinceIndex = provinceIndex;
            }
        }

        public struct ValidationStats
        {
            public int ProvinceCount;
            public int MemoryUsageBytes;
            public int OwnedProvinces;
            public int UnownedProvinces;
            public int OccupiedProvinces;
            public int CoastalProvinces;
            public float AverageDevelopment;
            public Dictionary<TerrainType, int> TerrainCounts;

            public ValidationStats(bool initialize)
            {
                ProvinceCount = 0;
                MemoryUsageBytes = 0;
                OwnedProvinces = 0;
                UnownedProvinces = 0;
                OccupiedProvinces = 0;
                CoastalProvinces = 0;
                AverageDevelopment = 0f;
                TerrainCounts = initialize ? new Dictionary<TerrainType, int>() : null;
            }
        }

        public enum ValidationErrorType
        {
            StructureSizeViolation,     // ProvinceState not 8 bytes
            InvalidMemoryLayout,        // Memory alignment issues
            DataInconsistency,          // Lookups don't match data
            InvalidProvinceState,       // State values out of range
            ArchitecturalViolation,     // Violates dual-layer architecture
            DeterminismViolation        // Non-deterministic data detected
        }

        public enum ValidationWarningType
        {
            PerformanceConcern,         // Performance impact detected
            UnoptimalMemoryUsage,       // Memory usage could be better
            GameplayInconsistency,      // Gameplay logic issues
            NetworkingConcern           // Multiplayer compatibility
        }

        /// <summary>
        /// Perform comprehensive validation of the province simulation
        /// </summary>
        public static ValidationResult ValidateSimulation(ProvinceSimulation simulation)
        {
            var result = new ValidationResult(true);
            result.Stats = new ValidationStats(true);

            if (simulation == null)
            {
                result.AddError(ValidationErrorType.InvalidProvinceState, "Simulation is null");
                return result;
            }

            if (!simulation.IsInitialized)
            {
                result.AddError(ValidationErrorType.InvalidProvinceState, "Simulation not initialized");
                return result;
            }

            // 1. Validate architectural compliance
            ValidateArchitecturalCompliance(simulation, ref result);

            // 2. Validate memory layout and performance
            ValidateMemoryLayout(simulation, ref result);

            // 3. Validate data consistency
            ValidateDataConsistency(simulation, ref result);

            // 4. Validate province states
            ValidateProvinceStates(simulation, ref result);

            // 5. Calculate statistics
            CalculateStatistics(simulation, ref result);

            return result;
        }

        /// <summary>
        /// Validate dual-layer architecture compliance
        /// </summary>
        private static void ValidateArchitecturalCompliance(ProvinceSimulation simulation, ref ValidationResult result)
        {
            // Validate ProvinceState struct size (CRITICAL)
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                result.AddError(ValidationErrorType.StructureSizeViolation,
                    $"ProvinceState must be exactly 8 bytes, but is {actualSize} bytes. " +
                    "This violates the core architecture requirement.");
            }

            // Validate memory usage (80KB limit for 10k provinces)
            var (totalBytes, hotBytes, coldBytes) = simulation.GetMemoryUsage();
            int maxAllowedHotBytes = simulation.ProvinceCount * 8;

            if (hotBytes > maxAllowedHotBytes * 1.1f) // Allow 10% overhead
            {
                result.AddError(ValidationErrorType.ArchitecturalViolation,
                    $"Hot data exceeds budget: {hotBytes} bytes vs {maxAllowedHotBytes} expected");
            }

            // Check for multiplayer determinism requirements
            var allProvinces = simulation.GetAllProvinces();
            for (int i = 0; i < allProvinces.Length; i++)
            {
                var state = allProvinces[i];

                // All data must be integer-based (no floats)
                // This is automatically satisfied by the struct design

                // Check for presentation data in simulation layer
                if (state.HasFlag(ProvinceFlags.IsSelected))
                {
                    result.AddWarning(ValidationWarningType.GameplayInconsistency,
                        $"Province {i} has presentation flag (IsSelected) in simulation layer", i);
                }
            }
        }

        /// <summary>
        /// Validate memory layout and performance characteristics
        /// </summary>
        private static void ValidateMemoryLayout(ProvinceSimulation simulation, ref ValidationResult result)
        {
            var (totalBytes, hotBytes, coldBytes) = simulation.GetMemoryUsage();

            result.Stats.MemoryUsageBytes = hotBytes; // Use hot bytes for architecture validation

            // Check hot data alignment
            var allProvinces = simulation.GetAllProvinces();
            unsafe
            {
                void* ptr = allProvinces.GetUnsafeReadOnlyPtr();
                long address = (long)ptr;

                // Check 8-byte alignment for optimal performance
                if (address % 8 != 0)
                {
                    result.AddWarning(ValidationWarningType.PerformanceConcern,
                        "Province data not 8-byte aligned - may impact cache performance");
                }
            }

            // Check capacity utilization
            float utilizationRatio = (float)simulation.ProvinceCount / allProvinces.Length;
            if (utilizationRatio < 0.5f)
            {
                result.AddWarning(ValidationWarningType.UnoptimalMemoryUsage,
                    $"Low capacity utilization: {utilizationRatio:P0} " +
                    $"({simulation.ProvinceCount}/{allProvinces.Length})");
            }

            // Check for performance targets
            if (simulation.ProvinceCount > 10000)
            {
                if (hotBytes > 100 * 1024) // 100KB limit
                {
                    result.AddError(ValidationErrorType.ArchitecturalViolation,
                        $"Hot data exceeds 100KB limit: {hotBytes / 1024f:F1} KB");
                }
            }
        }

        /// <summary>
        /// Validate data consistency between lookups and arrays
        /// </summary>
        private static void ValidateDataConsistency(ProvinceSimulation simulation, ref ValidationResult result)
        {
            // This would require extending ProvinceSimulation to expose internal lookups
            // For now, we validate what we can access

            string errorMessage;
            if (!simulation.ValidateState(out errorMessage))
            {
                result.AddError(ValidationErrorType.DataInconsistency, errorMessage);
            }

            // Validate state version consistency
            uint currentChecksum = simulation.CalculateStateChecksum();
            if (currentChecksum == 0)
            {
                result.AddWarning(ValidationWarningType.GameplayInconsistency,
                    "State checksum is zero - may indicate uninitialized data");
            }
        }

        /// <summary>
        /// Validate individual province states
        /// </summary>
        private static void ValidateProvinceStates(ProvinceSimulation simulation, ref ValidationResult result)
        {
            var allProvinces = simulation.GetAllProvinces();

            for (int i = 0; i < allProvinces.Length; i++)
            {
                var state = allProvinces[i];

                // Validate terrain type
                if (state.terrain > (byte)TerrainType.Tundra)
                {
                    result.AddError(ValidationErrorType.InvalidProvinceState,
                        $"Invalid terrain type: {state.terrain}", i);
                }

                // Validate development constraints
                if (state.terrain == (byte)TerrainType.Ocean && state.development > 0)
                {
                    result.AddError(ValidationErrorType.InvalidProvinceState,
                        "Ocean provinces cannot have development", i);
                }

                if (state.terrain == (byte)TerrainType.Mountain && state.development > 150)
                {
                    result.AddWarning(ValidationWarningType.GameplayInconsistency,
                        "Mountain provinces with very high development", i);
                }

                // Validate ownership consistency
                if (state.IsOccupied && !state.IsOwned)
                {
                    result.AddError(ValidationErrorType.InvalidProvinceState,
                        "Province is occupied but not owned", i);
                }

                // Validate flag combinations
                if (state.HasFlag(ProvinceFlags.IsCapital) && !state.IsOwned)
                {
                    result.AddError(ValidationErrorType.InvalidProvinceState,
                        $"Unowned province marked as capital (index {i}, ownerID {state.ownerID}, flags {state.flags})", i);
                }

                // Validate network-safe ranges
                if (state.ownerID > 1000) // Reasonable country limit
                {
                    result.AddWarning(ValidationWarningType.NetworkingConcern,
                        $"Very high owner ID: {state.ownerID}", i);
                }
            }
        }

        /// <summary>
        /// Calculate validation statistics
        /// </summary>
        private static void CalculateStatistics(ProvinceSimulation simulation, ref ValidationResult result)
        {
            var allProvinces = simulation.GetAllProvinces();
            var stats = result.Stats;

            stats.ProvinceCount = allProvinces.Length;

            int totalDevelopment = 0;

            for (int i = 0; i < allProvinces.Length; i++)
            {
                var state = allProvinces[i];

                // Count ownership
                if (state.IsOwned)
                {
                    stats.OwnedProvinces++;
                    if (state.IsOccupied)
                        stats.OccupiedProvinces++;
                }
                else
                {
                    stats.UnownedProvinces++;
                }

                // Count coastal
                if (state.HasFlag(ProvinceFlags.IsCoastal))
                    stats.CoastalProvinces++;

                // Count terrain types
                var terrain = (TerrainType)state.terrain;
                if (!stats.TerrainCounts.ContainsKey(terrain))
                    stats.TerrainCounts[terrain] = 0;
                stats.TerrainCounts[terrain]++;

                totalDevelopment += state.development;
            }

            stats.AverageDevelopment = stats.ProvinceCount > 0 ?
                (float)totalDevelopment / stats.ProvinceCount : 0f;

            result.Stats = stats;
        }

        /// <summary>
        /// Quick validation for performance-critical scenarios
        /// </summary>
        public static bool QuickValidate(ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (simulation == null)
            {
                errorMessage = "Simulation is null";
                return false;
            }

            if (!simulation.IsInitialized)
            {
                errorMessage = "Simulation not initialized";
                return false;
            }

            // Validate struct size (most critical check)
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                errorMessage = $"ProvinceState size violation: {actualSize} bytes instead of 8";
                return false;
            }

            // Validate internal consistency
            return simulation.ValidateState(out errorMessage);
        }

        /// <summary>
        /// Validate command execution prerequisites
        /// </summary>
        public static bool ValidateCommandExecution(ProvinceSimulation simulation,
            IProvinceCommand command, out string errorMessage)
        {
            errorMessage = null;

            if (!QuickValidate(simulation, out errorMessage))
                return false;

            return command.Validate(simulation, out errorMessage);
        }

        /// <summary>
        /// Validate serialization integrity
        /// </summary>
        public static bool ValidateSerializedState(byte[] data, out string errorMessage)
        {
            return ProvinceStateSerializer.ValidateSerializedData(data, out errorMessage);
        }
    }
}