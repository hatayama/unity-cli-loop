
namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Provides Unity CLI Loop Editor Domain Reload State dependencies to callers without exposing construction details.
    /// </summary>
    public sealed class UnityCliLoopEditorDomainReloadStateProvider : IDomainReloadStateProvider
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

    /// <summary>
    /// Provides Unity CLI Loop Editor Domain Reload State Registration behavior for Unity CLI Loop.
    /// </summary>
    public static class UnityCliLoopEditorDomainReloadStateRegistration
    {
        internal static void RegisterForEditorStartup()
        {
            DomainReloadStateRegistry.RegisterProvider(new UnityCliLoopEditorDomainReloadStateProvider());
        }
    }
}
