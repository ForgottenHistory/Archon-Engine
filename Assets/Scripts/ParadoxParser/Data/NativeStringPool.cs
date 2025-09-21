using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeStringPool : IDisposable
    {
        private NativeList<byte> m_StringData;
        private NativeHashMap<uint, int> m_HashToOffset;
        private NativeList<StringEntry> m_StringEntries;
        private int m_NextStringId;

        public bool IsCreated => m_StringData.IsCreated;

        public NativeStringPool(int initialCapacity, Allocator allocator)
        {
            m_StringData = new NativeList<byte>(initialCapacity * 16, allocator);
            m_HashToOffset = new NativeHashMap<uint, int>(initialCapacity, allocator);
            m_StringEntries = new NativeList<StringEntry>(initialCapacity, allocator);
            m_NextStringId = 0;
            m_IsDisposed = false;
        }

        public int InternString(in FixedString128Bytes str)
        {
            var hash = str.GetHashCode();
            var hashUint = (uint)hash;

            if (m_HashToOffset.TryGetValue(hashUint, out int existingId))
            {
                return existingId;
            }

            var stringId = m_NextStringId++;
            var offset = m_StringData.Length;
            var length = str.Length;

            var entry = new StringEntry
            {
                Offset = offset,
                Length = length,
                Hash = hashUint
            };

            m_StringEntries.Add(entry);
            m_HashToOffset[hashUint] = stringId;

            for (int i = 0; i < length; i++)
            {
                m_StringData.Add(str[i]);
            }

            return stringId;
        }

        public int InternString(string str)
        {
            var fixedStr = new FixedString128Bytes(str);
            return InternString(in fixedStr);
        }

        public FixedString128Bytes GetString(int stringId)
        {
            if (stringId < 0 || stringId >= m_StringEntries.Length)
            {
                return default;
            }

            var entry = m_StringEntries[stringId];

            // Extract the byte array from native data
            var bytes = new byte[entry.Length];
            for (int i = 0; i < entry.Length; i++)
            {
                bytes[i] = m_StringData[entry.Offset + i];
            }

            // Convert bytes back to string and then to FixedString128Bytes
            string str = System.Text.Encoding.UTF8.GetString(bytes);
            var result = new FixedString128Bytes(str);

            return result;
        }

        public bool TryGetString(int stringId, out FixedString128Bytes result)
        {
            result = default;

            if (stringId < 0 || stringId >= m_StringEntries.Length)
            {
                return false;
            }

            result = GetString(stringId);
            return true;
        }

        public int Count => m_StringEntries.Length;

        private bool m_IsDisposed;

        public void Dispose()
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;

            try
            {
                if (m_StringData.IsCreated)
                {
                    m_StringData.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_HashToOffset.IsCreated)
                {
                    m_HashToOffset.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_StringEntries.IsCreated)
                {
                    m_StringEntries.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (m_IsDisposed) return inputDeps;
            m_IsDisposed = true;

            JobHandle combinedJob = inputDeps;

            if (m_StringData.IsCreated)
            {
                combinedJob = m_StringData.Dispose(combinedJob);
            }
            if (m_HashToOffset.IsCreated)
            {
                combinedJob = m_HashToOffset.Dispose(combinedJob);
            }
            if (m_StringEntries.IsCreated)
            {
                combinedJob = m_StringEntries.Dispose(combinedJob);
            }

            return combinedJob;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StringEntry
    {
        public int Offset;
        public int Length;
        public uint Hash;
    }
}