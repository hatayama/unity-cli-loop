using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Throttles background migration progress and applies fresh updates on Unity's main thread.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationProgressReporter
        : IProgress<ThirdPartyToolMigrationProgress>
    {
        private readonly CancellationToken _ct;
        private readonly Func<CancellationToken, bool> _hasActiveOperation;
        private readonly Action<ThirdPartyToolMigrationProgress> _applyProgress;
        private long _lastReportTimestamp;

        internal ThirdPartyToolMigrationProgressReporter(
            CancellationToken ct,
            Func<CancellationToken, bool> hasActiveOperation,
            Action<ThirdPartyToolMigrationProgress> applyProgress)
        {
            Debug.Assert(hasActiveOperation != null, "hasActiveOperation must not be null");
            Debug.Assert(applyProgress != null, "applyProgress must not be null");

            _ct = ct;
            _hasActiveOperation = hasActiveOperation
                ?? throw new ArgumentNullException(nameof(hasActiveOperation));
            _applyProgress = applyProgress
                ?? throw new ArgumentNullException(nameof(applyProgress));
        }

        public void Report(ThirdPartyToolMigrationProgress value)
        {
            if (_ct.IsCancellationRequested)
            {
                return;
            }

            long currentTimestamp = Stopwatch.GetTimestamp();
            if (!ThirdPartyToolMigrationWizardStateRules.ShouldReportMigrationProgress(
                    _lastReportTimestamp,
                    currentTimestamp,
                    value,
                    Stopwatch.Frequency,
                    ThirdPartyToolMigrationWizardStateRules.MigrationProgressUiUpdateIntervalMilliseconds))
            {
                return;
            }

            _lastReportTimestamp = currentTimestamp;
            _ = ReportAsync(value, _ct);
        }

        private async Task ReportAsync(ThirdPartyToolMigrationProgress value, CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread();
            if (!ThirdPartyToolMigrationWizardStateRules.ShouldApplyMigrationProgress(
                    ct.IsCancellationRequested,
                    _hasActiveOperation(ct)))
            {
                return;
            }

            _applyProgress(value);
        }
    }
}
