using System;
using System.Collections.Generic;
using Core.Systems;
using Core.Data;
using Unity.Collections;
using UnityEngine;

namespace Map.Simulation
{
    /// <summary>
    /// Validates simulation state integrity and calculates checksums for multiplayer synchronization.
    /// Provides comprehensive state verification and desync detection.
    /// Works with ProvinceSystem's double-buffered state architecture.
    /// </summary>
    public static class StateValidator
    {
        /// <summary>
        /// Calculate comprehensive checksum of simulation state.
        /// Uses rolling hash for efficiency with large datasets.
        /// </summary>
        public static StateChecksum CalculateStateChecksum(ProvinceSystem provinceSystem)
        {
            if (provinceSystem == null || !provinceSystem.IsInitialized)
            {
                return StateChecksum.Invalid();
            }

            var checksum = new StateChecksum();
            uint hash = 0x811C9DC5; // FNV-1a initial value

            try
            {
                int provinceCount = provinceSystem.ProvinceCount;

                // Hash basic simulation properties
                hash = HashUInt(hash, (uint)provinceCount);

                // Get province IDs and states
                using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
                var stateBuffer = provinceSystem.GetUIReadBuffer();

                // Hash all province states in deterministic order
                for (int i = 0; i < provinceIds.Length && i < stateBuffer.Length; i++)
                {
                    ushort provinceId = provinceIds[i];
                    var state = stateBuffer[i];
                    hash = HashProvinceState(hash, provinceId, state);
                }

                checksum.MainHash = hash;
                checksum.ProvinceCount = (uint)provinceCount;
                checksum.CalculatedAt = DateTime.UtcNow;

                // Calculate sub-checksums for granular validation
                checksum.OwnershipHash = CalculateOwnershipHash(provinceSystem);
                checksum.TerrainHash = CalculateTerrainHash(provinceSystem);

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
        /// Calculate checksum of only ownership data (for network optimization).
        /// </summary>
        private static uint CalculateOwnershipHash(ProvinceSystem provinceSystem)
        {
            uint hash = 0x811C9DC5;

            using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            var stateBuffer = provinceSystem.GetUIReadBuffer();

            for (int i = 0; i < provinceIds.Length && i < stateBuffer.Length; i++)
            {
                ushort provinceId = provinceIds[i];
                var state = stateBuffer[i];
                hash = HashUInt(hash, provinceId);
                hash = HashUInt(hash, state.ownerID);
                hash = HashUInt(hash, state.controllerID);
            }

            return hash;
        }

        /// <summary>
        /// Calculate checksum of terrain data (should be static after map load).
        /// </summary>
        private static uint CalculateTerrainHash(ProvinceSystem provinceSystem)
        {
            uint hash = 0x811C9DC5;

            using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            var stateBuffer = provinceSystem.GetUIReadBuffer();

            for (int i = 0; i < provinceIds.Length && i < stateBuffer.Length; i++)
            {
                ushort provinceId = provinceIds[i];
                var state = stateBuffer[i];
                hash = HashUInt(hash, provinceId);
                hash = HashUInt(hash, state.terrainType);
            }

            return hash;
        }

        /// <summary>
        /// Hash a single province state (engine data only).
        /// </summary>
        private static uint HashProvinceState(uint hash, ushort provinceID, ProvinceState state)
        {
            hash = HashUInt(hash, provinceID);
            hash = HashUInt(hash, state.ownerID);
            hash = HashUInt(hash, state.controllerID);
            hash = HashUInt(hash, state.terrainType);
            hash = HashUInt(hash, state.gameDataSlot);
            return hash;
        }

        /// <summary>
        /// FNV-1a hash for uint values.
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
        /// Compare two checksums for equality.
        /// </summary>
        public static bool ChecksumsMatch(StateChecksum a, StateChecksum b)
        {
            if (!a.IsValid || !b.IsValid)
                return false;

            return a.MainHash == b.MainHash &&
                   a.ProvinceCount == b.ProvinceCount;
        }

        /// <summary>
        /// Perform detailed comparison of two checksums.
        /// Returns information about what differs.
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

                if (a.OwnershipHash != b.OwnershipHash)
                    differences.Add($"OwnershipHash: {a.OwnershipHash:X8} vs {b.OwnershipHash:X8}");

                if (a.TerrainHash != b.TerrainHash)
                    differences.Add($"TerrainHash: {a.TerrainHash:X8} vs {b.TerrainHash:X8}");

                comparison.Differences = differences;
            }

            return comparison;
        }

        /// <summary>
        /// Validate simulation state integrity.
        /// Checks for common corruption patterns and invariant violations.
        /// </summary>
        public static StateValidationResult ValidateSimulationState(ProvinceSystem provinceSystem)
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
                if (!provinceSystem.IsInitialized)
                {
                    result.IsValid = false;
                    result.Issues.Add("ProvinceSystem is not initialized");
                    return result;
                }

                int provinceCount = provinceSystem.ProvinceCount;

                // Get province IDs for validation
                using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
                var stateBuffer = provinceSystem.GetUIReadBuffer();

                // Check province count consistency
                if (provinceIds.Length != provinceCount)
                {
                    result.IsValid = false;
                    result.Issues.Add($"Province count mismatch: reported {provinceCount}, IDs array {provinceIds.Length}");
                }

                // Check for duplicate province IDs
                var seenIDs = new HashSet<ushort>();
                for (int i = 0; i < provinceIds.Length; i++)
                {
                    ushort provinceId = provinceIds[i];
                    if (seenIDs.Contains(provinceId))
                    {
                        result.IsValid = false;
                        result.Issues.Add($"Duplicate province ID: {provinceId}");
                    }
                    seenIDs.Add(provinceId);
                }

                // Check province state validity
                for (int i = 0; i < provinceIds.Length && i < stateBuffer.Length; i++)
                {
                    ushort provinceId = provinceIds[i];
                    var state = stateBuffer[i];

                    // Check owner ID bounds
                    if (state.ownerID > 65535)
                    {
                        result.Issues.Add($"Province {provinceId}: invalid owner ID {state.ownerID}");
                    }

                    // Check controller ID bounds
                    if (state.controllerID > 65535)
                    {
                        result.Issues.Add($"Province {provinceId}: invalid controller ID {state.controllerID}");
                    }
                }

                // Set final validation result
                result.IsValid = result.Issues.Count == 0;
                result.ProvinceCount = provinceCount;

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
        /// Create a lightweight checksum for frequent network synchronization.
        /// Only includes frequently changing data (ownership).
        /// </summary>
        public static uint CalculateLightweightChecksum(ProvinceSystem provinceSystem)
        {
            uint hash = 0x811C9DC5;

            hash = HashUInt(hash, (uint)provinceSystem.ProvinceCount);

            using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            var stateBuffer = provinceSystem.GetUIReadBuffer();

            for (int i = 0; i < provinceIds.Length && i < stateBuffer.Length; i++)
            {
                ushort provinceId = provinceIds[i];
                var state = stateBuffer[i];
                hash = HashUInt(hash, provinceId);
                hash = HashUInt(hash, state.ownerID);
                hash = HashUInt(hash, state.controllerID);
            }

            return hash;
        }
    }

    /// <summary>
    /// Comprehensive checksum of simulation state.
    /// </summary>
    public struct StateChecksum
    {
        public bool IsValid;
        public uint MainHash;           // Hash of all state data
        public uint OwnershipHash;      // Hash of ownership data only
        public uint TerrainHash;        // Hash of terrain data only (should be stable)
        public uint ProvinceCount;      // Number of provinces
        public DateTime CalculatedAt;   // When this checksum was calculated

        public static StateChecksum Invalid() => new StateChecksum { IsValid = false };

        public override string ToString()
        {
            return IsValid
                ? $"StateChecksum[Main:{MainHash:X8}, Own:{OwnershipHash:X8}, Terr:{TerrainHash:X8}, Count:{ProvinceCount}]"
                : "StateChecksum[Invalid]";
        }

        /// <summary>
        /// Get a compact representation for network transmission.
        /// </summary>
        public ulong GetCompactHash()
        {
            return ((ulong)MainHash << 32) | ProvinceCount;
        }
    }

    /// <summary>
    /// Result of checksum comparison.
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
    /// Result of state validation.
    /// </summary>
    public struct StateValidationResult
    {
        public bool IsValid;
        public List<string> Issues;
        public DateTime CheckedAt;
        public int ProvinceCount;

        public override string ToString()
        {
            if (IsValid)
                return $"State valid: {ProvinceCount} provinces";

            string issuesText = Issues != null && Issues.Count > 0
                ? string.Join("; ", Issues)
                : "Unknown issues";

            return $"State invalid: {issuesText}";
        }
    }
}
