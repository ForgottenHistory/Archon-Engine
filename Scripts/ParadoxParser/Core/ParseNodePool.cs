using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using ParadoxParser.Data;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ParseNodePool : IDisposable
    {
        private NativeList<ParseNode> m_Pool;
        private NativeQueue<int> m_FreeIndices;
        private int m_NextIndex;
        private int m_ActiveCount;

        public bool IsCreated => m_Pool.IsCreated;
        public int ActiveCount => m_ActiveCount;
        public int PoolSize => m_Pool.Length;
        public int AvailableCount => m_FreeIndices.Count;

        public ParseNodePool(int initialCapacity, Allocator allocator)
        {
            m_Pool = new NativeList<ParseNode>(initialCapacity, allocator);
            m_FreeIndices = new NativeQueue<int>(allocator);
            m_NextIndex = 0;
            m_ActiveCount = 0;

            // Pre-populate pool with default nodes
            for (int i = 0; i < initialCapacity; i++)
            {
                m_Pool.Add(new ParseNode(NodeType.Invalid));
                m_FreeIndices.Enqueue(i);
            }
            m_NextIndex = initialCapacity;
        }

        public int Rent(NodeType type, int stringId = -1, int parentIndex = -1)
        {
            int index;

            if (m_FreeIndices.Count > 0)
            {
                index = m_FreeIndices.Dequeue();
            }
            else
            {
                // Expand pool if needed
                index = m_NextIndex++;
                m_Pool.Add(new ParseNode(NodeType.Invalid));
            }

            var node = new ParseNode(type, stringId, parentIndex);
            m_Pool[index] = node;
            m_ActiveCount++;

            return index;
        }

        public void Return(int index)
        {
            if (index < 0 || index >= m_Pool.Length)
                return;

            // Reset node to default state
            m_Pool[index] = new ParseNode(NodeType.Invalid);
            m_FreeIndices.Enqueue(index);
            m_ActiveCount--;
        }

        public ParseNode GetNode(int index)
        {
            if (index < 0 || index >= m_Pool.Length)
                return new ParseNode(NodeType.Invalid);

            return m_Pool[index];
        }

        public void SetNode(int index, ParseNode node)
        {
            if (index < 0 || index >= m_Pool.Length)
                return;

            m_Pool[index] = node;
        }

        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < m_Pool.Length && m_Pool[index].Type != NodeType.Invalid;
        }

        public void Clear()
        {
            m_FreeIndices.Clear();

            for (int i = 0; i < m_Pool.Length; i++)
            {
                m_Pool[i] = new ParseNode(NodeType.Invalid);
                m_FreeIndices.Enqueue(i);
            }

            m_ActiveCount = 0;
        }

        public void Dispose()
        {
            if (m_Pool.IsCreated)
                m_Pool.Dispose();
            if (m_FreeIndices.IsCreated)
                m_FreeIndices.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var job1 = m_Pool.Dispose(inputDeps);
            var job2 = m_FreeIndices.Dispose(job1);
            return job2;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GenericObjectPool<T> : IDisposable where T : unmanaged
    {
        private NativeList<T> m_Pool;
        private NativeQueue<int> m_FreeIndices;
        private int m_NextIndex;
        private int m_ActiveCount;

        public bool IsCreated => m_Pool.IsCreated;
        public int ActiveCount => m_ActiveCount;
        public int PoolSize => m_Pool.Length;

        public GenericObjectPool(int initialCapacity, Allocator allocator)
        {
            m_Pool = new NativeList<T>(initialCapacity, allocator);
            m_FreeIndices = new NativeQueue<int>(allocator);
            m_NextIndex = 0;
            m_ActiveCount = 0;

            // Pre-populate pool
            for (int i = 0; i < initialCapacity; i++)
            {
                m_Pool.Add(default(T));
                m_FreeIndices.Enqueue(i);
            }
            m_NextIndex = initialCapacity;
        }

        public int Rent()
        {
            int index;

            if (m_FreeIndices.Count > 0)
            {
                index = m_FreeIndices.Dequeue();
            }
            else
            {
                index = m_NextIndex++;
                m_Pool.Add(default(T));
            }

            m_ActiveCount++;
            return index;
        }

        public void Return(int index)
        {
            if (index < 0 || index >= m_Pool.Length)
                return;

            m_Pool[index] = default(T);
            m_FreeIndices.Enqueue(index);
            m_ActiveCount--;
        }

        public ref T GetRef(int index)
        {
            return ref m_Pool.ElementAt(index);
        }

        public T Get(int index)
        {
            if (index < 0 || index >= m_Pool.Length)
                return default(T);

            return m_Pool[index];
        }

        public void Set(int index, T value)
        {
            if (index < 0 || index >= m_Pool.Length)
                return;

            m_Pool[index] = value;
        }

        public void Clear()
        {
            m_FreeIndices.Clear();

            for (int i = 0; i < m_Pool.Length; i++)
            {
                m_Pool[i] = default(T);
                m_FreeIndices.Enqueue(i);
            }

            m_ActiveCount = 0;
        }

        public void Dispose()
        {
            if (m_Pool.IsCreated)
                m_Pool.Dispose();
            if (m_FreeIndices.IsCreated)
                m_FreeIndices.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var job1 = m_Pool.Dispose(inputDeps);
            var job2 = m_FreeIndices.Dispose(job1);
            return job2;
        }
    }
}