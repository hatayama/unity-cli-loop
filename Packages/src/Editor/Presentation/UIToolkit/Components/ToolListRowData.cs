using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Row model for the Tool Settings virtualized list (header, tool, or details).
    /// </summary>
    internal sealed class ToolListRowData
    {
        public readonly bool IsHeader;
        public readonly bool IsDetails;
        public readonly string ToolName;
        public readonly string Label;
        public readonly string SkillDescription;
        public bool IsEnabled;
        public ToolSettingsSection Owner;
        public bool IsTool => !IsHeader && !IsDetails;

        private ToolListRowData(
            bool isHeader,
            bool isDetails,
            string toolName,
            string label,
            string skillDescription,
            bool isEnabled)
        {
            IsHeader = isHeader;
            IsDetails = isDetails;
            ToolName = toolName;
            Label = label;
            SkillDescription = skillDescription;
            IsEnabled = isEnabled;
        }

        public static ToolListRowData CreateHeader(string label)
        {
            return new ToolListRowData(
                true,
                false,
                string.Empty,
                label,
                string.Empty,
                true);
        }

        public static ToolListRowData CreateTool(ToolToggleItem item)
        {
            return new ToolListRowData(
                false,
                false,
                item.ToolName,
                item.ToolName,
                item.SkillDescription,
                item.IsEnabled);
        }

        public static ToolListRowData CreateDetails(ToolListRowData toolRow)
        {
            Debug.Assert(toolRow != null, "toolRow must not be null");
            Debug.Assert(toolRow.IsTool, "toolRow must be a tool row");

            return new ToolListRowData(
                false,
                true,
                toolRow.ToolName,
                string.Empty,
                toolRow.SkillDescription,
                true);
        }
    }
}
