using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Builds a stable layout signature for Tool Settings list rebuild decisions.
    /// </summary>
    internal static class ToolSettingsSectionLayoutSignature
    {
        public static string Create(ToolSettingsSectionData data)
        {
            Debug.Assert(data != null, "data must not be null");

            StringBuilder builder = new();
            AppendGroup(builder, data.BuiltInTools, "B");
            AppendGroup(builder, data.ThirdPartyTools, "T");
            return builder.ToString();
        }

        private static void AppendGroup(
            StringBuilder builder,
            IReadOnlyList<ToolToggleItem> items,
            string group)
        {
            Debug.Assert(builder != null, "builder must not be null");
            Debug.Assert(items != null, "items must not be null");
            Debug.Assert(!string.IsNullOrEmpty(group), "group must not be null or empty");

            builder.Append(group);
            builder.Append(':');
            for (int i = 0; i < items.Count; i++)
            {
                ToolToggleItem item = items[i];
                builder.Append(item.ToolName);
                builder.Append('|');
                builder.Append(item.SkillDescription);
                builder.Append('|');
            }

            builder.Append(';');
        }
    }
}
