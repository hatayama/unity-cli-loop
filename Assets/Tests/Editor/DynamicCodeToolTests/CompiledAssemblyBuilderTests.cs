using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class CompiledAssemblyBuilderTests
    {
        [Test]
        public void CreateUniqueCompilationName_WhenClassNameContainsPathCharacters_ShouldSanitizeFileName()
        {
            string compilationName = CompiledAssemblyBuilder.CreateUniqueCompilationName(
                "Bad/Name:With\\Separators",
                42);

            Assert.That(compilationName, Does.EndWith("_42"));
            Assert.That(compilationName, Does.Not.Contain("/"));
            Assert.That(compilationName, Does.Not.Contain("\\"));
            Assert.That(compilationName, Does.Not.Contain(":"));
        }

        [Test]
        public void AwaitBuildCompletionAsync_WhenCancellationIsRequested_ShouldCancelAfterBuildCompletion()
        {
            TaskCompletionSource<CompilerMessage[]> buildTaskCompletionSource =
                new TaskCompletionSource<CompilerMessage[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            Task<CompilerMessage[]> waitTask = AssemblyBuilderFallbackCompilerBackend.AwaitBuildCompletionAsync(
                buildTaskCompletionSource.Task,
                cancellationTokenSource.Token);

            cancellationTokenSource.Cancel();

            Assert.That(waitTask.IsCompleted, Is.False);

            buildTaskCompletionSource.SetResult(System.Array.Empty<CompilerMessage>());

            Assert.ThrowsAsync<TaskCanceledException>(async () => await waitTask);
        }

        [Test]
        public async Task RegisterBuildFinishedContinuation_WhenCancellationWins_ShouldWaitForActualBuildCompletion()
        {
            TaskCompletionSource<CompilerMessage[]> buildTaskCompletionSource =
                new TaskCompletionSource<CompilerMessage[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            bool buildFinished = false;

            Task continuationTask = AssemblyBuilderFallbackCompilerBackend.RegisterBuildFinishedContinuation(
                buildTaskCompletionSource.Task,
                () => buildFinished = true);
            Task<CompilerMessage[]> waitTask = AssemblyBuilderFallbackCompilerBackend.AwaitBuildCompletionAsync(
                buildTaskCompletionSource.Task,
                cancellationTokenSource.Token);

            cancellationTokenSource.Cancel();

            Assert.That(buildFinished, Is.False);

            buildTaskCompletionSource.SetResult(System.Array.Empty<CompilerMessage>());
            Assert.That(async () => await waitTask, Throws.InstanceOf<TaskCanceledException>());
            await continuationTask;

            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void SupportsAutoPrewarm_WhenExternalCompilerIsAvailableOnWindows_ShouldReturnTrue()
        {
            Assert.That(
                CompiledAssemblyBuilder.SupportsAutoPrewarm(
                    new ExternalCompilerPaths(
                        "Editor",
                        "Editor",
                        "dotnet",
                        "csc.dll",
                        "csc.runtimeconfig.json",
                        "csc.deps.json",
                        "Microsoft.CodeAnalysis.dll",
                        "Microsoft.CodeAnalysis.CSharp.dll",
                        "runtime"),
                    RuntimePlatform.WindowsEditor),
                Is.True);
        }
    }
}
