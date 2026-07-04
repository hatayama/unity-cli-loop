#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Builds screen-space outlines from sampled raycast hit cells.
    /// </summary>
    internal static class RaycastSampleOutlineBuilder
    {
        internal static List<RaycastOutlineSegment> CreateOutlineSegments(
            List<RaycastClusterSample> samples,
            RaycastSampleCoverage sampleCoverage)
        {
            Debug.Assert(samples != null, "Raycast samples must not be null.");
            List<RaycastClusterSample> validSamples = samples!;
            HashSet<RaycastSampleCellKey> occupiedCells = CreateOccupiedCells(validSamples);
            List<RaycastOutlineSegment> segments = new List<RaycastOutlineSegment>();

            foreach (RaycastClusterSample sample in validSamples)
            {
                if (!HasGridCell(sample))
                {
                    continue;
                }

                RaycastSampleCellBounds cellBounds = CalculateCellBounds(sample, sampleCoverage);
                AddBoundarySegments(sample, cellBounds, occupiedCells, segments);
            }

            return MergeCollinearSegments(segments);
        }

        private static HashSet<RaycastSampleCellKey> CreateOccupiedCells(List<RaycastClusterSample> samples)
        {
            HashSet<RaycastSampleCellKey> occupiedCells = new HashSet<RaycastSampleCellKey>();
            foreach (RaycastClusterSample sample in samples)
            {
                if (!HasGridCell(sample))
                {
                    continue;
                }

                occupiedCells.Add(new RaycastSampleCellKey(sample.Row, sample.Column));
            }

            return occupiedCells;
        }

        private static bool HasGridCell(RaycastClusterSample sample)
        {
            return sample.Row > 0 && sample.Column > 0;
        }

        private static void AddBoundarySegments(
            RaycastClusterSample sample,
            RaycastSampleCellBounds cellBounds,
            HashSet<RaycastSampleCellKey> occupiedCells,
            List<RaycastOutlineSegment> segments)
        {
            if (!occupiedCells.Contains(new RaycastSampleCellKey(sample.Row - 1, sample.Column)))
            {
                segments.Add(new RaycastOutlineSegment(
                    cellBounds.MinX,
                    cellBounds.MinY,
                    cellBounds.MaxX,
                    cellBounds.MinY));
            }

            if (!occupiedCells.Contains(new RaycastSampleCellKey(sample.Row + 1, sample.Column)))
            {
                segments.Add(new RaycastOutlineSegment(
                    cellBounds.MinX,
                    cellBounds.MaxY,
                    cellBounds.MaxX,
                    cellBounds.MaxY));
            }

            if (!occupiedCells.Contains(new RaycastSampleCellKey(sample.Row, sample.Column - 1)))
            {
                segments.Add(new RaycastOutlineSegment(
                    cellBounds.MinX,
                    cellBounds.MinY,
                    cellBounds.MinX,
                    cellBounds.MaxY));
            }

            if (!occupiedCells.Contains(new RaycastSampleCellKey(sample.Row, sample.Column + 1)))
            {
                segments.Add(new RaycastOutlineSegment(
                    cellBounds.MaxX,
                    cellBounds.MinY,
                    cellBounds.MaxX,
                    cellBounds.MaxY));
            }
        }

        private static RaycastSampleCellBounds CalculateCellBounds(
            RaycastClusterSample sample,
            RaycastSampleCoverage sampleCoverage)
        {
            return new RaycastSampleCellBounds(
                Mathf.Clamp(sample.InputX - sampleCoverage.HalfStepX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(sample.InputY - sampleCoverage.HalfStepY, sampleCoverage.MinY, sampleCoverage.MaxY),
                Mathf.Clamp(sample.InputX + sampleCoverage.HalfStepX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(sample.InputY + sampleCoverage.HalfStepY, sampleCoverage.MinY, sampleCoverage.MaxY));
        }

        private static List<RaycastOutlineSegment> MergeCollinearSegments(List<RaycastOutlineSegment> segments)
        {
            segments.Sort(CompareSegments);
            List<RaycastOutlineSegment> mergedSegments = new List<RaycastOutlineSegment>();

            foreach (RaycastOutlineSegment segment in segments)
            {
                if (mergedSegments.Count == 0)
                {
                    mergedSegments.Add(segment);
                    continue;
                }

                int lastIndex = mergedSegments.Count - 1;
                RaycastOutlineSegment lastSegment = mergedSegments[lastIndex];
                if (!CanMerge(lastSegment, segment))
                {
                    mergedSegments.Add(segment);
                    continue;
                }

                mergedSegments[lastIndex] = MergeSegments(lastSegment, segment);
            }

            return mergedSegments;
        }

        private static int CompareSegments(RaycastOutlineSegment left, RaycastOutlineSegment right)
        {
            int orientationComparison = GetOrientationIndex(left).CompareTo(GetOrientationIndex(right));
            if (orientationComparison != 0)
            {
                return orientationComparison;
            }

            if (IsHorizontal(left))
            {
                int yComparison = left.StartY.CompareTo(right.StartY);
                if (yComparison != 0)
                {
                    return yComparison;
                }

                return left.StartX.CompareTo(right.StartX);
            }

            int xComparison = left.StartX.CompareTo(right.StartX);
            if (xComparison != 0)
            {
                return xComparison;
            }

            return left.StartY.CompareTo(right.StartY);
        }

        private static bool CanMerge(RaycastOutlineSegment left, RaycastOutlineSegment right)
        {
            if (IsHorizontal(left) && IsHorizontal(right))
            {
                return Approximately(left.StartY, right.StartY) &&
                       Approximately(left.EndY, right.EndY) &&
                       Approximately(left.EndX, right.StartX);
            }

            if (IsVertical(left) && IsVertical(right))
            {
                return Approximately(left.StartX, right.StartX) &&
                       Approximately(left.EndX, right.EndX) &&
                       Approximately(left.EndY, right.StartY);
            }

            return false;
        }

        private static RaycastOutlineSegment MergeSegments(
            RaycastOutlineSegment left,
            RaycastOutlineSegment right)
        {
            if (IsHorizontal(left))
            {
                return new RaycastOutlineSegment(left.StartX, left.StartY, right.EndX, right.EndY);
            }

            return new RaycastOutlineSegment(left.StartX, left.StartY, right.EndX, right.EndY);
        }

        private static int GetOrientationIndex(RaycastOutlineSegment segment)
        {
            if (IsHorizontal(segment))
            {
                return 0;
            }

            return 1;
        }

        private static bool IsHorizontal(RaycastOutlineSegment segment)
        {
            return Approximately(segment.StartY, segment.EndY);
        }

        private static bool IsVertical(RaycastOutlineSegment segment)
        {
            return Approximately(segment.StartX, segment.EndX);
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.001f;
        }

        private readonly struct RaycastSampleCellKey
        {
            private readonly int _row;
            private readonly int _column;

            public RaycastSampleCellKey(int row, int column)
            {
                _row = row;
                _column = column;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is RaycastSampleCellKey other))
                {
                    return false;
                }

                return _row == other._row && _column == other._column;
            }

            public override int GetHashCode()
            {
                return (_row * 397) ^ _column;
            }
        }

        private readonly struct RaycastSampleCellBounds
        {
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;

            public RaycastSampleCellBounds(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }
    }

    /// <summary>
    /// Represents one axis-aligned outline segment in top-left Game View input coordinates.
    /// </summary>
    internal readonly struct RaycastOutlineSegment
    {
        public readonly float StartX;
        public readonly float StartY;
        public readonly float EndX;
        public readonly float EndY;

        public RaycastOutlineSegment(float startX, float startY, float endX, float endY)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
        }
    }
}
