namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// One added-field ledger row for --status Kind "AddedField".
    /// </summary>
    public sealed class HotReloadAddedFieldDescription
    {
        public string ProjectRelativePath { get; }

        public string TypeName { get; }

        public string FieldName { get; }

        public HotReloadAddedFieldDescription(
            string projectRelativePath,
            string typeName,
            string fieldName)
        {
            ProjectRelativePath = projectRelativePath ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            FieldName = fieldName ?? string.Empty;
        }
    }
}
