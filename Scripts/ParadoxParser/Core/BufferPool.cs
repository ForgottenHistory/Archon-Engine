using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BufferPool : IDisposable
    {
        private NativeList<NativeArray<byte>> m_Buffers;
        private NativeQueue<int> m_AvailableBuffers;
        private int m_BufferSize;
        private int m_MaxBuffers;
        private Allocator m_Allocator;

        public bool IsCreated => m_Buffers.IsCreated;
        public int BufferSize => m_BufferSize;
        public int TotalBuffers => m_Buffers.Length;
        public int AvailableBuffers => m_AvailableBuffers.Count;

        public BufferPool(int bufferSize, int initialBufferCount, int maxBuffers, Allocator allocator)
        {
            m_BufferSize = bufferSize;
            m_MaxBuffers = maxBuffers;
            m_Allocator = allocator;
            m_Buffers = new NativeList<NativeArray<byte>>(maxBuffers, allocator);
            m_AvailableBuffers = new NativeQueue<int>(allocator);

            // Pre-allocate initial buffers
            for (int i = 0; i < initialBufferCount; i++)
            {
                var buffer = new NativeArray<byte>(bufferSize, allocator);
                m_Buffers.Add(buffer);
                m_AvailableBuffers.Enqueue(i);
            }
        }

        public NativeArray<byte> RentBuffer()
        {
            if (m_AvailableBuffers.Count > 0)
            {
                int index = m_AvailableBuffers.Dequeue();
                return m_Buffers[index];
            }

            // If no available buffers and we haven't hit the max, create a new one
            if (m_Buffers.Length < m_MaxBuffers)
            {
                var newBuffer = new NativeArray<byte>(m_BufferSize, m_Allocator);
                m_Buffers.Add(newBuffer);
                return newBuffer;
            }

            // No buffers available and at max capacity, return invalid array
            return default;
        }

        public void ReturnBuffer(NativeArray<byte> buffer)
        {
            if (!buffer.IsCreated || buffer.Length != m_BufferSize)
                return;

            // Find the buffer index
            for (int i = 0; i < m_Buffers.Length; i++)
            {
                if (m_Buffers[i].GetUnsafePtr() == buffer.GetUnsafePtr())
                {
                    // Clear the buffer for reuse
                    UnsafeUtility.MemClear(buffer.GetUnsafePtr(), buffer.Length);
                    m_AvailableBuffers.Enqueue(i);
                    return;
                }
            }
        }

        public void Dispose()
        {
            if (m_Buffers.IsCreated)
            {
                for (int i = 0; i < m_Buffers.Length; i++)
                {
                    if (m_Buffers[i].IsCreated)
                        m_Buffers[i].Dispose();
                }
                m_Buffers.Dispose();
            }

            if (m_AvailableBuffers.IsCreated)
                m_AvailableBuffers.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct StreamingBuffer : IDisposable
    {
        private NativeArray<byte> m_Buffer;
        private int m_Position;
        private int m_Length;
        private int m_Capacity;

        public bool IsCreated => m_Buffer.IsCreated;
        public int Position => m_Position;
        public int Length => m_Length;
        public int Capacity => m_Capacity;
        public int Available => m_Length - m_Position;

        public StreamingBuffer(int capacity, Allocator allocator)
        {
            m_Buffer = new NativeArray<byte>(capacity, allocator);
            m_Position = 0;
            m_Length = 0;
            m_Capacity = capacity;
        }

        public void Reset()
        {
            m_Position = 0;
            m_Length = 0;
        }

        public void SetLength(int length)
        {
            m_Length = math.min(length, m_Capacity);
        }

        public byte ReadByte()
        {
            if (m_Position >= m_Length)
                return 0;

            return m_Buffer[m_Position++];
        }

        public int ReadBytes(NativeArray<byte> destination, int count)
        {
            int bytesToRead = math.min(count, Available);
            if (bytesToRead <= 0)
                return 0;

            NativeArray<byte>.Copy(m_Buffer, m_Position, destination, 0, bytesToRead);
            m_Position += bytesToRead;
            return bytesToRead;
        }

        public NativeSlice<byte> GetSlice(int start, int length)
        {
            start = math.clamp(start, 0, m_Length);
            length = math.min(length, m_Length - start);
            return new NativeSlice<byte>(m_Buffer, start, length);
        }

        public NativeSlice<byte> GetRemainingSlice()
        {
            return GetSlice(m_Position, Available);
        }

        public void Seek(int position)
        {
            m_Position = math.clamp(position, 0, m_Length);
        }

        public bool EndOfBuffer => m_Position >= m_Length;

        public void Dispose()
        {
            if (m_Buffer.IsCreated)
                m_Buffer.Dispose();
        }
    }

    public static class BufferUtilities
    {
        public static void ClearBuffer(NativeArray<byte> buffer)
        {
            if (buffer.IsCreated)
            {
                unsafe
                {
                    UnsafeUtility.MemClear(buffer.GetUnsafePtr(), buffer.Length);
                }
            }
        }

        public static void CopyBuffer(NativeArray<byte> source, NativeArray<byte> destination, int count)
        {
            if (!source.IsCreated || !destination.IsCreated)
                return;

            int bytesToCopy = math.min(count, math.min(source.Length, destination.Length));
            NativeArray<byte>.Copy(source, destination, bytesToCopy);
        }
    }
}