using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Port for domain reload lifecycle state and external lock-file signaling.
    /// <summary>
    /// Defines the Domain Reload Detection operations required by the owning workflow.
    /// </summary>
    public interface IDomainReloadDetectionService
    {
        void RegisterForEditorStartup();
        void StartDomainReload(string correlationId, bool serverIsRunning);
        void CompleteDomainReload(string correlationId);
        void RollbackDomainReloadStart(string correlationId);
        bool IsDomainReloadInProgress();
        bool ShouldShowReconnectingUI();
        bool IsAfterCompile();
        void DeleteLockFile();
        bool IsLockFilePresent();
    }

    // Static facade retained for Unity callbacks and recovery paths outside constructor control.
    /// <summary>
    /// Provides Domain Reload Detection operations for its owning module.
    /// </summary>
    public static class DomainReloadDetectionService
    {
        private static IDomainReloadDetectionService ServiceValue;

        internal static void RegisterService(IDomainReloadDetectionService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        internal static void RegisterForEditorStartup()
        {
            Service.RegisterForEditorStartup();
        }

        public static void StartDomainReload(string correlationId, bool serverIsRunning)
        {
            Service.StartDomainReload(correlationId, serverIsRunning);
        }

        public static void CompleteDomainReload(string correlationId)
        {
            Service.CompleteDomainReload(correlationId);
        }

        internal static void RollbackDomainReloadStart(string correlationId)
        {
            Service.RollbackDomainReloadStart(correlationId);
        }

        public static bool IsDomainReloadInProgress()
        {
            return Service.IsDomainReloadInProgress();
        }

        public static bool ShouldShowReconnectingUI()
        {
            return Service.ShouldShowReconnectingUI();
        }

        public static bool IsAfterCompile()
        {
            return Service.IsAfterCompile();
        }

        public static void DeleteLockFile()
        {
            Service.DeleteLockFile();
        }

        public static bool IsLockFilePresent()
        {
            return Service.IsLockFilePresent();
        }

        private static IDomainReloadDetectionService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop domain reload detection service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
