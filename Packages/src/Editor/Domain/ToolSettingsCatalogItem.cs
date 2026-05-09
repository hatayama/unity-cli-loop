namespace io.github.hatayama.UnityCliLoop.Domain
{
    public class ToolSettingsCatalogItem
    {
        public readonly string Name;
        public readonly bool DisplayDevelopmentOnly;
        public readonly bool IsThirdParty;

        public ToolSettingsCatalogItem(
            string name,
            bool displayDevelopmentOnly,
            bool isThirdParty)
        {
            Name = name;
            DisplayDevelopmentOnly = displayDevelopmentOnly;
            IsThirdParty = isThirdParty;
        }
    }
}
