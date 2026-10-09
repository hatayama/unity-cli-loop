using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadTransformWorkerWarmUpItem"/>: which requests it
    /// sends for which target, with which token, and what it does when the prepare does not
    /// complete or the warm-up is stopped. The requests go to a fake that records them;
    /// <see cref="HotReloadWarmUpE2ETests"/> sends them to the real worker.
    /// Not covered: a target Unity lists with no source files. Unity lists no compilation assembly
    /// without sources, and the item looks the assembly up in Unity's own list, so no test can
    /// make one.
    /// </summary>
    public class HotReloadTransformWorkerWarmUpItemTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private const string MissingAssemblyName = "Uloop.NoSuchAssembly";

        private const string ThisSourceProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadTransformWorkerWarmUpItemTests.cs";

        private const string MissingSourceProjectRelativePath = "Assets/Tests/Editor/HotReload/Uloop.NoSuchSource.cs";

        private string _temporaryProjectRoot;

        [TearDown]
        public void DeleteTemporaryProjectRoot()
        {
            if (_temporaryProjectRoot != null && Directory.Exists(_temporaryProjectRoot))
            {
                Directory.Delete(_temporaryProjectRoot, true);
            }

            _temporaryProjectRoot = null;
        }

        /// <summary>
        /// What: a ledger name Unity lists no compilation assembly for gives no request, and the
        /// item returns normally.
        /// </summary>
        [Test]
        public async Task RunAsync_WhenUnityListsNoTarget_SendsNothing()
        {
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(ProjectRoot(), MissingAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs, Is.Empty, "requests");
        }

        /// <summary>
        /// What: a target Unity does not list is passed over, and the requests are built for the
        /// next target, which Unity lists.
        /// </summary>
        [Test]
        public async Task RunAsync_SkipsATargetUnityDoesNotList()
        {
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(ProjectRoot(), MissingAssemblyName, TestAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs.Count, Is.EqualTo(2), "requests");
            Assert.That(sender.Inputs[0].targetAssemblyName, Is.EqualTo(TestAssemblyName), "prepare target");
            Assert.That(sender.Inputs[1].targetAssemblyName, Is.EqualTo(TestAssemblyName), "transform target");
        }

        /// <summary>
        /// What: the item sends a prepare request and then a transform request, and neither
        /// carries the item's token, because cancelling a request in flight kills the worker the
        /// item warms.
        /// </summary>
        [Test]
        public async Task RunAsync_SendsPrepareThenTransformWithATokenThatCannotBeCancelled()
        {
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());
            // Why a live token rather than None: None cannot be cancelled either, so an item that
            // passed its own token on would look the same.
            using CancellationTokenSource cts = new CancellationTokenSource();

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(ProjectRoot(), TestAssemblyName), cts.Token);

            Assert.That(sender.Inputs.Count, Is.EqualTo(2), "requests");
            Assert.That(sender.Inputs[0].operation, Is.EqualTo(HotReloadConstants.PrepareIntroducedTypesOperation), "first operation");
            Assert.That(sender.Inputs[1].operation, Is.Null, "second operation");
            Assert.That(sender.Tokens[0].CanBeCanceled, Is.False, "the prepare request can be cancelled");
            Assert.That(sender.Tokens[1].CanBeCanceled, Is.False, "the transform request can be cancelled");
        }

        /// <summary>
        /// What: the prepare request carries the target's material the way a run builds it (the
        /// first source, the defines, the worker reference paths, the dll path, the assembly name
        /// and MVID, and every source path), and the transform request carries the same material
        /// with no operation.
        /// </summary>
        [Test]
        public async Task RunAsync_BuildsThePrepareInputFromTheTargetAsTheRunDoes()
        {
            string root = ProjectRoot();
            string dllPath = TestAssemblyDllPath();
            UnityCompilationAssembly assembly = HotReloadCompilationAssemblies.FindByName(TestAssemblyName);
            Assert.That(assembly, Is.Not.Null, "Precondition: Unity must list the test assembly.");
            string[] sourcePaths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths(root, assembly.sourceFiles);
            Assert.That(File.Exists(sourcePaths[0]), Is.True, "Precondition: the first source must be on disk.");
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(root, TestAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs.Count, Is.EqualTo(2), "requests");
            TransformWorkerInputDto prepare = sender.Inputs[0];
            Assert.That(prepare.operation, Is.EqualTo(HotReloadConstants.PrepareIntroducedTypesOperation), "operation");
            Assert.That(prepare.sources.Length, Is.EqualTo(1), "sources");
            Assert.That(prepare.sources[0].sourcePath, Is.EqualTo(sourcePaths[0]), "source path");
            Assert.That(prepare.sources[0].projectRelativePath, Is.EqualTo(assembly.sourceFiles[0]), "project-relative path");
            Assert.That(prepare.defines, Is.EqualTo(assembly.defines), "defines");
            Assert.That(
                prepare.referencePaths,
                Is.EqualTo(HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(
                    root,
                    assembly,
                    HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, dllPath))),
                "reference paths");
            Assert.That(prepare.targetTypesAssemblyPath, Is.EqualTo(Path.GetFullPath(dllPath)), "dll path");
            Assert.That(prepare.targetAssemblyName, Is.EqualTo(TestAssemblyName), "assembly name");
            Assert.That(prepare.targetAssemblyMvid, Is.EqualTo(HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath)), "MVID");
            Assert.That(prepare.assemblySourcePaths, Is.EqualTo(sourcePaths), "assembly source paths");

            TransformWorkerInputDto transform = sender.Inputs[1];
            Assert.That(transform.operation, Is.Null, "transform operation");
            Assert.That(transform.sources.Length, Is.EqualTo(1), "transform sources");
            Assert.That(transform.sources[0].sourcePath, Is.EqualTo(prepare.sources[0].sourcePath), "transform source path");
            Assert.That(
                transform.sources[0].projectRelativePath,
                Is.EqualTo(prepare.sources[0].projectRelativePath),
                "transform project-relative path");
            Assert.That(transform.defines, Is.EqualTo(prepare.defines), "transform defines");
            Assert.That(transform.referencePaths, Is.EqualTo(prepare.referencePaths), "transform reference paths");
            Assert.That(transform.targetTypesAssemblyPath, Is.EqualTo(prepare.targetTypesAssemblyPath), "transform dll path");
            Assert.That(transform.targetAssemblyName, Is.EqualTo(prepare.targetAssemblyName), "transform assembly name");
            Assert.That(transform.targetAssemblyMvid, Is.EqualTo(prepare.targetAssemblyMvid), "transform MVID");
            Assert.That(transform.assemblySourcePaths, Is.EqualTo(prepare.assemblySourcePaths), "transform assembly source paths");
        }

        /// <summary>
        /// What: a warm-up stopped while the prepare request is in flight sends no transform
        /// request and throws, so the warm-up reports the item cancelled.
        /// </summary>
        [Test]
        public async Task RunAsync_CancelledDuringThePrepare_SendsNoTransform()
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            RecordingRequestSender sender = new RecordingRequestSender(_ =>
            {
                cts.Cancel();
                return Completed();
            });

            bool cancelled = await RunReportingCancellation(
                new HotReloadTransformWorkerWarmUpItem(sender.SendAsync),
                ContextFor(ProjectRoot(), TestAssemblyName),
                cts.Token);

            Assert.That(cancelled, Is.True, "the item threw OperationCanceledException");
            Assert.That(sender.Inputs.Count, Is.EqualTo(1), "requests");
        }

        /// <summary>
        /// What: a prepare request that does not complete is not followed by a transform request,
        /// and the item returns normally, so a worker that cannot start is not waited for twice.
        /// </summary>
        [TestCase(nameof(TransformWorkerHostResultKind.WorkerFailed))]
        [TestCase(nameof(TransformWorkerHostResultKind.BootstrapFailed))]
        [TestCase(nameof(TransformWorkerHostResultKind.TimedOut))]
        [TestCase(nameof(TransformWorkerHostResultKind.LifecycleClosed))]
        [TestCase(nameof(TransformWorkerHostResultKind.RetryExhausted))]
        public async Task RunAsync_WhenThePrepareDoesNotComplete_SendsNoTransform(string kindName)
        {
            TransformWorkerHostResultKind kind =
                (TransformWorkerHostResultKind)Enum.Parse(typeof(TransformWorkerHostResultKind), kindName);
            RecordingRequestSender sender = new RecordingRequestSender(_ => Failed(kind));

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(ProjectRoot(), TestAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs.Count, Is.EqualTo(1), "requests");
            Assert.That(sender.Inputs[0].operation, Is.EqualTo(HotReloadConstants.PrepareIntroducedTypesOperation), "operation");
        }

        /// <summary>
        /// What: a transform request that does not complete ends the item normally, without an
        /// exception.
        /// </summary>
        [Test]
        public async Task RunAsync_WhenTheTransformFails_Returns()
        {
            RecordingRequestSender sender = new RecordingRequestSender(
                index => index == 0 ? Completed() : Failed(TransformWorkerHostResultKind.WorkerFailed));

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(ProjectRoot(), TestAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs.Count, Is.EqualTo(2), "requests");
        }

        /// <summary>
        /// What: a target none of whose sources is on disk under the project root gives no
        /// request, and the item returns normally.
        /// </summary>
        [Test]
        public async Task RunAsync_WithNoSourceOnDisk_SendsNothing()
        {
            // Why an empty root: the source paths are the project-relative paths joined to the
            // root, so none of them is on disk under it.
            _temporaryProjectRoot = Path.Combine(
                Path.GetTempPath(),
                "UloopTransformWorkerWarmUpItemTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryProjectRoot);
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());

            await new HotReloadTransformWorkerWarmUpItem(sender.SendAsync)
                .RunAsync(ContextFor(_temporaryProjectRoot, TestAssemblyName), CancellationToken.None);

            Assert.That(sender.Inputs, Is.Empty, "requests");
        }

        /// <summary>
        /// What: the prepare request carries the first source that is on disk, passing over the
        /// ones before it that are not.
        /// </summary>
        [Test]
        public void BuildPrepareInput_UsesTheFirstSourceOnDisk()
        {
            string missing = AbsoluteSourcePath(MissingSourceProjectRelativePath);
            string present = AbsoluteSourcePath(ThisSourceProjectRelativePath);
            Assert.That(File.Exists(missing), Is.False, "Precondition: the first source must not exist.");
            Assert.That(File.Exists(present), Is.True, "Precondition: the second source must exist.");
            HotReloadTransformWorkerWarmUpRequest request = RequestFor(
                TestAssemblyDllPath(),
                new[] { MissingSourceProjectRelativePath, ThisSourceProjectRelativePath },
                new[] { missing, present });

            TransformWorkerInputDto input = request.BuildPrepareInput();

            Assert.That(input.sources.Length, Is.EqualTo(1), "sources");
            Assert.That(input.sources[0].sourcePath, Is.EqualTo(present), "source path");
            Assert.That(input.sources[0].projectRelativePath, Is.EqualTo(ThisSourceProjectRelativePath), "project-relative path");
        }

        /// <summary>
        /// What: a target dll that cannot be read throws an IO exception, which the warm-up
        /// reports as a failed item.
        /// </summary>
        [Test]
        public void BuildPrepareInput_WhenTheDllCannotBeRead_Throws()
        {
            string missingDll = CompiledAssemblyLayout.Resolve(ProjectRoot()).DllPath(MissingAssemblyName);
            Assert.That(File.Exists(missingDll), Is.False, "Precondition: the dll must not exist.");
            HotReloadTransformWorkerWarmUpRequest request = RequestFor(
                missingDll,
                new[] { ThisSourceProjectRelativePath },
                new[] { AbsoluteSourcePath(ThisSourceProjectRelativePath) });

            Assert.Throws<FileNotFoundException>(() => request.BuildPrepareInput());
        }

        /// <summary>
        /// What: a warm-up stopped before the item's first request sends nothing and throws, so
        /// the warm-up reports the item cancelled.
        /// </summary>
        [Test]
        public async Task RunAsync_WithACancelledToken_SendsNothingAndThrows()
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();
            RecordingRequestSender sender = new RecordingRequestSender(_ => Completed());

            bool cancelled = await RunReportingCancellation(
                new HotReloadTransformWorkerWarmUpItem(sender.SendAsync),
                ContextFor(ProjectRoot(), TestAssemblyName),
                cts.Token);

            Assert.That(cancelled, Is.True, "the item threw OperationCanceledException");
            Assert.That(sender.Inputs, Is.Empty, "requests");
        }

        // Why try/catch rather than Assert.ThrowsAsync: that blocks the main thread in this NUnit,
        // and the test framework reports an async test that ends canceled as passed.
        private static async Task<bool> RunReportingCancellation(
            HotReloadTransformWorkerWarmUpItem item,
            HotReloadWarmUpContext context,
            CancellationToken ct)
        {
            try
            {
                await item.RunAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                return true;
            }

            return false;
        }

        private static TransformWorkerHostResult Completed()
        {
            return TransformWorkerHostResult.Completed(new TransformWorkerOutputDto());
        }

        private static TransformWorkerHostResult Failed(TransformWorkerHostResultKind kind)
        {
            return TransformWorkerHostResult.Failure(kind, "Fake " + kind + " result.");
        }

        private static HotReloadTransformWorkerWarmUpRequest RequestFor(
            string dllPath,
            string[] sourceFiles,
            string[] assemblySourcePaths)
        {
            return new HotReloadTransformWorkerWarmUpRequest(
                TestAssemblyName,
                Path.GetFullPath(dllPath),
                sourceFiles,
                assemblySourcePaths,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string TestAssemblyDllPath()
        {
            return CompiledAssemblyLayout.Resolve(ProjectRoot()).DllPath(TestAssemblyName);
        }

        private static string AbsoluteSourcePath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot(), projectRelativePath));
        }

        // Every target points at the test assembly's dll and PDB, as the ledger's targets do once
        // the context source found both on disk; only the names differ.
        private static HotReloadWarmUpContext ContextFor(string projectRoot, params string[] assemblyNames)
        {
            string dllPath = TestAssemblyDllPath();
            List<HotReloadWarmUpTarget> targets = new List<HotReloadWarmUpTarget>();
            foreach (string assemblyName in assemblyNames)
            {
                targets.Add(new HotReloadWarmUpTarget(
                    assemblyName,
                    dllPath,
                    Path.ChangeExtension(dllPath, ".pdb"),
                    Array.Empty<string>()));
            }

            return new HotReloadWarmUpContext(projectRoot, targets);
        }

        // Records each request and its token, and answers it with what the test chose for its
        // index (0 = the first request, the prepare).
        private sealed class RecordingRequestSender
        {
            private readonly Func<int, TransformWorkerHostResult> _answer;

            internal RecordingRequestSender(Func<int, TransformWorkerHostResult> answer)
            {
                _answer = answer;
            }

            internal List<TransformWorkerInputDto> Inputs { get; } = new List<TransformWorkerInputDto>();

            internal List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

            internal Task<TransformWorkerHostResult> SendAsync(TransformWorkerInputDto input, CancellationToken ct)
            {
                Inputs.Add(input);
                Tokens.Add(ct);
                return Task.FromResult(_answer(Inputs.Count - 1));
            }
        }
    }
}
