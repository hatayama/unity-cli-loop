using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the Cecil search directories for publicizing a compilation assembly's references
    /// reach the precompiled DLLs that only a transitively referenced assembly lists, and keep the
    /// directories of the assembly's own references first.
    /// </summary>
    public sealed class HotReloadResolverSearchDirectoriesTests
    {
        private const string CodeAnalysisPluginPath =
            "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis/"
            + "UnityCliLoop.System.Collections.Immutable.dll";

        private readonly List<InternalsExposureTestImage> _images = new List<InternalsExposureTestImage>();
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (InternalsExposureTestImage image in _images)
            {
                foreach (string copyPath in PublicizedCopiesOf(image))
                {
                    File.Delete(copyPath);
                }

                image.Dispose();
            }

            _images.Clear();
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Verifies the directories reach a plugin two assembly references away, which neither the
        /// root's own references nor those of its direct reference list.
        /// </summary>
        [Test]
        public void Collect_IncludesDirectoriesOfTransitivelyReferencedPrecompiledAssemblies()
        {
            string pluginPath = CreateEmptyFile("plugins", "Plugin.dll");
            ReferenceGraph graph = CreateReferenceGraph(pluginPath, Array.Empty<string>());

            IReadOnlyCollection<string> directories = HotReloadResolverSearchDirectories.Collect(_tempRoot, graph.Tests);

            Assert.That(directories, Does.Contain(FullDirectoryOf(graph.Tests.outputPath)));
            Assert.That(directories, Does.Contain(FullDirectoryOf(pluginPath)));
            Assert.That(
                ReferencePublicizer.CollectResolverSearchDirectories(graph.Tests.allReferences),
                Does.Not.Contain(FullDirectoryOf(pluginPath)),
                "The root's own references must not list the plugin, or the graph does not test the transitive walk.");
            Assert.That(
                ReferencePublicizer.CollectResolverSearchDirectories(graph.Game.allReferences),
                Does.Not.Contain(FullDirectoryOf(pluginPath)),
                "The direct reference must not list the plugin, or one step of the walk would be enough.");
        }

        /// <summary>
        /// Verifies the directories of the root's own references come before those that only a
        /// transitive reference adds, so a same-named DLL resolves to the root's own copy first.
        /// </summary>
        [Test]
        public void Collect_OrdersOwnReferencesBeforeTransitiveOnes()
        {
            string pluginPath = CreateEmptyFile("plugins", "Plugin.dll");
            string ownPath = CreateEmptyFile("own", "Own.dll");
            ReferenceGraph graph = CreateReferenceGraph(pluginPath, new[] { ownPath });

            List<string> directories = new List<string>(HotReloadResolverSearchDirectories.Collect(_tempRoot, graph.Tests));

            int ownIndex = directories.IndexOf(FullDirectoryOf(ownPath));
            int pluginIndex = directories.IndexOf(FullDirectoryOf(pluginPath));
            Assert.That(ownIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(pluginIndex, Is.GreaterThan(ownIndex));
        }

        /// <summary>
        /// Verifies that for this project's hot-reload test assembly, whose asmdef overrides its
        /// references, the directories reach the code analysis plugins that only a referenced tool
        /// assembly lists.
        /// </summary>
        [Test]
        public void Collect_ForHotReloadTestAssembly_ReachesTheCodeAnalysisPluginDirectoryOnlyTransitively()
        {
            UnityCompilationAssembly testAssembly = PublicizerTestSearchDirectories.HotReloadTestAssembly();
            string pluginDirectory = Path.GetFullPath(Path.GetDirectoryName(CodeAnalysisPluginPath));
            Assert.That(
                ReferencePublicizer.CollectResolverSearchDirectories(testAssembly.allReferences),
                Does.Not.Contain(pluginDirectory),
                "Unity listed the plugin among the test assembly's own references, so the transitive walk is not what reaches it.");

            Assert.That(HotReloadResolverSearchDirectories.Collect(Directory.GetCurrentDirectory(), testAssembly), Does.Contain(pluginDirectory));
        }

        /// <summary>
        /// Verifies a publicized copy whose metadata needs an assembly that only a transitive
        /// reference lists cannot be written with the root's own reference directories, and can be
        /// written with the transitive ones.
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_ResolvesAnAssemblyThatOnlyATransitiveReferenceLists()
        {
            string externalName = "TransitiveEnumFixture_" + Guid.NewGuid().ToString("N");
            string externalDirectory = Path.Combine(_tempRoot, "plugins");
            InternalsExposureTestImage image =
                InternalsExposureTestImage.CreateWithConstantOfEnumIn(externalDirectory, externalName);
            _images.Add(image);
            ReferenceGraph graph = CreateReferenceGraph(
                Path.Combine(externalDirectory, externalName + ".dll"),
                Array.Empty<string>());

            // Why the failing call goes first: a failed write caches nothing, while a successful one
            // would satisfy the second call from the cache before Cecil resolves anything.
            Assert.Throws<AssemblyResolutionException>(() => ReferencePublicizer.GetOrCreatePublicizedCopy(
                image.Home,
                ReferencePublicizer.CollectResolverSearchDirectories(graph.Tests.allReferences)));

            string publicized = ReferencePublicizer.GetOrCreatePublicizedCopy(
                image.Home,
                HotReloadResolverSearchDirectories.Collect(_tempRoot, graph.Tests));

            Assert.That(File.Exists(publicized), Is.True);
        }

        /// <summary>
        /// Verifies the shim reference build publicizes its target with the transitive search
        /// directories, so a target whose metadata needs an assembly that only a transitive reference
        /// lists yields references instead of a publicize failure.
        /// </summary>
        [Test]
        public void TryBuildShimReferencePaths_PublicizesTheTargetWithTransitiveSearchDirectories()
        {
            string externalName = "TransitiveShimEnumFixture_" + Guid.NewGuid().ToString("N");
            string externalDirectory = Path.Combine(_tempRoot, "plugins");
            InternalsExposureTestImage image =
                InternalsExposureTestImage.CreateWithConstantOfEnumIn(externalDirectory, externalName);
            _images.Add(image);
            ReferenceGraph graph = CreateReferenceGraph(
                Path.Combine(externalDirectory, externalName + ".dll"),
                Array.Empty<string>());

            HotReloadShimReferenceBuilder.ShimReferencePathsResult result =
                HotReloadShimReferenceBuilder.TryBuildShimReferencePaths(
                    graph.Tests,
                    image.Home,
                    false,
                    false,
                    Array.Empty<HotReloadTypeHome>());

            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(File.Exists(result.References[0]), Is.True);
        }

        /// <summary>
        /// Verifies a reference Unity lists relative to a Virtual Player's root, as it does for the
        /// main project's script assemblies, is resolved against that root and not the current
        /// directory, so its directory becomes a search directory.
        /// </summary>
        [Test]
        public void Collect_ResolvesARelativeReferenceAgainstTheProjectRoot()
        {
            const string relativeReference = "../../ScriptAssemblies/Fixture.Other.dll";
            string scriptAssembliesPath = CreateEmptyFile(Path.Combine("Library", "ScriptAssemblies"), "Fixture.Other.dll");
            string playerRoot = Path.Combine(_tempRoot, "Library", "VP", "mppm1");
            Directory.CreateDirectory(playerRoot);
            Assert.That(
                File.Exists(relativeReference),
                Is.False,
                "The reference must not exist relative to the current directory, or the test cannot tell the two apart.");
            UnityCompilationAssembly root = new UnityCompilationAssembly(
                "Tests",
                ScriptPath("Tests.dll"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<UnityCompilationAssembly>(),
                new[] { relativeReference },
                AssemblyFlags.EditorAssembly);

            IReadOnlyCollection<string> directories = HotReloadResolverSearchDirectories.Collect(playerRoot, root);

            Assert.That(directories, Does.Contain(FullDirectoryOf(scriptAssembliesPath)));
        }

        // Tests -> Game -> Core -> plugin: two steps, so a walk that adds only the direct
        // reference's own references still misses the plugin.
        private ReferenceGraph CreateReferenceGraph(string pluginPath, string[] testsOwnReferences)
        {
            UnityCompilationAssembly core = new UnityCompilationAssembly(
                "Core",
                ScriptPath("Core.dll"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<UnityCompilationAssembly>(),
                new[] { pluginPath },
                AssemblyFlags.None);
            UnityCompilationAssembly game = new UnityCompilationAssembly(
                "Game",
                ScriptPath("Game.dll"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { core },
                Array.Empty<string>(),
                AssemblyFlags.None);
            UnityCompilationAssembly tests = new UnityCompilationAssembly(
                "Tests",
                ScriptPath("Tests.dll"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { game },
                testsOwnReferences,
                AssemblyFlags.EditorAssembly);
            return new ReferenceGraph(game, tests);
        }

        // The script assemblies exist as files so their directory counts as a search directory.
        private string ScriptPath(string fileName)
        {
            return CreateEmptyFile("scripts", fileName);
        }

        private string CreateEmptyFile(string directoryName, string fileName)
        {
            string directory = Path.Combine(_tempRoot, directoryName);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }

        private static string FullDirectoryOf(string path)
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }

        private static string[] PublicizedCopiesOf(InternalsExposureTestImage image)
        {
            string directory = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.PublicizedRefsRelativeDirectory);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, image.Definition.Name.Name + "-*")
                : Array.Empty<string>();
        }

        private sealed class ReferenceGraph
        {
            internal ReferenceGraph(UnityCompilationAssembly game, UnityCompilationAssembly tests)
            {
                Game = game;
                Tests = tests;
            }

            internal UnityCompilationAssembly Game { get; }

            internal UnityCompilationAssembly Tests { get; }
        }
    }
}
