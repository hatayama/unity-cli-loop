using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The stages one group run calls out to, so a test can observe the production pipeline's
    /// order instead of reimplementing it.
    /// </summary>
    internal sealed class HotReloadGroupProcessorDependencies
    {
        private static HotReloadGroupProcessorDependencies current = CreateProduction();

        private HotReloadGroupProcessorDependencies(
            Func<IReadOnlyList<HotReloadGroupFile>, bool> validateNewSourceMembership)
        {
            ValidateNewSourceMembership = validateNewSourceMembership
                ?? throw new ArgumentNullException(nameof(validateNewSourceMembership));
        }

        internal static HotReloadGroupProcessorDependencies Current => current;

        /// <summary>
        /// Confirms the group's files still belong to the assembly they were resolved against.
        /// </summary>
        internal Func<IReadOnlyList<HotReloadGroupFile>, bool> ValidateNewSourceMembership { get; }

        internal static HotReloadGroupProcessorDependencies CreateProduction()
        {
            return new HotReloadGroupProcessorDependencies(HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure);
        }

        internal static HotReloadGroupProcessorDependencies Create(
            Func<IReadOnlyList<HotReloadGroupFile>, bool> validateNewSourceMembership)
        {
            return new HotReloadGroupProcessorDependencies(validateNewSourceMembership);
        }

        /// <summary>
        /// Installs a replacement for the duration of the returned scope and puts the previous
        /// one back when it closes.
        /// </summary>
        internal static IDisposable BeginReplacement(HotReloadGroupProcessorDependencies replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            HotReloadGroupProcessorDependencies previous = current;
            current = replacement;
            return new ReplacementScope(previous);
        }

        private sealed class ReplacementScope : IDisposable
        {
            private readonly HotReloadGroupProcessorDependencies previous;
            private bool restored;

            public ReplacementScope(HotReloadGroupProcessorDependencies previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                current = previous;
            }
        }
    }
}
