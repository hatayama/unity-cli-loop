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
                if (ct.IsCancellationRequested)
                {
                    return CancelledReport(restoredCount);
                }

                bool restored;
                try
                {
                    restored = await TryRestoreAsync(record, warnings, ct);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is how clear-watch --all tells restore to stop; it is the
                    // user's intent, not a failure to report.
                    return CancelledReport(restoredCount);
                }

                if (ct.IsCancellationRequested)
                {
                    return CancelledReport(restoredCount);
                }

                if (restored)
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

        // Why no Save here: the only thing that cancels a restore is a clear the user asked for,
        // and that clear already wrote the authoritative store. Saving the registry now would
        // re-persist the records the clear just removed and silently undo it.
        private static WatchRestoreReport CancelledReport(int restoredCount)
        {
            return new WatchRestoreReport(restoredCount, Array.Empty<string>());
        }

        private async Task<bool> TryRestoreAsync(
            WatchPersistedRecord record,
            List<string> warnings,
            CancellationToken ct)
        {
            WatchCompilationResult compiled;
            try
            {
                compiled = await _compiler.CompileAsync(record.Expression, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancellation ends the whole restore, so it must not be turned into a warning here.
                throw;
            }
            catch (Exception exception)
            {
                // The compiler propagates its own failures; one bad expression must not stop the
                // remaining watches from coming back.
                AddDroppedWarning(warnings, record, exception.Message);
                return false;
            }

            // Why switch back: CompileAsync resumes off-thread, but the registry evaluates the
            // expression on registration and must run on the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!compiled.Success)
            {
                AddDroppedWarning(warnings, record, DescribeCompilationFailure(compiled));
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

        // Why check the registry first: if the user re-registered this id while restore was
        // compiling, the watch is not gone and telling them it was dropped would be false.
        private void AddDroppedWarning(List<string> warnings, WatchPersistedRecord record, string reason)
        {
            if (IsRegistered(record.Id))
            {
                return;
            }

            warnings.Add(
                $"Watch '{record.Id}' was not restored after the domain reload because its "
                + $"expression no longer compiles: {reason}");
        }

        private bool IsRegistered(string id)
        {
            foreach (WatchExpressionEntry entry in _registry.GetEntries())
            {
                if (string.Equals(entry.Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
