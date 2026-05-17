namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IDomainReloadDetectionService
    {
        void RegisterForEditorStartup();
        void StartDomainReload(string correlationId, bool serverIsRunning);
        void CompleteDomainReload(string correlationId);
        void RollbackDomainReloadStart(string correlationId);
        bool ShouldShowReconnectingUI();
    }
}
