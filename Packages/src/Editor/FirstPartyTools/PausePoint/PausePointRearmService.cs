using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Replays the enable requests that were persisted before the last domain reload, and reports
    /// what came back, so a pause point armed with --persist survives a compile or a Play entry.
    /// </summary>
    internal sealed class PausePointRearmService
    {
        private readonly IPausePointPersistenceStore _store;
        private readonly Func<EnablePausePointSchema, PausePointResponse> _enable;

        public PausePointRearmService(IPausePointPersistenceStore store)
            : this(store, new PausePointUseCase().Enable)
        {
        }

        public PausePointRearmService(
            IPausePointPersistenceStore store,
            Func<EnablePausePointSchema, PausePointResponse> enable)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _enable = enable ?? throw new ArgumentNullException(nameof(enable));
        }

        public IReadOnlyList<string> RearmAfterDomainReload()
        {
            IReadOnlyList<PausePointPersistedRecord> records = _store.Load();
            // Consume the store first: a re-arm that throws must not be replayed forever on every
            // later reload. The enables below re-persist what actually came back.
            _store.Save(Array.Empty<PausePointPersistedRecord>());

            List<string> report = new(records.Count);
            foreach (PausePointPersistedRecord record in records)
            {
                PausePointResponse response = _enable(record.ToSchema());
                if (response.Success)
                {
                    report.Add(DescribeSuccess(record, response));
                    continue;
                }

                string failure = DescribeFailure(record, response);
                report.Add(failure);
                // A user-facing notice, not a diagnostic: the pause point they asked to keep is
                // gone, and they need to see that in the Console and in get-logs.
                Debug.LogWarning(failure);
            }

            UloopPausePointRegistry.SetDomainReloadRearmReport(report);
            return report;
        }

        private static string DescribeSuccess(PausePointPersistedRecord record, PausePointResponse response)
        {
            StringBuilder line = new();
            line.Append("Re-armed pause point '").Append(record.RegistryId).Append("' after the domain reload");
            if (!string.IsNullOrEmpty(record.File))
            {
                line.Append(" (").Append(record.File).Append(':').Append(response.ResolvedLine);
                if (!string.IsNullOrEmpty(response.ResolvedLineText))
                {
                    line.Append(": `").Append(response.ResolvedLineText).Append('`');
                }

                line.Append(')');
            }

            if (!string.IsNullOrEmpty(response.Warning))
            {
                line.Append(" — ").Append(response.Warning);
            }

            return line.ToString();
        }

        private static string DescribeFailure(PausePointPersistedRecord record, PausePointResponse response)
        {
            StringBuilder line = new();
            line.Append("Could not re-arm pause point '").Append(record.RegistryId)
                .Append("' after the domain reload: ");
            if (!string.IsNullOrEmpty(response.ErrorCode))
            {
                line.Append('[').Append(response.ErrorCode).Append("] ");
            }

            line.Append(DescribeFailureReason(response));
            return line.ToString();
        }

        // The shared Release message is written for a hand-issued enable-pause-point, where the
        // CLI has already tried the automatic Debug switch. A re-arm never switches Code
        // Optimization, so repeating that message here would describe a step that did not run.
        private static string DescribeFailureReason(PausePointResponse response)
        {
            if (response.ErrorCode == SourcePausePointConstants.ErrorCodeReleaseCodeOptimization)
            {
                return "Code Optimization is Release and the re-arm does not switch it. Run "
                    + "uloop set-code-optimization debug, compile, then enable-pause-point again "
                    + "with --persist.";
            }

            return response.Message;
        }
    }
}
