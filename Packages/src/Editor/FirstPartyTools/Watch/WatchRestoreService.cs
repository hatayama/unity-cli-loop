using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Re-registers the watch expressions a domain reload dropped, recompiling each stored
    /// expression because the evaluator assembly itself cannot survive the reload.
    /// </summary>
    internal sealed class WatchRestoreService
    {
        private readonly WatchExpressionRegistry _registry;
        private readonly IWatchExpressionCompiler _compiler;
        private readonly IWatchPersistenceStore _store;
        private readonly Action _ensureMonitorStarted;

        public WatchRestoreService(
            WatchExpressionRegistry registry,
            IWatchExpressionCompiler compiler,
            IWatchPersistenceStore store,
            Action ensureMonitorStarted)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _ensureMonitorStarted = ensureMonitorStarted
                ?? throw new ArgumentNullException(nameof(ensureMonitorStarted));
        }

        public async Task<WatchRestoreReport> RestoreAsync(CancellationToken ct)
        {
            IReadOnlyList<WatchPersistedRecord> records = _store.Load();
            if (records.Count == 0)
            {
                return WatchRestoreReport.Empty;
            }

            List<string> warnings = new();
            int restoredCount = 0;
            // Sequential, not parallel: the registry evaluates watches in registration order and
            // the restored order has to match the order the user registered them in.
            foreach (WatchPersistedRecord record in records)
            {
                if (await TryRestoreAsync(record, warnings, ct))
                {
                    restoredCount++;
                }
            }

            if (restoredCount > 0)
            {
                _ensureMonitorStarted();
            }

            // Overwrite with what actually made it back, so a watch that no longer compiles is
            // reported once instead of failing again on every later reload.
            _store.Save(SnapshotRegistry());
            return new WatchRestoreReport(restoredCount, warnings);
        }

        private async Task<bool> TryRestoreAsync(
            WatchPersistedRecord record,
            List<string> warnings,
            CancellationToken ct)
        {
            WatchCompilationResult compiled = await _compiler.CompileAsync(record.Expression, ct);
            // Why switch back: CompileAsync resumes off-thread, but the registry evaluates the
            // expression on registration and must run on the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!compiled.Success)
            {
                warnings.Add(
                    $"Watch '{record.Id}' was not restored after the domain reload because its "
                    + $"expression no longer compiles: {DescribeCompilationFailure(compiled)}");
                return false;
            }

            WatchRegistrationResult registered = _registry.Register(
                record.Id,
                record.Expression,
                compiled.Evaluator,
                record.MaxHistory);
            // A duplicate id means the user re-registered the watch while restore was compiling.
            // Their registration is the newer intent, so it wins and the skip stays silent.
            return registered.Success;
        }

        private static string DescribeCompilationFailure(WatchCompilationResult compiled)
        {
            if (compiled.CompilationErrors != null && compiled.CompilationErrors.Count > 0)
            {
                return compiled.CompilationErrors[0].Message;
            }

            return compiled.ErrorMessage;
        }

        private IReadOnlyList<WatchPersistedRecord> SnapshotRegistry()
        {
            IReadOnlyList<WatchExpressionEntry> entries = _registry.GetEntries();
            List<WatchPersistedRecord> records = new(entries.Count);
            foreach (WatchExpressionEntry entry in entries)
            {
                records.Add(new WatchPersistedRecord
                {
                    Id = entry.Id,
                    Expression = entry.Expression,
                    MaxHistory = entry.MaxHistory
                });
            }

            return records;
        }
    }
}
