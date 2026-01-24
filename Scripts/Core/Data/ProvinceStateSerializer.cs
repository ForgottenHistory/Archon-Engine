using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using Core.Systems;

namespace Core.Data
{
    /// <summary>
    /// High-performance serialization for province state data.
    /// Optimized for networking with minimal bandwidth usage.
    /// Uses ProvinceSystem's double-buffered state for thread-safe reads.
    /// </summary>
    public static class ProvinceStateSerializer
    {
        private const uint SERIALIZATION_VERSION = 2;  // Bumped for ProvinceSystem compatibility
        private const uint MAGIC_HEADER = 0x50524F56; // "PROV" in ASCII

        /// <summary>
        /// Serialize entire province state for networking (full sync).
        /// Output: Header + Count + ProvinceIDs + States
        /// </summary>
        public static byte[] SerializeFullState(ProvinceSystem provinceSystem)
        {
            if (provinceSystem == null || !provinceSystem.IsInitialized)
            {
                throw new InvalidOperationException("ProvinceSystem not initialized");
            }

            // Get province data
            var stateBuffer = provinceSystem.GetUIReadBuffer();
            int provinceCount = provinceSystem.ProvinceCount;
            uint checksum = provinceSystem.GetStateChecksum();

            // Get province IDs for mapping
            using var provinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);

            // Calculate total size: header(16) + count(4) + ids(count * 2) + states(count * 8)
            int totalSize = 20 + (provinceCount * 2) + (provinceCount * 8);
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
                    *(uint*)(ptr + offset) = 0;  // Reserved (was StateVersion)
                    offset += 4;
                    *(uint*)(ptr + offset) = checksum;
                    offset += 4;

                    // Province count
                    *(int*)(ptr + offset) = provinceCount;
                    offset += 4;

                    // Province IDs (needed for mapping on deserialization)
                    for (int i = 0; i < provinceCount && i < provinceIds.Length; i++)
                    {
                        *(ushort*)(ptr + offset) = provinceIds[i];
                        offset += 2;
                    }

                    // Province states (direct memory copy for maximum performance)
                    if (stateBuffer.IsCreated && provinceCount > 0)
                    {
                        byte* statePtr = (byte*)stateBuffer.GetUnsafeReadOnlyPtr();
                        UnsafeUtility.MemCpy(ptr + offset, statePtr, provinceCount * 8);
                    }
                }
            }

            return buffer;
        }

        /// <summary>
        /// Deserialize full province state from network data.
        /// Returns province IDs and states for the caller to apply.
        /// </summary>
        public static DeserializeResult DeserializeFullState(byte[] data)
        {
            var result = new DeserializeResult();

            if (data == null || data.Length < 20)
            {
                result.ErrorMessage = "Invalid data: too short";
                return result;
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
                        result.ErrorMessage = $"Invalid magic header: 0x{magic:X8}";
                        return result;
                    }
                    offset += 4;

                    uint version = *(uint*)(ptr + offset);
                    if (version != SERIALIZATION_VERSION)
                    {
                        result.ErrorMessage = $"Unsupported version: {version} (expected {SERIALIZATION_VERSION})";
                        return result;
                    }
                    offset += 4;

                    // Skip reserved field
                    offset += 4;

                    result.Checksum = *(uint*)(ptr + offset);
                    offset += 4;

                    // Get province count
                    int provinceCount = *(int*)(ptr + offset);
                    offset += 4;

                    if (provinceCount < 0 || provinceCount > 65535)
                    {
                        result.ErrorMessage = $"Invalid province count: {provinceCount}";
                        return result;
                    }

                    // Validate remaining data size
                    int expectedSize = 20 + (provinceCount * 2) + (provinceCount * 8);
                    if (data.Length != expectedSize)
                    {
                        result.ErrorMessage = $"Data size mismatch: expected {expectedSize}, got {data.Length}";
                        return result;
                    }

                    // Read province IDs
                    result.ProvinceIds = new ushort[provinceCount];
                    for (int i = 0; i < provinceCount; i++)
                    {
                        result.ProvinceIds[i] = *(ushort*)(ptr + offset);
                        offset += 2;
                    }

                    // Read province states
                    result.States = new ProvinceState[provinceCount];
                    for (int i = 0; i < provinceCount; i++)
                    {
                        result.States[i] = *(ProvinceState*)(ptr + offset);
                        offset += 8;
                    }

                    result.IsSuccess = true;
                    result.ProvinceCount = provinceCount;
                }
            }

            return result;
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
                    stats.Reserved = *(uint*)(ptr + 8);
                    stats.Checksum = *(uint*)(ptr + 12);
                    stats.Count = *(int*)(ptr + 16);
                    stats.TotalSize = data.Length;
                }
            }

            stats.IsValid = stats.Magic == MAGIC_HEADER && stats.Version == SERIALIZATION_VERSION;
            return stats;
        }

        /// <summary>
        /// Result of deserialization
        /// </summary>
        public struct DeserializeResult
        {
            public bool IsSuccess;
            public string ErrorMessage;
            public int ProvinceCount;
            public uint Checksum;
            public ushort[] ProvinceIds;
            public ProvinceState[] States;
        }

        /// <summary>
        /// Serialization statistics
        /// </summary>
        public struct SerializationStats
        {
            public uint Magic;
            public uint Version;
            public uint Reserved;
            public uint Checksum;
            public int Count;
            public int TotalSize;
            public bool IsValid;

            public override string ToString()
            {
                return $"Serialization Stats: Count={Count}, Size={TotalSize} bytes, Checksum=0x{Checksum:X8}, Valid={IsValid}";
            }
        }
    }
}
