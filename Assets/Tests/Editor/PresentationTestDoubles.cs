using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Runs presentation background work on the calling thread so tests leave no thread-pool work behind.
    /// </summary>
    internal sealed class InlineBackgroundWorkRunner : IBackgroundWorkRunner
    {
        internal int RunCount { get; private set; }

        public Task<T> RunAsync<T>(Func<T> work)
        {
            RunCount++;
            return Task.FromResult(work());
        }

        public Task<T> RunTaskAsync<T>(Func<Task<T>> work)
        {
            RunCount++;
            return work();
        }
    }

    /// <summary>
    /// Records presentation dialogs instead of opening modal editor windows.
    /// </summary>
    internal sealed class RecordingPresentationDialogs : IPresentationDialogs
    {
        internal List<string> MessageTitles { get; } = new List<string>();
        internal List<string> Messages { get; } = new List<string>();
        internal List<string> ConfirmTitles { get; } = new List<string>();
        internal bool ConfirmResult { get; set; } = true;
        internal int SkillsInstalledCount { get; private set; }
        internal bool CliUninstallConfirmResult { get; set; } = true;
        internal int CliUninstallConfirmCount { get; private set; }
        internal CliPathSetupFlowResult CliPathSetupResult { get; set; }
        internal int CliPathSetupCount { get; private set; }

        public void ShowMessage(string title, string message)
        {
            MessageTitles.Add(title);
            Messages.Add(message);
        }

        public bool Confirm(string title, string message, string ok, string cancel)
        {
            ConfirmTitles.Add(title);
            return ConfirmResult;
        }

        public void ShowSkillsInstalled()
        {
            SkillsInstalledCount++;
        }

        public bool ConfirmCliUninstall()
        {
            CliUninstallConfirmCount++;
            return CliUninstallConfirmResult;
        }

        public Task<CliPathSetupFlowResult> EnsureCliVisibleAndShowResultAsync(
            RuntimePlatform platform,
            CliSetupApplicationService cliSetupApplicationService,
            CancellationToken ct)
        {
            CliPathSetupCount++;
            return Task.FromResult(CliPathSetupResult);
        }
    }
}
