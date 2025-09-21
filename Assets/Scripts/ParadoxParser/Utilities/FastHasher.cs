using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace ParadoxParser.Utilities
{
    /// <summary>
    /// High-performance string hashing utilities optimized for Paradox parser
    /// Provides multiple hashing algorithms for different use cases
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class FastHasher
    {
        // FNV-1a constants
        private const uint FNV_OFFSET_BASIS_32 = 2166136261u;
        private const uint FNV_PRIME_32 = 16777619u;
        private const ulong FNV_OFFSET_BASIS_64 = 14695981039346656037ul;
        private const ulong FNV_PRIME_64 = 1099511628211ul;

        // xxHash constants
        private const uint XXHASH_PRIME32_1 = 2654435761u;
        private const uint XXHASH_PRIME32_2 = 2246822519u;
        private const uint XXHASH_PRIME32_3 = 3266489917u;
        private const uint XXHASH_PRIME32_4 = 668265263u;
        private const uint XXHASH_PRIME32_5 = 374761393u;

        /// <summary>
        /// Fast FNV-1a hash for small strings (most common case in Paradox files)
        /// Optimized for strings up to 64 bytes
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint HashFNV1a32(byte* data, int length)
        {
            uint hash = FNV_OFFSET_BASIS_32;

            // Process 4 bytes at a time for better performance
            int i = 0;

            // Process chunks of 4 bytes
            while (i + 4 <= length)
            {
                hash ^= data[i];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 1];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 2];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 3];
                hash *= FNV_PRIME_32;
                i += 4;
            }

            // Process remaining bytes
            while (i < length)
            {
                hash ^= data[i];
                hash *= FNV_PRIME_32;
                i++;
            }

            return hash;
        }

        /// <summary>
        /// Convenience overload for NativeSlice
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashFNV1a32(NativeSlice<byte> data)
        {
            uint hash = FNV_OFFSET_BASIS_32;

            // Process 4 bytes at a time for better performance
            int i = 0;
            int length = data.Length;

            // Process chunks of 4 bytes
            while (i + 4 <= length)
            {
                hash ^= data[i];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 1];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 2];
                hash *= FNV_PRIME_32;
                hash ^= data[i + 3];
                hash *= FNV_PRIME_32;
                i += 4;
            }

            // Process remaining bytes
            while (i < length)
            {
                hash ^= data[i];
                hash *= FNV_PRIME_32;
                i++;
            }

            return hash;
        }

        /// <summary>
        /// 64-bit FNV-1a hash for larger strings or when more hash space is needed
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong HashFNV1a64(NativeSlice<byte> data)
        {
            ulong hash = FNV_OFFSET_BASIS_64;

            // Process 8 bytes at a time
            int i = 0;
            int length = data.Length;

            while (i + 8 <= length)
            {
                for (int j = 0; j < 8; j++)
                {
                    hash ^= data[i + j];
                    hash *= FNV_PRIME_64;
                }
                i += 8;
            }

            // Process remaining bytes
            while (i < length)
            {
                hash ^= data[i];
                hash *= FNV_PRIME_64;
                i++;
            }

            return hash;
        }

        /// <summary>
        /// xxHash32 implementation - faster than FNV for larger strings
        /// Ideal for file names and longer identifiers
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashXX32(NativeSlice<byte> data, uint seed = 0)
        {
            int length = data.Length;
            uint hash;

            if (length >= 16)
            {
                hash = HashXX32Large(data, seed);
            }
            else
            {
                hash = seed + XXHASH_PRIME32_5;
            }

            hash += (uint)length;

            // Process remaining bytes (less than 16)
            int remaining = length & 15;
            int offset = length - remaining;

            while (remaining >= 4)
            {
                uint value = ReadUInt32(data, offset);
                hash += value * XXHASH_PRIME32_3;
                hash = RotateLeft(hash, 17) * XXHASH_PRIME32_4;
                offset += 4;
                remaining -= 4;
            }

            while (remaining > 0)
            {
                hash += data[offset] * XXHASH_PRIME32_5;
                hash = RotateLeft(hash, 11) * XXHASH_PRIME32_1;
                offset++;
                remaining--;
            }

            // Final avalanche
            hash ^= hash >> 15;
            hash *= XXHASH_PRIME32_2;
            hash ^= hash >> 13;
            hash *= XXHASH_PRIME32_3;
            hash ^= hash >> 16;

            return hash;
        }

        /// <summary>
        /// Case-insensitive hash for Paradox identifiers
        /// Many Paradox files are case-insensitive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashCaseInsensitive(NativeSlice<byte> data)
        {
            uint hash = FNV_OFFSET_BASIS_32;

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                // Convert to lowercase if it's an uppercase ASCII letter
                if (b >= (byte)'A' && b <= (byte)'Z')
                {
                    b = (byte)(b + 32); // Convert to lowercase
                }

                hash ^= b;
                hash *= FNV_PRIME_32;
            }

            return hash;
        }

        /// <summary>
        /// Specialized hash for common Paradox patterns
        /// Optimized for common strings like "yes", "no", province IDs, etc.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashParadoxPattern(NativeSlice<byte> data)
        {
            int length = data.Length;

            // Fast path for very common short strings
            if (length <= 8)
            {
                return HashShortString(data);
            }

            // Use FNV-1a for medium strings
            if (length <= 32)
            {
                return HashFNV1a32(data);
            }

            // Use xxHash for longer strings
            return HashXX32(data);
        }

        /// <summary>
        /// Optimized hash for very short strings (<=8 bytes)
        /// Common for Paradox keywords and short identifiers
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashShortString(NativeSlice<byte> data)
        {
            uint hash = 0;
            int length = data.Length;

            // Pack bytes into uint for very fast hashing
            for (int i = 0; i < length && i < 4; i++)
            {
                hash |= (uint)data[i] << (i * 8);
            }

            if (length > 4)
            {
                uint hash2 = 0;
                for (int i = 4; i < length && i < 8; i++)
                {
                    hash2 |= (uint)data[i] << ((i - 4) * 8);
                }
                hash ^= hash2 * FNV_PRIME_32;
            }

            // Simple avalanche
            hash ^= hash >> 16;
            hash *= FNV_PRIME_32;
            hash ^= hash >> 13;

            return hash;
        }

        /// <summary>
        /// xxHash32 implementation for large strings (>=16 bytes)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashXX32Large(NativeSlice<byte> data, uint seed)
        {
            int length = data.Length;
            uint v1 = seed + XXHASH_PRIME32_1 + XXHASH_PRIME32_2;
            uint v2 = seed + XXHASH_PRIME32_2;
            uint v3 = seed + 0;
            uint v4 = seed - XXHASH_PRIME32_1;

            int offset = 0;
            int limit = length - 16;

            while (offset <= limit)
            {
                v1 = ProcessXXHashRound(v1, ReadUInt32(data, offset));
                v2 = ProcessXXHashRound(v2, ReadUInt32(data, offset + 4));
                v3 = ProcessXXHashRound(v3, ReadUInt32(data, offset + 8));
                v4 = ProcessXXHashRound(v4, ReadUInt32(data, offset + 12));
                offset += 16;
            }

            uint hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
            return hash;
        }

        /// <summary>
        /// xxHash round processing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ProcessXXHashRound(uint acc, uint value)
        {
            acc += value * XXHASH_PRIME32_2;
            acc = RotateLeft(acc, 13);
            acc *= XXHASH_PRIME32_1;
            return acc;
        }

        /// <summary>
        /// Read uint32 from byte array at offset (little-endian)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadUInt32(NativeSlice<byte> data, int offset)
        {
            return (uint)(data[offset] |
                         (data[offset + 1] << 8) |
                         (data[offset + 2] << 16) |
                         (data[offset + 3] << 24));
        }

        /// <summary>
        /// Left rotate bits
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        /// <summary>
        /// Compute hash and choose best algorithm based on string characteristics
        /// </summary>
        public static uint HashAdaptive(NativeSlice<byte> data)
        {
            int length = data.Length;

            // Very short strings: optimized short hash
            if (length <= 3)
                return HashShortString(data);

            // Short strings: FNV-1a
            if (length <= 16)
                return HashFNV1a32(data);

            // Medium strings: check if mostly ASCII
            if (length <= 64 && IsAsciiAlpha(data))
                return HashCaseInsensitive(data);

            // Large strings: xxHash
            return HashXX32(data);
        }

        /// <summary>
        /// Check if string is mostly ASCII alphabetic (common for identifiers)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAsciiAlpha(NativeSlice<byte> data)
        {
            int alphaCount = 0;
            int length = math.min(data.Length, 16); // Sample first 16 bytes

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                if ((b >= (byte)'a' && b <= (byte)'z') ||
                    (b >= (byte)'A' && b <= (byte)'Z'))
                {
                    alphaCount++;
                }
            }

            return alphaCount > length / 2; // More than 50% alphabetic
        }
    }
}