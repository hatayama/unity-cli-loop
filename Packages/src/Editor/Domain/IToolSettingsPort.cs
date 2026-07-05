namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IToolSettingsPort
    {
        bool IsToolEnabled(string toolName);
        void SetToolEnabled(string toolName, bool enabled);
        string[] GetDisabledTools();
        void InvalidateCache();
    }
}
