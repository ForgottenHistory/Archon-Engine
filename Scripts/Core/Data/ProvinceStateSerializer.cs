using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using Core.Systems;

namespace Core.Data
{
    /// <summary>
    /// High-performance serialization for province simulation state
    /// Optimized for networking with minimal bandwidth usage
    /// </summary>
    public static class ProvinceStateSerializer
    {
        private const uint SERIALIZATION_VERSION = 1;
        private const uint MAGIC_HEADER = 0x50524F56; // "PROV" in ASCII

        /// <summary>
        /// Serialize entire simulation state for networking (full sync)
        /// Output: Header + Count + States = ~80KB for 10k provinces
        /// </summary>
        public static byte[] SerializeFullState(ProvinceSimulation simulation)
        {
            if (!simulation.IsInitialized)
            {
                throw new InvalidOperationException("Simulation not initialized");
            }

            var provinces = simulation.GetAllProvinces();
            int provinceCount = provinces.Length;

            // Calculate total size: header(16) + count(4) + states(provinceCount * 8)
            int totalSize = 20 + (provinceCount * 8);
            byte[] buffer = new byte[totalSize];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    int offset = 0;

                    // Header
                    *(uint*)(ptr + offset) = MAGIC_HEADER;
                    offset += 4;
                    *(uint*)(ptr + offset) = SERIALIZATION_VERSION;
                    offset += 4;
                    *(uint*)(ptr + offset) = simulation.StateVersion;
                    offset += 4;
                    *(uint*)(ptr + offset) = simulation.CalculateStateChecksum();
                    offset += 4;

                    // Province count
                    *(int*)(ptr + offset) = provinceCount;
                    offset += 4;

                    // Province states (direct memory copy for maximum performance)
                    byte* statePtr = (byte*)provinces.GetUnsafeReadOnlyPtr();
                    UnsafeUtility.MemCpy(ptr + offset, statePtr, provinceCount * 8);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Deserialize full simulation state from network data
        /// </summary>
        public static bool DeserializeFullState(byte[] data, ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (data == null || data.Length < 20)
            {
                errorMessage = "Invalid data: too short";
                return false;
            }

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    int offset = 0;

                    // Validate header
                    uint magic = *(uint*)(ptr + offset);
                    if (magic != MAGIC_HEADER)
                    {
                        errorMessage = $"Invalid magic header: 0x{magic:X8}";
                        return false;
                    }
                    offset += 4;

                    uint version = *(uint*)(ptr + offset);
                    if (version != SERIALIZATION_VERSION)
                    {
                        errorMessage = $"Unsupported version: {version}";
                        return false;
                    }
                    offset += 4;

                    uint stateVersion = *(uint*)(ptr + offset);
                    offset += 4;

                    uint expectedChecksum = *(uint*)(ptr + offset);
                    offset += 4;

                    // Get province count
                    int provinceCount = *(int*)(ptr + offset);
                    offset += 4;

                    if (provinceCount < 0 || provinceCount > 65535)
                    {
                        errorMessage = $"Invalid province count: {provinceCount}";
                        return false;
                    }

                    // Validate remaining data size
                    int expectedSize = 20 + (provinceCount * 8);
                    if (data.Length != expectedSize)
                    {
                        errorMessage = $"Data size mismatch: expected {expectedSize}, got {data.Length}";
                        return false;
                    }

                    // TODO: Apply deserialized state to simulation
                    // This would require extending ProvinceSimulation with batch loading capabilities

                    ArchonLogger.LogDataLinking($"Successfully deserialized {provinceCount} provinces (version {stateVersion})");
                    return true;
                }
            }
        }

        /// <summary>
        /// Serialize delta changes for efficient networking
        /// Only sends provinces that have changed since last sync
        /// </summary>
        public static byte[] SerializeDeltaState(ProvinceSimulation simulation)
        {
            if (!simulation.IsDirty)
                return Array.Empty<byte>();

            var dirtyIndices = simulation.GetDirtyIndices();
            var allProvinces = simulation.GetAllProvinces();

            // Calculate size: header(16) + count(4) + (index(4) + state(8)) * dirtyCount
            int dirtyCount = dirtyIndices.Count;
            int totalSize = 20 + (dirtyCount * 12);
            byte[] buffer = new byte[totalSize];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    int offset = 0;

                    // Header
                    *(uint*)(ptr + offset) = MAGIC_HEADER;
                    offset += 4;
                    *(uint*)(ptr + offset) = SERIALIZATION_VERSION;
                    offset += 4;
                    *(uint*)(ptr + offset) = simulation.StateVersion;
                    offset += 4;
                    *(uint*)(ptr + offset) = simulation.CalculateStateChecksum();
                    offset += 4;

                    // Delta count
                    *(int*)(ptr + offset) = dirtyCount;
                    offset += 4;

                    // Delta entries
                    foreach (int index in dirtyIndices)
                    {
                        *(int*)(ptr + offset) = index;
                        offset += 4;

                        var state = allProvinces[index];
                        *(ProvinceState*)(ptr + offset) = state;
                        offset += 8;
                    }
                }
            }

            ArchonLogger.LogDataLinking($"Serialized delta state: {dirtyCount} changed provinces, {totalSize} bytes");
            return buffer;
        }

        /// <summary>
        /// Serialize single province state for command transmission
        /// </summary>
        public static byte[] SerializeSingleProvince(ProvinceState state, ushort provinceID)
        {
            byte[] buffer = new byte[10]; // 2 bytes ID + 8 bytes state

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    *(ushort*)ptr = provinceID;
                    *(ProvinceState*)(ptr + 2) = state;
                }
            }

            return buffer;
        }

        /// <summary>
        /// Deserialize single province state
        /// </summary>
        public static bool DeserializeSingleProvince(byte[] data, out ushort provinceID, out ProvinceState state)
        {
            provinceID = 0;
            state = default;

            if (data == null || data.Length != 10)
                return false;

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    provinceID = *(ushort*)ptr;
                    state = *(ProvinceState*)(ptr + 2);
                }
            }

            return true;
        }

        /// <summary>
        /// Calculate compression ratio for debugging
        /// </summary>
        public static float CalculateCompressionRatio(int originalProvinceCount, int deltaProvinceCount)
        {
            if (originalProvinceCount == 0) return 1f;

            int fullSize = 20 + (originalProvinceCount * 8);
            int deltaSize = 20 + (deltaProvinceCount * 12);

            return (float)deltaSize / fullSize;
        }

        /// <summary>
        /// Validate serialized data integrity
        /// </summary>
        public static bool ValidateSerializedData(byte[] data, out string errorMessage)
        {
            errorMessage = null;

            if (data == null)
            {
                errorMessage = "Data is null";
                return false;
            }

            if (data.Length < 20)
            {
                errorMessage = "Data too short for header";
                return false;
            }

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    uint magic = *(uint*)ptr;
                    if (magic != MAGIC_HEADER)
                    {
                        errorMessage = $"Invalid magic header: 0x{magic:X8}";
                        return false;
                    }

                    uint version = *(uint*)(ptr + 4);
                    if (version != SERIALIZATION_VERSION)
                    {
                        errorMessage = $"Unsupported version: {version}";
                        return false;
                    }

                    // Additional validation could be added here
                }
            }

            return true;
        }

        /// <summary>
        /// Get serialization statistics
        /// </summary>
        public static SerializationStats GetSerializationStats(byte[] data)
        {
            var stats = new SerializationStats();

            if (data == null || data.Length < 20)
                return stats;

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    stats.Magic = *(uint*)ptr;
                    stats.Version = *(uint*)(ptr + 4);
                    stats.StateVersion = *(uint*)(ptr + 8);
                    stats.Checksum = *(uint*)(ptr + 12);
                    stats.Count = *(int*)(ptr + 16);
                    stats.TotalSize = data.Length;
                }
            }

            stats.IsValid = stats.Magic == MAGIC_HEADER && stats.Version == SERIALIZATION_VERSION;
            return stats;
        }

        public struct SerializationStats
        {
            public uint Magic;
            public uint Version;
            public uint StateVersion;
            public uint Checksum;
            public int Count;
            public int TotalSize;
            public bool IsValid;

            public override string ToString()
            {
                return $"Serialization Stats: Version={StateVersion}, Count={Count}, Size={TotalSize} bytes, Valid={IsValid}";
            }
        }
    }
}