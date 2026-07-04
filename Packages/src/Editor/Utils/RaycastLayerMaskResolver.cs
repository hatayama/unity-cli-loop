#nullable enable
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Resolves comma-separated physics layer names into a Unity layer mask.
    /// </summary>
    internal static class RaycastLayerMaskResolver
    {
        private const int MIN_LAYER_INDEX = 0;
        private const int MAX_LAYER_INDEX = 31;

        internal static RaycastLayerMaskResolution Resolve(
            string layerNamesText,
            IReadOnlyList<RaycastLayerDefinition> availableLayers)
        {
            System.Diagnostics.Debug.Assert(layerNamesText != null, "Layer mask text must not be null.");
            System.Diagnostics.Debug.Assert(availableLayers != null, "Available layers must not be null.");
            string validLayerNamesText = layerNamesText!;
            IReadOnlyList<RaycastLayerDefinition> validAvailableLayers = availableLayers!;

            List<string> parsedLayerNames = ParseLayerNames(validLayerNamesText);
            List<string> validLayerNames = CreateValidLayerNames(validAvailableLayers);
            if (parsedLayerNames.Count == 0)
            {
                return new RaycastLayerMaskResolution
                {
                    IsValid = true,
                    HasLayerNames = false,
                    Mask = 0,
                    LayerNames = parsedLayerNames,
                    InvalidLayerNames = new List<string>(),
                    ValidLayerNames = validLayerNames
                };
            }

            Dictionary<string, int> layerIndexByName = CreateLayerIndexByName(validAvailableLayers);
            List<string> resolvedLayerNames = new List<string>();
            List<string> invalidLayerNames = new List<string>();
            int mask = 0;

            foreach (string layerName in parsedLayerNames)
            {
                if (!layerIndexByName.ContainsKey(layerName))
                {
                    invalidLayerNames.Add(layerName);
                    continue;
                }

                int layerIndex = layerIndexByName[layerName];
                mask |= 1 << layerIndex;
                resolvedLayerNames.Add(layerName);
            }

            return new RaycastLayerMaskResolution
            {
                IsValid = invalidLayerNames.Count == 0,
                HasLayerNames = true,
                Mask = mask,
                LayerNames = resolvedLayerNames,
                InvalidLayerNames = invalidLayerNames,
                ValidLayerNames = validLayerNames
            };
        }

        private static List<string> ParseLayerNames(string layerNamesText)
        {
            List<string> layerNames = new List<string>();
            HashSet<string> seenLayerNames = new HashSet<string>();
            string[] parts = layerNamesText.Split(',');

            foreach (string part in parts)
            {
                string layerName = part.Trim();
                if (layerName.Length == 0)
                {
                    continue;
                }

                if (seenLayerNames.Contains(layerName))
                {
                    continue;
                }

                seenLayerNames.Add(layerName);
                layerNames.Add(layerName);
            }

            return layerNames;
        }

        private static Dictionary<string, int> CreateLayerIndexByName(
            IReadOnlyList<RaycastLayerDefinition> availableLayers)
        {
            Dictionary<string, int> layerIndexByName = new Dictionary<string, int>();
            foreach (RaycastLayerDefinition layer in availableLayers)
            {
                if (!IsValidLayer(layer))
                {
                    continue;
                }

                if (layerIndexByName.ContainsKey(layer.Name))
                {
                    continue;
                }

                layerIndexByName.Add(layer.Name, layer.Index);
            }

            return layerIndexByName;
        }

        private static List<string> CreateValidLayerNames(
            IReadOnlyList<RaycastLayerDefinition> availableLayers)
        {
            List<string> layerNames = new List<string>();
            foreach (RaycastLayerDefinition layer in availableLayers)
            {
                if (!IsValidLayer(layer))
                {
                    continue;
                }

                layerNames.Add(layer.Name);
            }

            return layerNames;
        }

        private static bool IsValidLayer(RaycastLayerDefinition layer)
        {
            return !string.IsNullOrEmpty(layer.Name) &&
                   layer.Index >= MIN_LAYER_INDEX &&
                   layer.Index <= MAX_LAYER_INDEX;
        }
    }

    /// <summary>
    /// Describes one named Unity physics layer.
    /// </summary>
    internal sealed class RaycastLayerDefinition
    {
        public string Name { get; set; } = "";
        public int Index { get; set; }
    }

    /// <summary>
    /// Carries the result of layer-mask name resolution without throwing from pure logic.
    /// </summary>
    internal sealed class RaycastLayerMaskResolution
    {
        public bool IsValid { get; set; }
        public bool HasLayerNames { get; set; }
        public int Mask { get; set; }
        public List<string> LayerNames { get; set; } = new List<string>();
        public List<string> InvalidLayerNames { get; set; } = new List<string>();
        public List<string> ValidLayerNames { get; set; } = new List<string>();
    }
}
