using System.IO;
using Core.Data;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Low-level binary serialization utilities
    ///
    /// Principles:
    /// - Deterministic serialization (same data = same bytes)
    /// - Platform-independent (works on Windows/Mac/Linux)
    /// - Efficient binary format (no JSON/XML overhead)
    /// - Type-safe helpers for common patterns
    ///
    /// Handles:
    /// - FixedPoint64 (as long RawValue for determinism)
    /// - NativeArray (raw memory copy)
    /// - Primitives (int, ushort, string, etc.)
    /// - Arrays of primitives
    /// </summary>
    public static class SerializationHelper
    {
        // ====================================================================
        // FIXEDPOINT64 SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Serialize FixedPoint64 as deterministic long value
        /// </summary>
        public static void WriteFixedPoint64(BinaryWriter writer, FixedPoint64 value)
        {
            writer.Write(value.RawValue);
        }

        /// <summary>
        /// Deserialize FixedPoint64 from long value
        /// </summary>
        public static FixedPoint64 ReadFixedPoint64(BinaryReader reader)
        {
            long rawValue = reader.ReadInt64();
            return FixedPoint64.FromRaw(rawValue);
        }

        /// <summary>
        /// Serialize array of FixedPoint64 values
        /// </summary>
        public static void WriteFixedPoint64Array(BinaryWriter writer, FixedPoint64[] values)
        {
            writer.Write(values.Length);
            foreach (var value in values)
            {
                WriteFixedPoint64(writer, value);
            }
        }

        /// <summary>
        /// Deserialize array of FixedPoint64 values
        /// </summary>
        public static FixedPoint64[] ReadFixedPoint64Array(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            FixedPoint64[] values = new FixedPoint64[length];
            for (int i = 0; i < length; i++)
            {
                values[i] = ReadFixedPoint64(reader);
            }
            return values;
        }

        // ====================================================================
        // NATIVEARRAY SERIALIZATION (UNSAFE - RAW MEMORY COPY)
        // ====================================================================

        /// <summary>
        /// Serialize NativeArray as raw bytes (fastest, deterministic)
        /// IMPORTANT: Only works for blittable structs (no managed references)
        /// </summary>
        public static unsafe void WriteNativeArray<T>(BinaryWriter writer, NativeArray<T> array) where T : struct
        {
            // Write length
            writer.Write(array.Length);

            if (array.Length == 0)
                return;

            // Calculate byte size
            int elementSize = UnsafeUtility.SizeOf<T>();
            int totalBytes = array.Length * elementSize;

            // Get raw pointer
            void* ptr = array.GetUnsafeReadOnlyPtr();

            // Copy raw bytes to temporary managed array
            byte[] bytes = new byte[totalBytes];
            fixed (byte* dest = bytes)
            {
                UnsafeUtility.MemCpy(dest, ptr, totalBytes);
            }

            // Write to stream
            writer.Write(bytes);
        }

        /// <summary>
        /// Deserialize NativeArray from raw bytes
        /// Allocates new NativeArray - caller must dispose
        /// </summary>
        public static unsafe NativeArray<T> ReadNativeArray<T>(BinaryReader reader, Allocator allocator) where T : struct
        {
            // Read length
            int length = reader.ReadInt32();

            if (length == 0)
                return new NativeArray<T>(0, allocator);

            // Calculate byte size
            int elementSize = UnsafeUtility.SizeOf<T>();
            int totalBytes = length * elementSize;

            // Create native array
            NativeArray<T> array = new NativeArray<T>(length, allocator);

            // Read raw bytes
            byte[] bytes = reader.ReadBytes(totalBytes);

            // Copy to native array
            void* ptr = array.GetUnsafePtr();
            fixed (byte* src = bytes)
            {
                UnsafeUtility.MemCpy(ptr, src, totalBytes);
            }

            return array;
        }

        // ====================================================================
        // SPARSE DATA SERIALIZATION (SKIP EMPTY ENTRIES)
        // ====================================================================

        /// <summary>
        /// Write sparse ushort array (only non-zero values)
        /// Format: count, [index, value] pairs
        /// Efficient for arrays where most values are 0
        /// </summary>
        public static void WriteSparseUShortArray(BinaryWriter writer, ushort[] array)
        {
            // Count non-zero entries
            int nonZeroCount = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] != 0)
                    nonZeroCount++;
            }

            // Write count and array length
            writer.Write(nonZeroCount);
            writer.Write(array.Length);

            // Write [index, value] pairs for non-zero entries
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] != 0)
                {
                    writer.Write((ushort)i);
                    writer.Write(array[i]);
                }
            }
        }

        /// <summary>
        /// Read sparse ushort array
        /// </summary>
        public static ushort[] ReadSparseUShortArray(BinaryReader reader)
        {
            int nonZeroCount = reader.ReadInt32();
            int arrayLength = reader.ReadInt32();

            ushort[] array = new ushort[arrayLength];

            // Read [index, value] pairs
            for (int i = 0; i < nonZeroCount; i++)
            {
                ushort index = reader.ReadUInt16();
                ushort value = reader.ReadUInt16();
                array[index] = value;
            }

            return array;
        }

        // ====================================================================
        // COMMON PRIMITIVES (HELPERS FOR CONSISTENCY)
        // ====================================================================

        /// <summary>
        /// Write string with length prefix (null-safe)
        /// </summary>
        public static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1); // -1 = null
            }
            else
            {
                writer.Write(value.Length);
                if (value.Length > 0)
                {
                    writer.Write(value.ToCharArray());
                }
            }
        }

        /// <summary>
        /// Read string with length prefix (null-safe)
        /// </summary>
        public static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1)
                return null;

            if (length == 0)
                return string.Empty;

            char[] chars = reader.ReadChars(length);
            return new string(chars);
        }

        /// <summary>
        /// Write ushort array with length prefix
        /// </summary>
        public static void WriteUShortArray(BinaryWriter writer, ushort[] array)
        {
            writer.Write(array.Length);
            foreach (var value in array)
            {
                writer.Write(value);
            }
        }

        /// <summary>
        /// Read ushort array with length prefix
        /// </summary>
        public static ushort[] ReadUShortArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            ushort[] array = new ushort[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = reader.ReadUInt16();
            }
            return array;
        }
    }
}
