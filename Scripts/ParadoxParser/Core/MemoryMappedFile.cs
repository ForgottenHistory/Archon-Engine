using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public unsafe struct NativeMemoryMappedFile : IDisposable
    {
        private MemoryMappedFile m_MappedFile;
        private MemoryMappedViewAccessor m_Accessor;
        private byte* m_Pointer;
        private long m_Size;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;
        public long Size => m_Size;
        public byte* Pointer => m_Pointer;

        public static NativeMemoryMappedFile CreateFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[MemoryMappedFile] File not found: {filePath}");
                    return default;
                }

                var fileInfo = new FileInfo(filePath);
                var mappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, "ParadoxParser", fileInfo.Length, MemoryMappedFileAccess.Read);
                var accessor = mappedFile.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

                var result = new NativeMemoryMappedFile
                {
                    m_MappedFile = mappedFile,
                    m_Accessor = accessor,
                    m_Size = fileInfo.Length,
                    m_IsCreated = true
                };

                result.m_Pointer = (byte*)accessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MemoryMappedFile] Failed to create memory-mapped file: {ex.Message}");
                return default;
            }
        }

        public static NativeMemoryMappedFile CreateFromFileSegment(string filePath, long offset, long size)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[MemoryMappedFile] File not found: {filePath}");
                    return default;
                }

                var fileInfo = new FileInfo(filePath);
                if (offset + size > fileInfo.Length)
                {
                    Debug.LogError($"[MemoryMappedFile] Segment exceeds file size");
                    return default;
                }

                var mappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, "ParadoxParser", fileInfo.Length, MemoryMappedFileAccess.Read);
                var accessor = mappedFile.CreateViewAccessor(offset, size, MemoryMappedFileAccess.Read);

                var result = new NativeMemoryMappedFile
                {
                    m_MappedFile = mappedFile,
                    m_Accessor = accessor,
                    m_Size = size,
                    m_IsCreated = true
                };

                result.m_Pointer = (byte*)accessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MemoryMappedFile] Failed to create memory-mapped file segment: {ex.Message}");
                return default;
            }
        }

        public byte ReadByte(long offset)
        {
            if (!m_IsCreated || offset >= m_Size) return 0;
            return m_Pointer[offset];
        }

        public void ReadBytes(long offset, NativeArray<byte> destination, int count)
        {
            if (!m_IsCreated || offset >= m_Size) return;

            long bytesToRead = Math.Min(count, Math.Min(destination.Length, m_Size - offset));
            if (bytesToRead <= 0) return;

            byte* destPtr = (byte*)destination.GetUnsafePtr();
            UnsafeUtility.MemCpy(destPtr, m_Pointer + offset, bytesToRead);
        }

        public NativeSlice<byte> GetSlice(long offset, int length)
        {
            if (!m_IsCreated || offset >= m_Size) return default;

            long actualLength = Math.Min(length, m_Size - offset);
            if (actualLength <= 0) return default;

            // Create a NativeArray wrapper around the memory-mapped data
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                m_Pointer + offset, (int)actualLength, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safety);
#endif

            return new NativeSlice<byte>(array, 0, (int)actualLength);
        }

        public bool TryFindSequence(ReadOnlySpan<byte> sequence, long startOffset, out long foundOffset)
        {
            foundOffset = -1;
            if (!m_IsCreated || sequence.Length == 0 || startOffset >= m_Size) return false;

            long searchEnd = m_Size - sequence.Length + 1;
            for (long i = startOffset; i < searchEnd; i++)
            {
                bool found = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (m_Pointer[i + j] != sequence[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    foundOffset = i;
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                try
                {
                    m_Accessor?.Dispose();
                    m_MappedFile?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MemoryMappedFile] Error disposing: {ex.Message}");
                }

                m_Accessor = null;
                m_MappedFile = null;
                m_Pointer = null;
                m_Size = 0;
                m_IsCreated = false;
            }
        }
    }

    public static class MemoryMappedFileUtilities
    {
        public const long LargeFileThreshold = 100 * 1024 * 1024; // 100MB
        public const long MaxMappingSize = 1024 * 1024 * 1024; // 1GB max mapping

        public static bool ShouldUseMemoryMapping(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var fileInfo = new FileInfo(filePath);
                return fileInfo.Length >= LargeFileThreshold;
            }
            catch
            {
                return false;
            }
        }

        public static MemoryMappingStrategy GetOptimalStrategy(long fileSize)
        {
            if (fileSize < LargeFileThreshold)
                return MemoryMappingStrategy.LoadToMemory;

            if (fileSize < MaxMappingSize)
                return MemoryMappingStrategy.FullMapping;

            return MemoryMappingStrategy.SegmentedMapping;
        }

        public static long CalculateOptimalSegmentSize(long fileSize)
        {
            // Use smaller segments for very large files to avoid memory pressure
            if (fileSize > MaxMappingSize)
                return 64 * 1024 * 1024; // 64MB segments

            if (fileSize > 500 * 1024 * 1024)
                return 128 * 1024 * 1024; // 128MB segments

            return 256 * 1024 * 1024; // 256MB segments
        }
    }

    public enum MemoryMappingStrategy
    {
        LoadToMemory,    // Small files - load entirely to native arrays
        FullMapping,     // Medium files - map entire file
        SegmentedMapping // Large files - map in segments as needed
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MappedFileSegment : IDisposable
    {
        public NativeMemoryMappedFile MappedFile;
        public long StartOffset;
        public long Size;
        public bool IsActive;

        public bool Contains(long fileOffset)
        {
            return IsActive && fileOffset >= StartOffset && fileOffset < StartOffset + Size;
        }

        public long GetRelativeOffset(long fileOffset)
        {
            return fileOffset - StartOffset;
        }

        public void Dispose()
        {
            if (IsActive)
            {
                MappedFile.Dispose();
                IsActive = false;
            }
        }
    }
}