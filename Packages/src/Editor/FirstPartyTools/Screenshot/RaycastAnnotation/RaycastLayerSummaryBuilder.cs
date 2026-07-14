#nullable enable
using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Aggregates dense raycast grid samples into per-layer summary records.
    /// </summary>
    internal static class RaycastLayerSummaryBuilder
    {
        internal static RaycastLayerHitSample CreateLayerHitSample(GameViewRaycastResult raycastResult)
        {
            RaycastLayerHitSample sample = new RaycastLayerHitSample
            {
                Hit = raycastResult.Hits.Length > 0
            };

            if (!sample.Hit)
            {
                return sample;
            }

            RaycastHit hit = raycastResult.Hits[0];
            sample.HitGameObjectPath = GameObjectPathUtility.GetFullPath(hit.collider.gameObject);
            sample.HitLayerIndex = hit.collider.gameObject.layer;
            sample.HitLayer = LayerMask.LayerToName(hit.collider.gameObject.layer);
            return sample;
        }

        internal static List<RaycastLayerSummaryInfo> CreateLayerSummaries(List<RaycastLayerHitSample> samples)
        {
            Dictionary<int, RaycastLayerSummaryAccumulator> accumulatorsByLayerIndex =
                new Dictionary<int, RaycastLayerSummaryAccumulator>();

            foreach (RaycastLayerHitSample sample in samples)
            {
                if (!sample.Hit || sample.HitLayerIndex == null)
                {
                    continue;
                }

                int layerIndex = sample.HitLayerIndex.Value;
                if (!accumulatorsByLayerIndex.ContainsKey(layerIndex))
                {
                    accumulatorsByLayerIndex.Add(
                        layerIndex,
                        new RaycastLayerSummaryAccumulator(sample.HitLayer ?? "", layerIndex));
                }

                RaycastLayerSummaryAccumulator accumulator = accumulatorsByLayerIndex[layerIndex];
                accumulator.AddHit(sample.HitGameObjectPath ?? "");
            }

            List<RaycastLayerSummaryInfo> summaries = new List<RaycastLayerSummaryInfo>();
            foreach (RaycastLayerSummaryAccumulator accumulator in accumulatorsByLayerIndex.Values)
            {
                summaries.Add(accumulator.CreateSummary());
            }

            summaries.Sort(CompareLayerSummaries);
            return summaries;
        }

        private static int CompareLayerSummaries(
            RaycastLayerSummaryInfo left,
            RaycastLayerSummaryInfo right)
        {
            int hitCountComparison = right.HitCount.CompareTo(left.HitCount);
            if (hitCountComparison != 0)
            {
                return hitCountComparison;
            }

            return left.LayerIndex.CompareTo(right.LayerIndex);
        }

        private sealed class RaycastLayerSummaryAccumulator
        {
            private readonly Dictionary<string, int> _objectHitCounts = new Dictionary<string, int>();
            private string _representativeObjectPath = "";
            private int _representativeObjectHitCount;

            public string Layer { get; }
            public int LayerIndex { get; }
            public int HitCount { get; private set; }

            public RaycastLayerSummaryAccumulator(string layer, int layerIndex)
            {
                Layer = layer;
                LayerIndex = layerIndex;
            }

            public void AddHit(string objectPath)
            {
                HitCount++;
                int objectHitCount = 1;
                if (_objectHitCounts.ContainsKey(objectPath))
                {
                    objectHitCount = _objectHitCounts[objectPath] + 1;
                }

                _objectHitCounts[objectPath] = objectHitCount;
                if (ShouldUseAsRepresentative(objectPath, objectHitCount))
                {
                    _representativeObjectPath = objectPath;
                    _representativeObjectHitCount = objectHitCount;
                }
            }

            public RaycastLayerSummaryInfo CreateSummary()
            {
                return new RaycastLayerSummaryInfo
                {
                    Layer = Layer,
                    LayerIndex = LayerIndex,
                    HitCount = HitCount,
                    RepresentativeObjectPath = _representativeObjectPath
                };
            }

            private bool ShouldUseAsRepresentative(string objectPath, int objectHitCount)
            {
                if (objectHitCount > _representativeObjectHitCount)
                {
                    return true;
                }

                if (objectHitCount < _representativeObjectHitCount)
                {
                    return false;
                }

                return string.CompareOrdinal(objectPath, _representativeObjectPath) < 0;
            }
        }
    }

    /// <summary>
    /// Carries the raycast hit layer/object of one dense grid sample, for internal layer-summary aggregation only.
    /// </summary>
    internal sealed class RaycastLayerHitSample
    {
        public bool Hit { get; set; }
        public string? HitGameObjectPath { get; set; }
        public string? HitLayer { get; set; }
        public int? HitLayerIndex { get; set; }
    }
}
