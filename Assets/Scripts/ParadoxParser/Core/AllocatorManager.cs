using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using ParadoxParser.Data;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ParserAllocatorManager : IDisposable
    {
        private bool m_IsInitialized;
        private NativeAllocatorWrapper m_AllocatorWrapper;
        private ParseNodePool m_NodePool;
        private BufferPool m_BufferPool;
        private GenericObjectPool<int> m_IntPool;

        public bool IsInitialized => m_IsInitialized;
        public Allocator PersistentAllocator => Allocator.Persistent;
        public Allocator TempAllocator => Allocator.Temp;
        public Allocator TempJobAllocator => Allocator.TempJob;

        public static ParserAllocatorManager Create(int nodePoolSize = 10000, int bufferCount = 16, int bufferSize = 65536)
        {
            var manager = new ParserAllocatorManager();
            manager.Initialize(nodePoolSize, bufferCount, bufferSize);
            return manager;
        }

        private void Initialize(int nodePoolSize, int bufferCount, int bufferSize)
        {
            m_AllocatorWrapper = NativeAllocatorWrapper.Create();
            m_NodePool = new ParseNodePool(nodePoolSize, Allocator.Persistent);
            m_BufferPool = new BufferPool(bufferSize, bufferCount / 2, bufferCount, Allocator.Persistent);
            m_IntPool = new GenericObjectPool<int>(1000, Allocator.Persistent);
            m_IsInitialized = true;
        }

        public int RentParseNode(NodeType type, int stringId = -1, int parentIndex = -1)
        {
            if (!m_IsInitialized) return -1;
            return m_NodePool.Rent(type, stringId, parentIndex);
        }

        public void ReturnParseNode(int nodeIndex)
        {
            if (!m_IsInitialized) return;
            m_NodePool.Return(nodeIndex);
        }

        public NativeArray<byte> RentBuffer()
        {
            if (!m_IsInitialized) return default;
            return m_BufferPool.RentBuffer();
        }

        public void ReturnBuffer(NativeArray<byte> buffer)
        {
            if (!m_IsInitialized) return;
            m_BufferPool.ReturnBuffer(buffer);
        }

        public NativeArray<T> AllocatePersistent<T>(int length) where T : unmanaged
        {
            return new NativeArray<T>(length, Allocator.Persistent);
        }

        public NativeArray<T> AllocateTemp<T>(int length) where T : unmanaged
        {
            return new NativeArray<T>(length, Allocator.Temp);
        }

        public NativeArray<T> AllocateTempJob<T>(int length) where T : unmanaged
        {
            return new NativeArray<T>(length, Allocator.TempJob);
        }

        public NativeList<T> CreatePersistentList<T>(int initialCapacity) where T : unmanaged
        {
            return new NativeList<T>(initialCapacity, Allocator.Persistent);
        }

        public NativeList<T> CreateTempList<T>(int initialCapacity) where T : unmanaged
        {
            return new NativeList<T>(initialCapacity, Allocator.Temp);
        }

        public NativeHashMap<TKey, TValue> CreatePersistentHashMap<TKey, TValue>(int capacity)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return new NativeHashMap<TKey, TValue>(capacity, Allocator.Persistent);
        }

        public void ClearTempAllocations()
        {
            // Temp allocations are automatically cleared by Unity
            // This is a placeholder for explicit cleanup if needed
        }

        public MemoryStats GetMemoryStats()
        {
            return new MemoryStats
            {
                ActiveNodes = m_NodePool.ActiveCount,
                PooledNodes = m_NodePool.AvailableCount,
                TotalNodes = m_NodePool.PoolSize,
                AvailableBuffers = m_BufferPool.AvailableBuffers,
                TotalBuffers = m_BufferPool.TotalBuffers,
                BufferSize = m_BufferPool.BufferSize
            };
        }

        public void Dispose()
        {
            if (m_IsInitialized)
            {
                if (m_NodePool.IsCreated)
                    m_NodePool.Dispose();
                if (m_BufferPool.IsCreated)
                    m_BufferPool.Dispose();
                if (m_IntPool.IsCreated)
                    m_IntPool.Dispose();
                if (m_AllocatorWrapper.IsCreated)
                    m_AllocatorWrapper.Dispose();

                m_IsInitialized = false;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStats
    {
        public int ActiveNodes;
        public int PooledNodes;
        public int TotalNodes;
        public int AvailableBuffers;
        public int TotalBuffers;
        public int BufferSize;

        public override string ToString()
        {
            return $"Nodes: {ActiveNodes}/{TotalNodes} active, Buffers: {AvailableBuffers}/{TotalBuffers} available ({BufferSize} bytes each)";
        }
    }
}