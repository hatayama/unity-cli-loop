namespace io.github.hatayama.UnityCliLoop.Domain
{
    // Domain reload is a tool-level lifecycle state, while Unity hooks and lock files stay behind this port.
    public interface IDomainReloadDetectionService
    {
        void RegisterForEditorStartup();
        void StartDomainReload(string correlationId, bool serverIsRunning);
        void CompleteDomainReload(string correlationId);
        void RollbackDomainReloadStart(string correlationId);
        bool ShouldShowReconnectingUI();
        void DeleteLockFile();
        bool IsLockFilePresent();
    }
}
