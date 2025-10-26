using System;
using System.Collections.Generic;
using Core.Data;
using Core.Systems;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Map.Simulation
{
    /// <summary>
    /// Validates simulation state integrity and calculates checksums for multiplayer synchronization
    /// Provides comprehensive state verification and desync detection
    /// </summary>
    public static class StateValidator
    {
        /// <summary>
        /// Calculate comprehensive checksum of simulation state
        /// Uses rolling hash for efficiency with large datasets
        /// </summary>
        public static StateChecksum CalculateStateChecksum(ProvinceSimulation simulation)
        {
            if (simulation == null || !simulation.IsInitialized)
            {
                return StateChecksum.Invalid();
            }

            var checksum = new StateChecksum();
            uint hash = 0x811C9DC5; // FNV-1a initial value

            try
            {
                // Hash basic simulation properties
                hash = HashUInt(hash, (uint)simulation.ProvinceCount);
                hash = HashUInt(hash, simulation.StateVersion);

                // Hash all province states in deterministic order
                var allProvinceIDs = simulation.GetAllProvinceIDs();

                foreach (var provinceID in allProvinceIDs)
                {
                    var state = simulation.GetProvinceState(provinceID);
                    hash = HashProvinceState(hash, provinceID, state);
                }

                checksum.MainHash = hash;
                checksum.ProvinceCount = (uint)simulation.ProvinceCount;
                checksum.StateVersion = simulation.StateVersion;
                checksum.CalculatedAt = DateTime.UtcNow;

                // Calculate sub-checksums for granular validation
                checksum.OwnershipHash = CalculateOwnershipHash(simulation);
                checksum.DevelopmentHash = CalculateDevelopmentHash(simulation);
                checksum.TerrainHash = CalculateTerrainHash(simulation);

                checksum.IsValid = true;
                return checksum;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"Failed to calculate state checksum: {ex}", "map_rendering");
                return StateChecksum.Invalid();
            }
        }

        /// <summary>
        /// Calculate checksum of only ownership data (for network optimization)
        /// </summary>
        private static uint CalculateOwnershipHash(ProvinceSimulation simulation)
        {
            uint hash = 0x811C9DC5;
            var allProvinceIDs = simulation.GetAllProvinceIDs();

            foreach (var provinceID in allProvinceIDs)
            {
                var state = simulation.GetProvinceState(provinceID);
                hash = HashUInt(hash, provinceID);
                hash = HashUInt(hash, state.ownerID);
                hash = HashUInt(hash, state.controllerID);
            }

            return hash;
        }

        /// <summary>
        /// Calculate checksum of development data (REMOVED - game-specific)
        /// NOTE: Development is game-specific, moved to game layer
        /// </summary>
        private static uint CalculateDevelopmentHash(ProvinceSimulation simulation)
        {
            // REMOVED: development and fortLevel no longer in engine ProvinceState
            // Game layer should calculate its own checksums for game-specific data
            return 0;
        }

        /// <summary>
        /// Calculate checksum of terrain data (should be static after map load)
        /// </summary>
        private static uint CalculateTerrainHash(ProvinceSimulation simulation)
        {
            uint hash = 0x811C9DC5;
            var allProvinceIDs = simulation.GetAllProvinceIDs();

            foreach (var provinceID in allProvinceIDs)
            {
                var state = simulation.GetProvinceState(provinceID);
                hash = HashUInt(hash, provinceID);
                hash = HashUInt(hash, state.terrainType); // Changed from byte to ushort
            }

            return hash;
        }

        /// <summary>
        /// Hash a single province state (engine data only)
        /// </summary>
        private static uint HashProvinceState(uint hash, ushort provinceID, ProvinceState state)
        {
            hash = HashUInt(hash, provinceID);
            hash = HashUInt(hash, state.ownerID);
            hash = HashUInt(hash, state.controllerID);
            hash = HashUInt(hash, state.terrainType);  // Now ushort
            hash = HashUInt(hash, state.gameDataSlot);  // New field
            // REMOVED: development, fortLevel, flags (game-specific)
            return hash;
        }

        /// <summary>
        /// FNV-1a hash for uint values
        /// </summary>
        private static uint HashUInt(uint hash, uint value)
        {
            hash ^= (value & 0xFF);
            hash *= 0x01000193;
            hash ^= ((value >> 8) & 0xFF);
            hash *= 0x01000193;
            hash ^= ((value >> 16) & 0xFF);
            hash *= 0x01000193;
            hash ^= ((value >> 24) & 0xFF);
            hash *= 0x01000193;
            return hash;
        }

        /// <summary>
        /// FNV-1a hash for byte values
        /// </summary>
        private static uint HashByte(uint hash, byte value)
        {
            hash ^= value;
            hash *= 0x01000193;
            return hash;
        }

        /// <summary>
        /// Compare two checksums for equality
        /// </summary>
        public static bool ChecksumsMatch(StateChecksum a, StateChecksum b)
        {
            if (!a.IsValid || !b.IsValid)
                return false;

            return a.MainHash == b.MainHash &&
                   a.ProvinceCount == b.ProvinceCount &&
                   a.StateVersion == b.StateVersion;
        }

        /// <summary>
        /// Perform detailed comparison of two checksums
        /// Returns information about what differs
        /// </summary>
        public static ChecksumComparison CompareChecksums(StateChecksum a, StateChecksum b)
        {
            var comparison = new ChecksumComparison
            {
                Match = ChecksumsMatch(a, b),
                ChecksumA = a,
                ChecksumB = b
            };

            if (!comparison.Match)
            {
                var differences = new List<string>();

                if (a.MainHash != b.MainHash)
                    differences.Add($"MainHash: {a.MainHash:X8} vs {b.MainHash:X8}");

                if (a.ProvinceCount != b.ProvinceCount)
                    differences.Add($"ProvinceCount: {a.ProvinceCount} vs {b.ProvinceCount}");

                if (a.StateVersion != b.StateVersion)
                    differences.Add($"StateVersion: {a.StateVersion} vs {b.StateVersion}");

                if (a.OwnershipHash != b.OwnershipHash)
                    differences.Add($"OwnershipHash: {a.OwnershipHash:X8} vs {b.OwnershipHash:X8}");

                if (a.DevelopmentHash != b.DevelopmentHash)
                    differences.Add($"DevelopmentHash: {a.DevelopmentHash:X8} vs {b.DevelopmentHash:X8}");

                if (a.TerrainHash != b.TerrainHash)
                    differences.Add($"TerrainHash: {a.TerrainHash:X8} vs {b.TerrainHash:X8}");

                comparison.Differences = differences;
            }

            return comparison;
        }

        /// <summary>
        /// Validate simulation state integrity
        /// Checks for common corruption patterns and invariant violations
        /// </summary>
        public static StateValidationResult ValidateSimulationState(ProvinceSimulation simulation)
        {
            var result = new StateValidationResult
            {
                IsValid = true,
                Issues = new List<string>(),
                CheckedAt = DateTime.UtcNow
            };

            try
            {
                // Check if simulation is initialized
                if (!simulation.IsInitialized)
                {
                    result.IsValid = false;
                    result.Issues.Add("Simulation is not initialized");
                    return result;
                }

                // Get all province IDs for validation
                var allProvinceIDs = simulation.GetAllProvinceIDs();

                // Check province count consistency
                if (allProvinceIDs.Length != simulation.ProvinceCount)
                {
                    result.IsValid = false;
                    result.Issues.Add($"Province count mismatch: reported {simulation.ProvinceCount}, actual {allProvinceIDs.Length}");
                }

                // Check for duplicate province IDs (should not happen due to implementation, but validate)
                var seenIDs = new HashSet<ushort>();
                foreach (var provinceID in allProvinceIDs)
                {
                    if (seenIDs.Contains(provinceID))
                    {
                        result.IsValid = false;
                        result.Issues.Add($"Duplicate province ID: {provinceID}");
                    }
                    seenIDs.Add(provinceID);
                }

                // Check province state validity
                foreach (var provinceID in allProvinceIDs)
                {
                    var state = simulation.GetProvinceState(provinceID);

                    // Check owner ID bounds
                    if (state.ownerID > 255)
                    {
                        result.Issues.Add($"Province {provinceID}: invalid owner ID {state.ownerID}");
                    }

                    // Check controller ID bounds
                    if (state.controllerID > 255)
                    {
                        result.Issues.Add($"Province {provinceID}: invalid controller ID {state.controllerID}");
                    }

                    // REMOVED: Development and fort validation (game-specific)
                    // Game layer should validate its own data

                    // Check terrain type validity
                    if (!Enum.IsDefined(typeof(TerrainType), state.terrainType))
                    {
                        result.Issues.Add($"Province {provinceID}: invalid terrain type {state.terrainType}");
                    }
                }

                // Check memory usage is within bounds
                var (totalBytes, hotBytes, coldBytes) = simulation.GetMemoryUsage();
                if (hotBytes != simulation.ProvinceCount * 8)
                {
                    result.Issues.Add($"Hot memory size mismatch: expected {simulation.ProvinceCount * 8}, got {hotBytes}");
                }

                // Set final validation result
                result.IsValid = result.Issues.Count == 0;
                result.ProvinceCount = simulation.ProvinceCount;
                result.StateVersion = simulation.StateVersion;
                result.MemoryUsage = totalBytes;

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add($"Validation failed with exception: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Create a lightweight checksum for frequent network synchronization
        /// Only includes frequently changing data (ownership)
        /// NOTE: Development removed (game-specific)
        /// </summary>
        public static uint CalculateLightweightChecksum(ProvinceSimulation simulation)
        {
            uint hash = 0x811C9DC5;

            hash = HashUInt(hash, simulation.StateVersion);

            var allProvinceIDs = simulation.GetAllProvinceIDs();
            foreach (var provinceID in allProvinceIDs)
            {
                var state = simulation.GetProvinceState(provinceID);
                hash = HashUInt(hash, provinceID);
                hash = HashUInt(hash, state.ownerID);
                hash = HashUInt(hash, state.controllerID);
                // REMOVED: development (game-specific)
            }

            return hash;
        }

        /// <summary>
        /// Calculate state-only checksum (excluding version) for comparing identical game states
        /// Used for testing that identical states produce identical checksums regardless of how they were reached
        /// </summary>
        public static uint CalculateStateOnlyChecksum(ProvinceSimulation simulation)
        {
            if (simulation == null || !simulation.IsInitialized)
            {
                return 0;
            }

            uint hash = 0x811C9DC5;

            // Hash province count but NOT state version
            hash = HashUInt(hash, (uint)simulation.ProvinceCount);

            // Hash all province states in deterministic order
            var allProvinceIDs = simulation.GetAllProvinceIDs();

            foreach (var provinceID in allProvinceIDs)
            {
                var state = simulation.GetProvinceState(provinceID);
                hash = HashProvinceState(hash, provinceID, state);
            }

            return hash;
        }
    }

    /// <summary>
    /// Comprehensive checksum of simulation state
    /// </summary>
    public struct StateChecksum
    {
        public bool IsValid;
        public uint MainHash;           // Hash of all state data
        public uint OwnershipHash;      // Hash of ownership data only
        public uint DevelopmentHash;    // Hash of development data only
        public uint TerrainHash;        // Hash of terrain data only (should be stable)
        public uint ProvinceCount;      // Number of provinces
        public uint StateVersion;       // Current state version
        public DateTime CalculatedAt;   // When this checksum was calculated

        public static StateChecksum Invalid() => new StateChecksum { IsValid = false };

        public override string ToString()
        {
            return IsValid
                ? $"StateChecksum[Main:{MainHash:X8}, Own:{OwnershipHash:X8}, Dev:{DevelopmentHash:X8}, " +
                  $"Terr:{TerrainHash:X8}, Count:{ProvinceCount}, Ver:{StateVersion}]"
                : "StateChecksum[Invalid]";
        }

        /// <summary>
        /// Get a compact representation for network transmission
        /// </summary>
        public ulong GetCompactHash()
        {
            return ((ulong)MainHash << 32) | StateVersion;
        }
    }

    /// <summary>
    /// Result of checksum comparison
    /// </summary>
    public struct ChecksumComparison
    {
        public bool Match;
        public StateChecksum ChecksumA;
        public StateChecksum ChecksumB;
        public List<string> Differences;

        public override string ToString()
        {
            if (Match)
                return "Checksums match";

            string differencesText = Differences != null && Differences.Count > 0
                ? string.Join(", ", Differences)
                : "Unknown differences";

            return $"Checksums differ: {differencesText}";
        }
    }

    /// <summary>
    /// Result of state validation
    /// </summary>
    public struct StateValidationResult
    {
        public bool IsValid;
        public List<string> Issues;
        public DateTime CheckedAt;
        public int ProvinceCount;
        public uint StateVersion;
        public int MemoryUsage;

        public override string ToString()
        {
            if (IsValid)
                return $"State valid: {ProvinceCount} provinces, version {StateVersion}, {MemoryUsage} bytes";

            string issuesText = Issues != null && Issues.Count > 0
                ? string.Join("; ", Issues)
                : "Unknown issues";

            return $"State invalid: {issuesText}";
        }
    }
}