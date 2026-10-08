using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the import callback that drops the compilation assembly list only when an
    /// import changed the script set without a domain reload.
    /// </summary>
    public sealed class HotReloadCompilationAssemblyListPostprocessorTests
    {
        private static readonly string[] Empty = Array.Empty<string>();

        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Verifies that the callback has the five-parameter static shape Unity discovers, and no
        /// four-parameter form that Unity would call instead.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_IsTheFiveParameterStaticFormUnityPrefers()
        {
            Type postprocessor = typeof(HotReloadCompilationAssemblyListPostprocessor);

            Assert.That(postprocessor.IsSubclassOf(typeof(AssetPostprocessor)), Is.True);
            Assert.That(
                postprocessor.GetMethod(
                    "OnPostprocessAllAssets",
                    AnyStatic,
                    null,
                    new[] { typeof(string[]), typeof(string[]), typeof(string[]), typeof(string[]), typeof(bool) },
                    null),
                Is.Not.Null);
            Assert.That(
                postprocessor.GetMethod(
                    "OnPostprocessAllAssets",
                    AnyStatic,
                    null,
                    new[] { typeof(string[]), typeof(string[]), typeof(string[]), typeof(string[]) },
                    null),
                Is.Null);
        }

        /// <summary>
        /// Verifies that importing a source, assembly definition, assembly reference or precompiled
        /// assembly without a domain reload drops the kept list, whatever the extension's case.
        /// </summary>
        [TestCase("Assets/Scripts/New.cs")]
        [TestCase("Assets/Scripts/New.asmdef")]
        [TestCase("Assets/Scripts/New.asmref")]
        [TestCase("Assets/Plugins/Lib.dll")]
        [TestCase("Assets/Scripts/Upper.CS")]
        public void OnPostprocessAllAssets_AssemblyListAssetImportedWithoutReload_DropsTheMemo(string assetPath)
        {
            AssertBatchDropsTheMemo(new[] { assetPath }, Empty, Empty, Empty);
        }

        /// <summary>
        /// Verifies that a source imported after an asset that does not change the assembly list, in the
        /// same batch without a domain reload, still drops the kept list.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_AssemblyListAssetAfterOtherAssetWithoutReload_DropsTheMemo()
        {
            AssertBatchDropsTheMemo(
                new[] { "Assets/Textures/A.png", "Assets/Scripts/New.cs" },
                Empty,
                Empty,
                Empty);
        }

        /// <summary>
        /// Verifies that deleting a source without a domain reload drops the kept list.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_AssemblyListAssetDeletedWithoutReload_DropsTheMemo()
        {
            AssertBatchDropsTheMemo(Empty, new[] { "Assets/Scripts/Gone.cs" }, Empty, Empty);
        }

        /// <summary>
        /// Verifies that a source that appears as a move destination without a domain reload drops the
        /// kept list.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_AssemblyListAssetMovedWithoutReload_DropsTheMemo()
        {
            AssertBatchDropsTheMemo(Empty, Empty, new[] { "Assets/Scripts/Gone.cs" }, Empty);
        }

        /// <summary>
        /// Verifies that a source that appears as a move origin without a domain reload drops the kept
        /// list.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_AssemblyListAssetMovedFromWithoutReload_DropsTheMemo()
        {
            AssertBatchDropsTheMemo(Empty, Empty, Empty, new[] { "Assets/Scripts/Gone.cs" });
        }

        /// <summary>
        /// Verifies that the batch of the import whose compile reloaded the domain keeps the list the new
        /// domain already filled.
        /// </summary>
        [Test]
        public void OnPostprocessAllAssets_AssemblyListAssetImportedWithReload_KeepsTheMemo()
        {
            IReadOnlyList<UnityCompilationAssembly> first = HotReloadCompilationAssemblies.Current();

            HotReloadCompilationAssemblyListPostprocessor.OnPostprocessAllAssets(
                new[] { "Assets/Scripts/New.cs" },
                Empty,
                Empty,
                Empty,
                true);
            IReadOnlyList<UnityCompilationAssembly> second = HotReloadCompilationAssemblies.Current();

            Assert.That(second, Is.SameAs(first));
        }

        /// <summary>
        /// Verifies that importing assets that do not change the assembly list, or an empty batch, keeps
        /// the kept list.
        /// </summary>
        [TestCase("Assets/Textures/A.png")]
        [TestCase("Assets/Prefabs/A.prefab")]
        [TestCase("Assets/Data/a.json")]
        [TestCase("Assets/Notes/Readme.cs.txt")]
        [TestCase("")]
        public void OnPostprocessAllAssets_OtherAssetsImportedWithoutReload_KeepsTheMemo(string assetPath)
        {
            string[] imported = assetPath.Length == 0 ? Empty : new[] { assetPath };
            IReadOnlyList<UnityCompilationAssembly> first = HotReloadCompilationAssemblies.Current();

            HotReloadCompilationAssemblyListPostprocessor.OnPostprocessAllAssets(imported, Empty, Empty, Empty, false);
            IReadOnlyList<UnityCompilationAssembly> second = HotReloadCompilationAssemblies.Current();

            Assert.That(second, Is.SameAs(first));
        }

        private static void AssertBatchDropsTheMemo(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            IReadOnlyList<UnityCompilationAssembly> first = HotReloadCompilationAssemblies.Current();

            HotReloadCompilationAssemblyListPostprocessor.OnPostprocessAllAssets(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths,
                false);
            IReadOnlyList<UnityCompilationAssembly> second = HotReloadCompilationAssemblies.Current();

            Assert.That(second, Is.Not.Empty);
            Assert.That(second, Is.Not.SameAs(first));
        }
    }
}
