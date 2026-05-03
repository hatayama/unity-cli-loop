using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    public sealed class McpEditorDomainReloadStateProvider : IDomainReloadStateProvider
    {
        private static volatile bool _isDomainReloadInProgress;

        public bool IsDomainReloadInProgress()
        {
            return _isDomainReloadInProgress;
        }

        public static void SetDomainReloadInProgressFromMainThread(bool isDomainReloadInProgress)
        {
            _isDomainReloadInProgress = isDomainReloadInProgress;
        }
    }

    public static class McpEditorDomainReloadStateRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(
                McpEditorSettings.GetIsDomainReloadInProgress());
            DomainReloadStateRegistry.RegisterProvider(new McpEditorDomainReloadStateProvider());
        }
    }
}
