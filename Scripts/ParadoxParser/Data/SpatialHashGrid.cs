using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SpatialHashGrid<T> : IDisposable where T : unmanaged, ISpatialElement
    {
        private NativeParallelMultiHashMap<int, T> m_Grid;
        private float m_CellSize;
        private int2 m_GridSize;
        private float2 m_WorldMin;
        private float2 m_WorldMax;

        public bool IsCreated => m_Grid.IsCreated;

        public SpatialHashGrid(float2 worldMin, float2 worldMax, float cellSize, Allocator allocator)
        {
            m_WorldMin = worldMin;
            m_WorldMax = worldMax;
            m_CellSize = cellSize;

            var worldSize = worldMax - worldMin;
            m_GridSize = new int2(
                (int)math.ceil(worldSize.x / cellSize),
                (int)math.ceil(worldSize.y / cellSize)
            );

            var totalCells = m_GridSize.x * m_GridSize.y;
            m_Grid = new NativeParallelMultiHashMap<int, T>(totalCells * 4, allocator);
        }

        public void Add(T element)
        {
            var position = element.GetPosition();
            var cellIndex = GetCellIndex(position);
            m_Grid.Add(cellIndex, element);
        }

        public void Remove(T element)
        {
            var position = element.GetPosition();
            var cellIndex = GetCellIndex(position);

            if (m_Grid.TryGetFirstValue(cellIndex, out T value, out var iterator))
            {
                do
                {
                    if (value.Equals(element))
                    {
                        m_Grid.Remove(iterator);
                        break;
                    }
                }
                while (m_Grid.TryGetNextValue(out value, ref iterator));
            }
        }

        public NativeList<T> Query(float2 center, float radius, Allocator allocator)
        {
            var results = new NativeList<T>(16, allocator);
            var radiusSquared = radius * radius;

            var minCell = GetCellCoord(center - new float2(radius));
            var maxCell = GetCellCoord(center + new float2(radius));

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    var cellIndex = GetCellIndex(x, y);

                    if (m_Grid.TryGetFirstValue(cellIndex, out T value, out var iterator))
                    {
                        do
                        {
                            var elementPos = value.GetPosition();
                            var distanceSquared = math.distancesq(center, elementPos);

                            if (distanceSquared <= radiusSquared)
                            {
                                results.Add(value);
                            }
                        }
                        while (m_Grid.TryGetNextValue(out value, ref iterator));
                    }
                }
            }

            return results;
        }

        public NativeList<T> QueryRect(float2 min, float2 max, Allocator allocator)
        {
            var results = new NativeList<T>(16, allocator);

            var minCell = GetCellCoord(min);
            var maxCell = GetCellCoord(max);

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    var cellIndex = GetCellIndex(x, y);

                    if (m_Grid.TryGetFirstValue(cellIndex, out T value, out var iterator))
                    {
                        do
                        {
                            var elementPos = value.GetPosition();

                            if (elementPos.x >= min.x && elementPos.x <= max.x &&
                                elementPos.y >= min.y && elementPos.y <= max.y)
                            {
                                results.Add(value);
                            }
                        }
                        while (m_Grid.TryGetNextValue(out value, ref iterator));
                    }
                }
            }

            return results;
        }

        private int GetCellIndex(float2 worldPos)
        {
            var coord = GetCellCoord(worldPos);
            return GetCellIndex(coord.x, coord.y);
        }

        private int2 GetCellCoord(float2 worldPos)
        {
            var localPos = worldPos - m_WorldMin;
            return new int2(
                math.clamp((int)(localPos.x / m_CellSize), 0, m_GridSize.x - 1),
                math.clamp((int)(localPos.y / m_CellSize), 0, m_GridSize.y - 1)
            );
        }

        private int GetCellIndex(int x, int y)
        {
            return y * m_GridSize.x + x;
        }

        public void Clear()
        {
            m_Grid.Clear();
        }

        public void Dispose()
        {
            if (m_Grid.IsCreated)
                m_Grid.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_Grid.Dispose(inputDeps);
        }
    }

    public interface ISpatialElement : IEquatable<ISpatialElement>
    {
        float2 GetPosition();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SpatialPoint : ISpatialElement
    {
        public int Id;
        public float2 Position;

        public SpatialPoint(int id, float2 position)
        {
            Id = id;
            Position = position;
        }

        public float2 GetPosition() => Position;

        public bool Equals(ISpatialElement other)
        {
            return other is SpatialPoint point && Id == point.Id;
        }

        public override bool Equals(object obj) => obj is SpatialPoint other && Equals(other);
        public override int GetHashCode() => Id.GetHashCode();
    }
}