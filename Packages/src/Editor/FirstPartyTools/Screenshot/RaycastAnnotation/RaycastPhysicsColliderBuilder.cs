#nullable enable
using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds physics-collider UIElementInfo records from clustered raycast grid samples.
    /// </summary>
    internal static class RaycastPhysicsColliderBuilder
    {
        // why: shared with RaycastGridAnnotator so sample loop density and cell half-steps stay in lockstep
        internal const int CLUSTERED_GRID_COLUMNS = 40;
        internal const int CLUSTERED_GRID_ROWS = 40;
        // why: shared with UIElementAnnotationRenderer so physics outline Y-flip keeps matching element Type
        internal const string PhysicsColliderElementType = "PhysicsCollider";

        // Split the reachable cluster into 4-connected regions and materialize one UIElementInfo per region.
        // Why: a single GameObject can produce multiple visually closed outline regions when UI occlusion
        // splits its reachable samples, and agents need one annotation per closed region so they can address
        // each one with its own Label, Bounds, SimX/SimY, and RaycastOutlineSegments.
        internal static List<UIElementInfo> CreateComponentElements(
            RaycastClusterInfo reachableCluster,
            RaycastColliderMetadata metadata,
            RaycastSampleCoverage sampleCoverage,
            int startLabelNumber)
        {
            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(reachableCluster.Samples);
            components.Sort(CompareComponentsByTopLeft);

            List<UIElementInfo> componentElements = new List<UIElementInfo>();
            for (int j = 0; j < components.Count; j++)
            {
                List<RaycastClusterSample> componentSamples = components[j];
                RaycastClusterInfo componentCluster = new RaycastClusterInfo
                {
                    Samples = componentSamples,
                    SampleCount = componentSamples.Count,
                    Representative = RaycastHitClusterer.SelectRepresentativeSample(componentSamples)
                };
                componentElements.Add(CreatePhysicsColliderElement(
                    $"R{startLabelNumber + j}",
                    componentCluster,
                    metadata,
                    sampleCoverage));
            }
            return componentElements;
        }

        internal static UIElementInfo CreatePhysicsColliderElement(
            string label,
            RaycastClusterInfo cluster,
            RaycastColliderMetadata metadata,
            RaycastSampleCoverage sampleCoverage)
        {
            Debug.Assert(cluster.Samples.Count > 0, "Physics collider cluster must contain sampled hits.");
            RaycastClusterSample representative = cluster.Representative;
            RaycastSampleBounds sampleBounds = CalculateSampleCellBounds(cluster.Samples, sampleCoverage);
            List<RaycastOutlineSegment> outlineSegments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(cluster.Samples, sampleCoverage);

            UIElementInfo element = new UIElementInfo
            {
                Label = label,
                Name = metadata.Name,
                Path = metadata.Path,
                Type = PhysicsColliderElementType,
                Interaction = "Raycast",
                SimX = representative.InputX,
                SimY = representative.InputY,
                BoundsMinX = sampleBounds.MinX,
                BoundsMinY = sampleBounds.MinY,
                BoundsMaxX = sampleBounds.MaxX,
                BoundsMaxY = sampleBounds.MaxY,
                SortingOrder = 0,
                SiblingIndex = 0,
                Layer = metadata.Layer,
                Components = new List<string>(metadata.Components),
                RaycastOutlineSegments = outlineSegments
            };

            Debug.Assert(
                element.SimX >= element.BoundsMinX &&
                element.SimX <= element.BoundsMaxX &&
                element.SimY >= element.BoundsMinY &&
                element.SimY <= element.BoundsMaxY,
                "Physics collider bounds must use the same top-left input coordinate space as SimX/SimY.");
            return element;
        }

        internal static RaycastSampleCoverage CreateClusterSampleCoverage(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            float stepX = renderingImageSize.x / (CLUSTERED_GRID_COLUMNS + 1f);
            float stepY = renderingImageSize.y / (CLUSTERED_GRID_ROWS + 1f);
            return new RaycastSampleCoverage(
                stepX / 2f,
                stepY / 2f,
                0f,
                imageToInputOffsetY,
                renderingImageSize.x,
                imageToInputOffsetY + renderingImageSize.y);
        }

        internal static RaycastColliderMetadata CreateColliderMetadata(Collider collider)
        {
            GameObject hitObject = collider.gameObject;
            return new RaycastColliderMetadata
            {
                Name = hitObject.name,
                Path = GameObjectPathUtility.GetFullPath(hitObject),
                Layer = LayerMask.LayerToName(hitObject.layer),
                Components = GetRelevantComponentTypeNames(hitObject)
            };
        }

        internal static List<string> GetRelevantComponentTypeNames(GameObject hitObject)
        {
            List<string> componentTypeNames = new List<string>();
            HashSet<System.Type> seenTypes = new HashSet<System.Type>();
            Component[] components = hitObject.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                if (!(component is Collider) && !(component is MonoBehaviour))
                {
                    continue;
                }

                System.Type componentType = component.GetType();
                if (seenTypes.Contains(componentType))
                {
                    continue;
                }

                seenTypes.Add(componentType);
                componentTypeNames.Add(componentType.Name);
            }

            return componentTypeNames;
        }

        // Order components so labels are assigned in a fully deterministic top-left-first sequence.
        // Why: List<T>.Sort is not stable, so two components sharing the same min InputY and min InputX
        // would otherwise flip order between runs. The min (Row, Column) tiebreaker pins the order.
        private static int CompareComponentsByTopLeft(
            List<RaycastClusterSample> left,
            List<RaycastClusterSample> right)
        {
            float leftMinY = MinInputY(left);
            float rightMinY = MinInputY(right);
            int yComparison = leftMinY.CompareTo(rightMinY);
            if (yComparison != 0)
            {
                return yComparison;
            }

            float leftMinX = MinInputX(left);
            float rightMinX = MinInputX(right);
            int xComparison = leftMinX.CompareTo(rightMinX);
            if (xComparison != 0)
            {
                return xComparison;
            }

            (int, int) leftMinCell = MinRowColumn(left);
            (int, int) rightMinCell = MinRowColumn(right);
            int rowComparison = leftMinCell.Item1.CompareTo(rightMinCell.Item1);
            if (rowComparison != 0)
            {
                return rowComparison;
            }
            return leftMinCell.Item2.CompareTo(rightMinCell.Item2);
        }

        private static float MinInputX(List<RaycastClusterSample> samples)
        {
            float minValue = samples[0].InputX;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].InputX < minValue)
                {
                    minValue = samples[i].InputX;
                }
            }
            return minValue;
        }

        private static float MinInputY(List<RaycastClusterSample> samples)
        {
            float minValue = samples[0].InputY;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].InputY < minValue)
                {
                    minValue = samples[i].InputY;
                }
            }
            return minValue;
        }

        private static (int, int) MinRowColumn(List<RaycastClusterSample> samples)
        {
            (int, int) minCell = (samples[0].Row, samples[0].Column);
            for (int i = 1; i < samples.Count; i++)
            {
                (int, int) candidate = (samples[i].Row, samples[i].Column);
                if (candidate.Item1 < minCell.Item1 ||
                    (candidate.Item1 == minCell.Item1 && candidate.Item2 < minCell.Item2))
                {
                    minCell = candidate;
                }
            }
            return minCell;
        }

        private static RaycastSampleBounds CalculateSampleCellBounds(
            List<RaycastClusterSample> samples,
            RaycastSampleCoverage sampleCoverage)
        {
            Debug.Assert(samples.Count > 0, "At least one raycast sample is required.");

            float minX = samples[0].InputX - sampleCoverage.HalfStepX;
            float minY = samples[0].InputY - sampleCoverage.HalfStepY;
            float maxX = samples[0].InputX + sampleCoverage.HalfStepX;
            float maxY = samples[0].InputY + sampleCoverage.HalfStepY;

            for (int i = 1; i < samples.Count; i++)
            {
                RaycastClusterSample sample = samples[i];
                minX = Mathf.Min(minX, sample.InputX - sampleCoverage.HalfStepX);
                minY = Mathf.Min(minY, sample.InputY - sampleCoverage.HalfStepY);
                maxX = Mathf.Max(maxX, sample.InputX + sampleCoverage.HalfStepX);
                maxY = Mathf.Max(maxY, sample.InputY + sampleCoverage.HalfStepY);
            }

            return new RaycastSampleBounds(
                Mathf.Clamp(minX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(minY, sampleCoverage.MinY, sampleCoverage.MaxY),
                Mathf.Clamp(maxX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(maxY, sampleCoverage.MinY, sampleCoverage.MaxY));
        }

        private readonly struct RaycastSampleBounds
        {
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;

            public RaycastSampleBounds(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }
    }
}
