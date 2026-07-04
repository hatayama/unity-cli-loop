#nullable enable
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Groups raycast samples by collider and chooses a real sampled hit as each representative.
    /// </summary>
    internal static class RaycastHitClusterer
    {
        internal static List<RaycastClusterInfo> CreateClusters(List<RaycastClusterSample> samples)
        {
            System.Diagnostics.Debug.Assert(samples != null, "Raycast samples must not be null.");
            List<RaycastClusterSample> validSamples = samples!;

            Dictionary<int, List<RaycastClusterSample>> samplesByClusterKey =
                new Dictionary<int, List<RaycastClusterSample>>();
            List<int> clusterKeys = new List<int>();

            foreach (RaycastClusterSample sample in validSamples)
            {
                if (!samplesByClusterKey.ContainsKey(sample.ClusterKey))
                {
                    samplesByClusterKey.Add(sample.ClusterKey, new List<RaycastClusterSample>());
                    clusterKeys.Add(sample.ClusterKey);
                }

                samplesByClusterKey[sample.ClusterKey].Add(sample);
            }

            List<RaycastClusterInfo> clusters = new List<RaycastClusterInfo>();
            foreach (int clusterKey in clusterKeys)
            {
                List<RaycastClusterSample> clusterSamples = samplesByClusterKey[clusterKey];
                clusters.Add(new RaycastClusterInfo
                {
                    Representative = SelectRepresentativeSample(clusterSamples),
                    SampleCount = clusterSamples.Count,
                    Samples = new List<RaycastClusterSample>(clusterSamples)
                });
            }

            return clusters;
        }

        internal static RaycastClusterSample SelectRepresentativeSample(List<RaycastClusterSample> samples)
        {
            System.Diagnostics.Debug.Assert(samples != null, "Raycast samples must not be null.");
            List<RaycastClusterSample> validSamples = samples!;
            System.Diagnostics.Debug.Assert(validSamples.Count > 0, "At least one raycast sample is required.");

            Vector2Like centroid = CalculateCentroid(validSamples);
            RaycastClusterSample representative = validSamples[0];
            float nearestSquaredDistance = CalculateSquaredDistance(representative, centroid);

            for (int i = 1; i < validSamples.Count; i++)
            {
                RaycastClusterSample candidate = validSamples[i];
                float candidateSquaredDistance = CalculateSquaredDistance(candidate, centroid);
                if (candidateSquaredDistance >= nearestSquaredDistance)
                {
                    continue;
                }

                representative = candidate;
                nearestSquaredDistance = candidateSquaredDistance;
            }

            return representative;
        }

        internal static RaycastClusterSample? SelectReachableRepresentativeSample(
            List<RaycastClusterSample> samples,
            RaycastClusterSampleOcclusionCheck isOccluded)
        {
            System.Diagnostics.Debug.Assert(samples != null, "Raycast samples must not be null.");
            System.Diagnostics.Debug.Assert(isOccluded != null, "Raycast sample occlusion check must not be null.");
            List<RaycastClusterSample> validSamples = samples!;
            RaycastClusterSampleOcclusionCheck validIsOccluded = isOccluded!;
            System.Diagnostics.Debug.Assert(validSamples.Count > 0, "At least one raycast sample is required.");

            List<RaycastClusterSample> candidates = CreateSamplesOrderedByCentroidDistance(validSamples);
            foreach (RaycastClusterSample candidate in candidates)
            {
                if (validIsOccluded(candidate))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static Vector2Like CalculateCentroid(List<RaycastClusterSample> samples)
        {
            float sumX = 0f;
            float sumY = 0f;
            foreach (RaycastClusterSample sample in samples)
            {
                sumX += sample.InputX;
                sumY += sample.InputY;
            }

            return new Vector2Like
            {
                X = sumX / samples.Count,
                Y = sumY / samples.Count
            };
        }

        private static float CalculateSquaredDistance(RaycastClusterSample sample, Vector2Like point)
        {
            float deltaX = sample.InputX - point.X;
            float deltaY = sample.InputY - point.Y;
            return deltaX * deltaX + deltaY * deltaY;
        }

        private static List<RaycastClusterSample> CreateSamplesOrderedByCentroidDistance(
            List<RaycastClusterSample> samples)
        {
            Vector2Like centroid = CalculateCentroid(samples);
            List<RankedRaycastClusterSample> rankedSamples = new List<RankedRaycastClusterSample>();
            for (int i = 0; i < samples.Count; i++)
            {
                RaycastClusterSample sample = samples[i];
                rankedSamples.Add(new RankedRaycastClusterSample
                {
                    Sample = sample,
                    OriginalIndex = i,
                    SquaredDistance = CalculateSquaredDistance(sample, centroid)
                });
            }

            rankedSamples.Sort(CompareRankedSamples);

            List<RaycastClusterSample> orderedSamples = new List<RaycastClusterSample>();
            foreach (RankedRaycastClusterSample rankedSample in rankedSamples)
            {
                orderedSamples.Add(rankedSample.Sample);
            }

            return orderedSamples;
        }

        private static int CompareRankedSamples(RankedRaycastClusterSample left, RankedRaycastClusterSample right)
        {
            int distanceComparison = left.SquaredDistance.CompareTo(right.SquaredDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private struct RankedRaycastClusterSample
        {
            public RaycastClusterSample Sample { get; set; }
            public int OriginalIndex { get; set; }
            public float SquaredDistance { get; set; }
        }

        private struct Vector2Like
        {
            public float X { get; set; }
            public float Y { get; set; }
        }
    }

    /// <summary>
    /// Represents one screen-space raycast hit sample before collider clustering.
    /// </summary>
    internal sealed class RaycastClusterSample
    {
        public int ClusterKey { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
    }

    /// <summary>
    /// Contains one collider cluster and its selected representative hit sample.
    /// </summary>
    internal sealed class RaycastClusterInfo
    {
        public RaycastClusterSample Representative { get; set; } = new RaycastClusterSample();
        public int SampleCount { get; set; }
        public List<RaycastClusterSample> Samples { get; set; } = new List<RaycastClusterSample>();
    }

    /// <summary>
    /// Reports whether a raycast sample cannot be used as a click target.
    /// </summary>
    internal delegate bool RaycastClusterSampleOcclusionCheck(RaycastClusterSample sample);

    /// <summary>
    /// Carries GameObject metadata for one clustered collider.
    /// </summary>
    internal sealed class RaycastColliderMetadata
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Layer { get; set; } = "";
        public List<string> Components { get; set; } = new List<string>();
    }

    /// <summary>
    /// Keeps pure raycast samples separate from per-collider metadata.
    /// </summary>
    internal sealed class RaycastClusterCollection
    {
        public List<RaycastClusterSample> Samples { get; set; } = new List<RaycastClusterSample>();

        public Dictionary<int, RaycastColliderMetadata> MetadataByClusterKey { get; set; } =
            new Dictionary<int, RaycastColliderMetadata>();
    }
}
