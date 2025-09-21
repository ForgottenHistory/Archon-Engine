using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CompactToken
    {
        public byte Type;           // 1 byte - TokenType enum
        public byte Flags;          // 1 byte - token flags
        public ushort Length;       // 2 bytes - token length
        public int Offset;          // 4 bytes - offset in source
        // Total: 8 bytes, cache-line friendly
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CompactParseNode
    {
        public byte NodeType;       // 1 byte - NodeType enum
        public byte Depth;          // 1 byte - nesting depth
        public ushort ChildCount;   // 2 bytes - number of children
        public int StringId;        // 4 bytes - interned string ID
        public int ParentIndex;     // 4 bytes - parent node index
        public int ChildStartIndex; // 4 bytes - first child index
        // Total: 16 bytes, exactly cache-line aligned
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StringTableEntry
    {
        public uint Hash;           // 4 bytes - string hash
        public ushort Length;       // 2 bytes - string length
        public ushort Flags;        // 2 bytes - string flags
        public int Offset;          // 4 bytes - offset in string pool
        // Total: 12 bytes, compact representation
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public unsafe struct SoAParseNodes
    {
        // Structure of Arrays layout for better cache utilization
        private NativeArray<byte> m_NodeTypes;
        private NativeArray<byte> m_Depths;
        private NativeArray<ushort> m_ChildCounts;
        private NativeArray<int> m_StringIds;
        private NativeArray<int> m_ParentIndices;
        private NativeArray<int> m_ChildStartIndices;
        private int m_Count;
        private int m_Capacity;

        public bool IsCreated => m_NodeTypes.IsCreated;
        public int Count => m_Count;
        public int Capacity => m_Capacity;

        public SoAParseNodes(int capacity, Allocator allocator)
        {
            m_NodeTypes = new NativeArray<byte>(capacity, allocator);
            m_Depths = new NativeArray<byte>(capacity, allocator);
            m_ChildCounts = new NativeArray<ushort>(capacity, allocator);
            m_StringIds = new NativeArray<int>(capacity, allocator);
            m_ParentIndices = new NativeArray<int>(capacity, allocator);
            m_ChildStartIndices = new NativeArray<int>(capacity, allocator);
            m_Count = 0;
            m_Capacity = capacity;
        }

        public int AddNode(NodeType nodeType, byte depth, ushort childCount, int stringId, int parentIndex, int childStartIndex)
        {
            if (m_Count >= m_Capacity) return -1;

            int index = m_Count++;
            m_NodeTypes[index] = (byte)nodeType;
            m_Depths[index] = depth;
            m_ChildCounts[index] = childCount;
            m_StringIds[index] = stringId;
            m_ParentIndices[index] = parentIndex;
            m_ChildStartIndices[index] = childStartIndex;

            return index;
        }

        public CompactParseNode GetNode(int index)
        {
            if (index < 0 || index >= m_Count) return default;

            return new CompactParseNode
            {
                NodeType = m_NodeTypes[index],
                Depth = m_Depths[index],
                ChildCount = m_ChildCounts[index],
                StringId = m_StringIds[index],
                ParentIndex = m_ParentIndices[index],
                ChildStartIndex = m_ChildStartIndices[index]
            };
        }

        public void SetNode(int index, CompactParseNode node)
        {
            if (index < 0 || index >= m_Count) return;

            m_NodeTypes[index] = node.NodeType;
            m_Depths[index] = node.Depth;
            m_ChildCounts[index] = node.ChildCount;
            m_StringIds[index] = node.StringId;
            m_ParentIndices[index] = node.ParentIndex;
            m_ChildStartIndices[index] = node.ChildStartIndex;
        }

        // Optimized bulk operations for better cache utilization
        public void GetNodeTypes(int startIndex, int count, NativeArray<byte> destination)
        {
            if (startIndex + count <= m_Count && count <= destination.Length)
            {
                NativeArray<byte>.Copy(m_NodeTypes, startIndex, destination, 0, count);
            }
        }

        public void GetStringIds(int startIndex, int count, NativeArray<int> destination)
        {
            if (startIndex + count <= m_Count && count <= destination.Length)
            {
                NativeArray<int>.Copy(m_StringIds, startIndex, destination, 0, count);
            }
        }

        public void Clear()
        {
            m_Count = 0;
        }

        public void Dispose()
        {
            if (m_NodeTypes.IsCreated) m_NodeTypes.Dispose();
            if (m_Depths.IsCreated) m_Depths.Dispose();
            if (m_ChildCounts.IsCreated) m_ChildCounts.Dispose();
            if (m_StringIds.IsCreated) m_StringIds.Dispose();
            if (m_ParentIndices.IsCreated) m_ParentIndices.Dispose();
            if (m_ChildStartIndices.IsCreated) m_ChildStartIndices.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PackedStringPool
    {
        // Tightly packed string storage for better cache locality
        private NativeArray<byte> m_StringData;
        private NativeArray<StringTableEntry> m_StringTable;
        private NativeHashMap<uint, int> m_HashToIndex;
        private int m_StringCount;
        private int m_DataUsed;

        public bool IsCreated => m_StringData.IsCreated;
        public int StringCount => m_StringCount;
        public int DataUsed => m_DataUsed;

        public PackedStringPool(int stringCapacity, int dataCapacity, Allocator allocator)
        {
            m_StringData = new NativeArray<byte>(dataCapacity, allocator);
            m_StringTable = new NativeArray<StringTableEntry>(stringCapacity, allocator);
            m_HashToIndex = new NativeHashMap<uint, int>(stringCapacity, allocator);
            m_StringCount = 0;
            m_DataUsed = 0;
        }

        public int InternString(ReadOnlySpan<byte> utf8String)
        {
            uint hash = ComputeHash(utf8String);

            if (m_HashToIndex.TryGetValue(hash, out int existingIndex))
            {
                return existingIndex;
            }

            if (m_StringCount >= m_StringTable.Length || m_DataUsed + utf8String.Length > m_StringData.Length)
            {
                return -1; // Pool full
            }

            int stringIndex = m_StringCount++;
            int offset = m_DataUsed;

            // Copy string data
            for (int i = 0; i < utf8String.Length; i++)
            {
                m_StringData[offset + i] = utf8String[i];
            }

            // Create table entry
            var entry = new StringTableEntry
            {
                Hash = hash,
                Length = (ushort)utf8String.Length,
                Flags = 0,
                Offset = offset
            };

            m_StringTable[stringIndex] = entry;
            m_HashToIndex[hash] = stringIndex;
            m_DataUsed += utf8String.Length;

            return stringIndex;
        }

        public ReadOnlySpan<byte> GetString(int stringIndex)
        {
            if (stringIndex < 0 || stringIndex >= m_StringCount)
                return ReadOnlySpan<byte>.Empty;

            var entry = m_StringTable[stringIndex];
            unsafe
            {
                byte* dataPtr = (byte*)m_StringData.GetUnsafeReadOnlyPtr();
                return new ReadOnlySpan<byte>(dataPtr + entry.Offset, entry.Length);
            }
        }

        private static uint ComputeHash(ReadOnlySpan<byte> data)
        {
            uint hash = 2166136261u; // FNV-1a
            foreach (byte b in data)
            {
                hash = (hash ^ b) * 16777619u;
            }
            return hash;
        }

        public void Dispose()
        {
            if (m_StringData.IsCreated) m_StringData.Dispose();
            if (m_StringTable.IsCreated) m_StringTable.Dispose();
            if (m_HashToIndex.IsCreated) m_HashToIndex.Dispose();
        }
    }

    public static class CacheOptimizations
    {
        public const int CacheLineSize = 64;
        public const int L1CacheSize = 32 * 1024;    // 32KB typical L1 cache
        public const int L2CacheSize = 256 * 1024;   // 256KB typical L2 cache
        public const int L3CacheSize = 8 * 1024 * 1024; // 8MB typical L3 cache

        public static int OptimalBatchSize<T>() where T : unmanaged
        {
            int elementSize = UnsafeUtility.SizeOf<T>();
            return L1CacheSize / (elementSize * 2); // Leave room for other data
        }

        public static bool IsAligned(IntPtr ptr, int alignment)
        {
            return (ptr.ToInt64() & (alignment - 1)) == 0;
        }

        public static int AlignToCache(int size)
        {
            return (size + CacheLineSize - 1) & ~(CacheLineSize - 1);
        }
    }
}