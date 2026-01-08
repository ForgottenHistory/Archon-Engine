using System;
using Unity.Collections;
using Unity.Burst;
using Core.Data;

namespace Core.Collections
{
    /// <summary>
    /// Burst-compatible min-heap for priority queue operations.
    /// Used by pathfinding for efficient lowest-cost node extraction.
    ///
    /// Performance:
    /// - Push: O(log n)
    /// - Pop: O(log n)
    /// - Peek: O(1)
    ///
    /// Memory: Pre-allocated NativeList, zero allocations during use.
    /// </summary>
    public struct NativeMinHeap<T> : IDisposable where T : unmanaged, IComparable<T>
    {
        private NativeList<T> data;

        public int Count => data.Length;
        public bool IsCreated => data.IsCreated;
        public bool IsEmpty => data.Length == 0;

        public NativeMinHeap(int initialCapacity, Allocator allocator)
        {
            data = new NativeList<T>(initialCapacity, allocator);
        }

        /// <summary>
        /// Push element onto heap. O(log n)
        /// </summary>
        public void Push(T item)
        {
            data.Add(item);
            HeapifyUp(data.Length - 1);
        }

        /// <summary>
        /// Remove and return minimum element. O(log n)
        /// </summary>
        public T Pop()
        {
            if (data.Length == 0)
                throw new InvalidOperationException("Heap is empty");

            T min = data[0];
            int lastIndex = data.Length - 1;

            if (lastIndex > 0)
            {
                data[0] = data[lastIndex];
                data.RemoveAt(lastIndex);
                HeapifyDown(0);
            }
            else
            {
                data.RemoveAt(0);
            }

            return min;
        }

        /// <summary>
        /// Return minimum element without removing. O(1)
        /// </summary>
        public T Peek()
        {
            if (data.Length == 0)
                throw new InvalidOperationException("Heap is empty");
            return data[0];
        }

        /// <summary>
        /// Clear all elements. O(1)
        /// </summary>
        public void Clear()
        {
            data.Clear();
        }

        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (data[index].CompareTo(data[parent]) >= 0)
                    break;

                // Swap
                T temp = data[index];
                data[index] = data[parent];
                data[parent] = temp;

                index = parent;
            }
        }

        private void HeapifyDown(int index)
        {
            int lastIndex = data.Length - 1;

            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int smallest = index;

                if (left <= lastIndex && data[left].CompareTo(data[smallest]) < 0)
                    smallest = left;

                if (right <= lastIndex && data[right].CompareTo(data[smallest]) < 0)
                    smallest = right;

                if (smallest == index)
                    break;

                // Swap
                T temp = data[index];
                data[index] = data[smallest];
                data[smallest] = temp;

                index = smallest;
            }
        }

        public void Dispose()
        {
            if (data.IsCreated)
                data.Dispose();
        }
    }

    /// <summary>
    /// Pathfinding node for A* algorithm.
    /// Comparable by fScore for min-heap priority queue.
    /// </summary>
    public struct PathfindingNode : IComparable<PathfindingNode>
    {
        public ushort provinceID;
        public FixedPoint64 fScore;

        public int CompareTo(PathfindingNode other)
        {
            return fScore.CompareTo(other.fScore);
        }
    }
}
