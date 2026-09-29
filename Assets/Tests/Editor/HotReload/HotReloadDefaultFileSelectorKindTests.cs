using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers whether a resolved selection says it was chosen from compile snapshots, which is
    /// what lets a run leave out a file the caller never named.
    /// </summary>
    public sealed class HotReloadDefaultFileSelectorKindTests
    {
        private const string EnumPath = "Assets/Scripts/Kind.cs";
        private const string CallerPath = "Assets/Scripts/Caller.cs";

        [SetUp]
        public void SetUp()
        {
            // The production tool captures the package roots before it normalizes paths; a direct
            // call to the real normalizer has to do the same.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
        }

        /// <summary>
        /// What: files the caller passed are not a default selection.
        /// </summary>
        [Test]
        public void Resolve_WhenFilesArePassed_IsNotADefaultSelection()
        {
            HotReloadDefaultFileSelection selection = HotReloadDefaultFileSelector.Resolve(
                new[] { EnumPath, CallerPath },
                () => throw new AssertionException("Explicit --files must not scan for changed files."),
                Array.Empty<string>(),
                ToProjectRelativePath);

            Assert.That(selection.IsDefaultSelection, Is.False);
        }

        /// <summary>
        /// What: the changed files chosen when the files parameter is omitted are a default selection.
        /// </summary>
        [Test]
        public void Resolve_WhenFilesAreOmitted_IsADefaultSelection()
        {
            HotReloadDefaultFileSelection selection = HotReloadDefaultFileSelector.Resolve(
                Array.Empty<string>(),
                () => new HotReloadChangedFileAggregationResult(
                    true,
                    new List<string> { EnumPath, CallerPath },
                    new List<string>()),
                Array.Empty<string>(),
                ToProjectRelativePath);

            Assert.That(selection.Files, Is.EqualTo(new[] { EnumPath, CallerPath }));
            Assert.That(selection.IsDefaultSelection, Is.True);
        }

        private static string ToProjectRelativePath(string path)
        {
            return HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                HotReloadCompositionRoot.Services.PackageRootCapture,
                path);
        }
    }
}
