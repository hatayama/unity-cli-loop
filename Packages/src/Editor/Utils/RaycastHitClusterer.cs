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
                    SampleCount = clusterSamples.Count
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
    }

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
