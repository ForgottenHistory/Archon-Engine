using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Spatial hash grid for accelerating Bézier curve lookups
    /// Divides map into uniform grid cells, each storing which curve segments intersect it
    /// Reduces segment testing from O(all segments) to O(segments in cell) - typically 100x+ faster
    /// </summary>
    public class SpatialHashGrid
    {
        // Grid parameters
        public readonly int CellSize;      // Pixels per cell (e.g., 64)
        public readonly int GridWidth;     // Number of cells horizontally
        public readonly int GridHeight;    // Number of cells vertically
        public readonly int MapWidth;
        public readonly int MapHeight;

        // CPU data: for each cell, which segments touch it
        private List<uint>[] cellSegments;  // Array of lists [gridWidth × gridHeight]

        // GPU-ready data: flattened for structured buffers
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct CellRange
        {
            public uint startIndex;  // Start index in flatSegmentIndices
            public uint count;       // Number of segments in this cell
        }

        private CellRange[] cellRanges;           // Per-cell ranges
        private uint[] flatSegmentIndices;        // Flat array of segment indices (uint for GPU alignment)

        public SpatialHashGrid(int mapWidth, int mapHeight, int cellSize = 64)
        {
            MapWidth = mapWidth;
            MapHeight = mapHeight;
            CellSize = cellSize;

            // Calculate grid dimensions
            GridWidth = (mapWidth + cellSize - 1) / cellSize;
            GridHeight = (mapHeight + cellSize - 1) / cellSize;

            // Initialize cell lists
            int totalCells = GridWidth * GridHeight;
            cellSegments = new List<uint>[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                cellSegments[i] = new List<uint>();
            }

            ArchonLogger.Log($"SpatialHashGrid: Created {GridWidth}×{GridHeight} grid ({totalCells} cells) for {mapWidth}×{mapHeight} map", "map_initialization");
        }

        /// <summary>
        /// Add a Bézier segment to all grid cells it intersects
        /// </summary>
        public void AddSegment(uint segmentIndex, BezierSegment segment)
        {
            // Calculate bounding box of segment
            float minX = Mathf.Min(segment.P0.x, segment.P1.x, segment.P2.x, segment.P3.x);
            float minY = Mathf.Min(segment.P0.y, segment.P1.y, segment.P2.y, segment.P3.y);
            float maxX = Mathf.Max(segment.P0.x, segment.P1.x, segment.P2.x, segment.P3.x);
            float maxY = Mathf.Max(segment.P0.y, segment.P1.y, segment.P2.y, segment.P3.y);

            // Expand bounding box by a small margin (for curve thickness and anti-aliasing)
            const float margin = 3.0f; // pixels
            minX -= margin;
            minY -= margin;
            maxX += margin;
            maxY += margin;

            // Find which grid cells this bounding box intersects
            int minCellX = Mathf.Max(0, (int)(minX / CellSize));
            int minCellY = Mathf.Max(0, (int)(minY / CellSize));
            int maxCellX = Mathf.Min(GridWidth - 1, (int)(maxX / CellSize));
            int maxCellY = Mathf.Min(GridHeight - 1, (int)(maxY / CellSize));

            // Add segment to all intersecting cells
            for (int cy = minCellY; cy <= maxCellY; cy++)
            {
                for (int cx = minCellX; cx <= maxCellX; cx++)
                {
                    int cellIdx = cy * GridWidth + cx;
                    cellSegments[cellIdx].Add(segmentIndex);
                }
            }
        }

        /// <summary>
        /// Finalize grid and prepare GPU-ready data
        /// Call this after all segments have been added
        /// </summary>
        public void Finalize()
        {
            // Allocate arrays for GPU data
            int totalCells = GridWidth * GridHeight;
            cellRanges = new CellRange[totalCells];

            // Count total segment references
            int totalSegmentRefs = 0;
            for (int i = 0; i < totalCells; i++)
            {
                totalSegmentRefs += cellSegments[i].Count;
            }

            // Flatten segment indices into single array
            flatSegmentIndices = new uint[totalSegmentRefs];
            int writeIndex = 0;

            for (int i = 0; i < totalCells; i++)
            {
                cellRanges[i].startIndex = (uint)writeIndex;
                cellRanges[i].count = (uint)cellSegments[i].Count;

                // Copy segment indices for this cell
                foreach (uint segIdx in cellSegments[i])
                {
                    flatSegmentIndices[writeIndex++] = segIdx;
                }
            }

            // Log statistics
            int minSegments = int.MaxValue;
            int maxSegments = 0;
            int totalSegments = 0;
            int emptyCells = 0;

            for (int i = 0; i < totalCells; i++)
            {
                int count = cellSegments[i].Count;
                if (count == 0) emptyCells++;
                if (count > 0)
                {
                    minSegments = Mathf.Min(minSegments, count);
                    maxSegments = Mathf.Max(maxSegments, count);
                }
                totalSegments += count;
            }

            float avgSegments = totalSegments / (float)totalCells;

            ArchonLogger.Log($"SpatialHashGrid: Finalized - {totalCells} cells, {totalSegmentRefs} total segment references", "map_initialization");
            ArchonLogger.Log($"  Segments per cell: min={minSegments}, max={maxSegments}, avg={avgSegments:F1}, empty={emptyCells}", "map_initialization");
        }

        /// <summary>
        /// Get GPU-ready cell range data
        /// </summary>
        public CellRange[] GetCellRanges()
        {
            return cellRanges;
        }

        /// <summary>
        /// Get GPU-ready flattened segment indices
        /// </summary>
        public uint[] GetFlatSegmentIndices()
        {
            return flatSegmentIndices;
        }

        /// <summary>
        /// Get number of cells in the grid
        /// </summary>
        public int GetCellCount()
        {
            return GridWidth * GridHeight;
        }

        /// <summary>
        /// Get total number of segment references (with duplicates across cells)
        /// </summary>
        public int GetTotalSegmentReferences()
        {
            return flatSegmentIndices != null ? flatSegmentIndices.Length : 0;
        }
    }
}
