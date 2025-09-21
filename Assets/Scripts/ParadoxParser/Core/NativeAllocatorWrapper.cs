using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using ParadoxParser.Data;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeAllocatorWrapper : IDisposable
    {
        private AllocatorManager.AllocatorHandle m_PersistentAllocator;
        private AllocatorManager.AllocatorHandle m_TempAllocator;
        private AllocatorManager.AllocatorHandle m_TempJobAllocator;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;

        public AllocatorManager.AllocatorHandle PersistentAllocator => m_PersistentAllocator;
        public AllocatorManager.AllocatorHandle TempAllocator => m_TempAllocator;
        public AllocatorManager.AllocatorHandle TempJobAllocator => m_TempJobAllocator;

        public static NativeAllocatorWrapper Create()
        {
            var wrapper = new NativeAllocatorWrapper();
            wrapper.Initialize();
            return wrapper;
        }

        private void Initialize()
        {
            m_PersistentAllocator = Allocator.Persistent;
            m_TempAllocator = Allocator.Temp;
            m_TempJobAllocator = Allocator.TempJob;
            m_IsCreated = true;
        }

        public NativeArray<T> AllocateArray<T>(int length, Allocator allocator) where T : unmanaged
        {
            return new NativeArray<T>(length, allocator);
        }

        public NativeList<T> AllocateList<T>(int initialCapacity, Allocator allocator) where T : unmanaged
        {
            return new NativeList<T>(initialCapacity, allocator);
        }

        public NativeHashMap<TKey, TValue> AllocateHashMap<TKey, TValue>(int capacity, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return new NativeHashMap<TKey, TValue>(capacity, allocator);
        }

        public NativeParallelHashMap<TKey, TValue> AllocateParallelHashMap<TKey, TValue>(int capacity, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return new NativeParallelHashMap<TKey, TValue>(capacity, allocator);
        }

        public NativeParallelMultiHashMap<TKey, TValue> AllocateParallelMultiHashMap<TKey, TValue>(int capacity, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            return new NativeParallelMultiHashMap<TKey, TValue>(capacity, allocator);
        }

        public StringInternSystem AllocateStringPool(int capacity, Allocator allocator)
        {
            return new StringInternSystem(capacity, allocator);
        }

        public ParserDataStorage AllocateParserStorage(int capacity, Allocator allocator)
        {
            return new ParserDataStorage(capacity, allocator);
        }

        public NativeDynamicArray<T> AllocateDynamicArray<T>(int capacity, Allocator allocator) where T : unmanaged
        {
            return new NativeDynamicArray<T>(capacity, allocator);
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                m_IsCreated = false;
            }
        }
    }

    public static class AllocatorExtensions
    {
        public static bool IsPersistent(this Allocator allocator)
        {
            return allocator == Allocator.Persistent || allocator == Allocator.Domain;
        }

        public static bool IsTemporary(this Allocator allocator)
        {
            return allocator == Allocator.Temp || allocator == Allocator.TempJob;
        }

        public static bool IsValid(this Allocator allocator)
        {
            return allocator != Allocator.Invalid && allocator != Allocator.None;
        }
    }
}