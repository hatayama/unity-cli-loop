using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Assembly Builder Fallback Compiler Backend behavior for Unity CLI Loop.
    /// </summary>
    internal static class AssemblyBuilderFallbackCompilerBackend
    {
        private static Func<
            string,
            string,
            List<string>,
            CancellationToken,
            Action,
            Action,
            Action,
            Task<DynamicCompilationBackendResult>> compilerOverride;

        public static async Task<DynamicCompilationBackendResult> CompileAsync(
            string sourcePath,
            string dllPath,
            List<string> references,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            if (compilerOverride != null)
            {
                return await compilerOverride(
                    sourcePath,
                    dllPath,
                    references,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount).ConfigureAwait(false);
            }

            TaskCompletionSource<CompilerMessage[]> taskCompletionSource = new();
            ct.ThrowIfCancellationRequested();
            incrementBuildCount();

            string[] referenceArray = references != null
                ? DynamicReferenceSetBuilder.MergeReferencesByAssemblyName(Array.Empty<string>(), references)
                : Array.Empty<string>();

#pragma warning disable 618 // Unity's fallback compiler API has no supported replacement on older editor versions.
            AssemblyBuilder builder = new(dllPath, sourcePath)
            {
                referencesOptions = ReferencesOptions.UseEngineModules,
                additionalReferences = referenceArray
            };
#pragma warning restore 618

            Action<string, CompilerMessage[]> onBuildFinished = (string assemblyPath, CompilerMessage[] compilerMessages) =>
            {
                taskCompletionSource.TrySetResult(compilerMessages);
            };
            builder.buildFinished += onBuildFinished;
            _ = RegisterBuildFinishedContinuation(taskCompletionSource.Task, markBuildFinished);

            bool started = builder.Build();
            if (!started)
            {
                builder.buildFinished -= onBuildFinished;
                return new DynamicCompilationBackendResult(
                    new CompilerMessage[]
                    {
                        new CompilerMessage
                        {
                            type = CompilerMessageType.Error,
                            message = "AssemblyBuilder.Build() failed to start compilation"
                        }
                    },
                    DynamicCompilationBackendKind.AssemblyBuilderFallback);
            }

            markBuildStarted();
            CompilerMessage[] messages;
            try
            {
                messages = await AwaitBuildCompletionAsync(taskCompletionSource.Task, ct).ConfigureAwait(false);
            }
            finally
            {
                // A cancelled wait leaves the build running; a late completion must not reach this call.
                builder.buildFinished -= onBuildFinished;
            }

            ct.ThrowIfCancellationRequested();
            return new DynamicCompilationBackendResult(
                messages,
                DynamicCompilationBackendKind.AssemblyBuilderFallback);
        }

        internal static Func<
            string,
            string,
            List<string>,
            CancellationToken,
            Action,
            Action,
            Action,
            Task<DynamicCompilationBackendResult>> SwapCompilerForTests(
            Func<
                string,
                string,
                List<string>,
                CancellationToken,
                Action,
                Action,
                Action,
                Task<DynamicCompilationBackendResult>> replacement)
        {
            Func<
                string,
                string,
                List<string>,
                CancellationToken,
                Action,
                Action,
                Action,
                Task<DynamicCompilationBackendResult>> previous = compilerOverride;
            compilerOverride = replacement;
            return previous;
        }

        internal static async Task<CompilerMessage[]> AwaitBuildCompletionAsync(
            Task<CompilerMessage[]> buildTask,
            CancellationToken ct)
        {
            TaskCompletionSource<CompilerMessage[]> cancellationTaskCompletionSource =
                new TaskCompletionSource<CompilerMessage[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            using CancellationTokenRegistration cancellationRegistration =
                ct.Register(
                    static state => ((TaskCompletionSource<CompilerMessage[]>)state).TrySetCanceled(),
                    cancellationTaskCompletionSource);

            Task completedTask = await Task.WhenAny(buildTask, cancellationTaskCompletionSource.Task).ConfigureAwait(false);
            // Unity reports the fallback build's end only while something polls its status, so waiting
            // for it after a cancellation can hold the execution slot forever.
            if (completedTask == cancellationTaskCompletionSource.Task)
            {
                return await cancellationTaskCompletionSource.Task.ConfigureAwait(false);
            }

            return await buildTask.ConfigureAwait(false);
        }

        internal static Task RegisterBuildFinishedContinuation(
            Task<CompilerMessage[]> buildTask,
            Action markBuildFinished)
        {
            return buildTask.ContinueWith(
                static (_, state) => ((Action)state)(),
                markBuildFinished,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
