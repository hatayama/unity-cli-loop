using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for the compile-fallback decision on the tool's apply path:
    /// a real reload that leaves an edit unapplied, entered through the tool the CLI calls rather
    /// than through the orchestrator, so the decision reaches the response the caller reads.
    /// </summary>
    public class HotReloadCompileFallbackE2ETests
    {
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";

        private const string CallerProjectRelativePath =
            "Assets/Tests/Editor/HotReload/" + CallerFileName;

        private const string CallerCallBodyAnchor = "return host.Value();";

        // A body the shim cannot compile, so its method is reported unapplied.
        private const string BrokenCallBody =
            "int broken = \"not an int\";\n            return broken;";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a real apply run that leaves an edit unapplied asks the CLI for a compile,
        /// because the Editor is in Edit Mode and the option defaults to auto.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_EditThatStaysUnapplied_RequestsTheCompile()
        {
            HotReloadResponse response = await RunBrokenCallerAsync(new JObject
            {
                ["Files"] = new JArray(CallerProjectRelativePath)
            });

            AssertLeftAnEditUnapplied(response);
            Assert.That(response.CompileFallback, Is.EqualTo("Requested"));
        }

        /// <summary>
        /// What: the same run with --compile-on-skip off reports the fallback as turned off, so
        /// the CLI runs no compile the caller did not ask for.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_EditThatStaysUnapplied_WithFallbackOff_ReportsDisabled()
        {
            HotReloadResponse response = await RunBrokenCallerAsync(new JObject
            {
                ["Files"] = new JArray(CallerProjectRelativePath),
                ["CompileOnSkip"] = "off"
            });

            AssertLeftAnEditUnapplied(response);
            Assert.That(response.CompileFallback, Is.EqualTo("Disabled"));
        }

        // Runs the tool over a fixture broken on disk for the length of the run, so the reload
        // fails for real instead of being handed a substituted source the tool never reads.
        private static async Task<HotReloadResponse> RunBrokenCallerAsync(JObject parameters)
        {
            using (BreakCallerFixture())
            {
                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse =
                    await tool.ExecuteAsync(parameters, CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;
                Assert.That(response, Is.Not.Null);
                return response;
            }
        }

        private static void AssertLeftAnEditUnapplied(HotReloadResponse response)
        {
            Assert.That(
                response.Methods,
                Has.Some.Matches<HotReloadMethodResult>(
                    method => method.Kind == HotReloadMethodOutcomeKind.Failed.ToString()
                        || method.Kind == HotReloadMethodOutcomeKind.Skipped.ToString()),
                "Precondition: the run must leave an edit unapplied.");
        }

        private static IDisposable BreakCallerFixture()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", CallerFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            string original = File.ReadAllText(path);
            Assert.That(
                original,
                Does.Contain(CallerCallBodyAnchor),
                "Precondition: anchor must exist: " + CallerCallBodyAnchor);

            // Why the lock: the fixture is a compiled source of this project, and a reload of the
            // assemblies triggered by the edit would tear the test down mid-run.
            EditorApplication.LockReloadAssemblies();
            File.WriteAllText(
                path,
                original.Replace(CallerCallBodyAnchor, BrokenCallBody, StringComparison.Ordinal));
            return new FixtureRestoreScope(path, original);
        }

        private sealed class FixtureRestoreScope : IDisposable
        {
            private readonly string _path;
            private readonly string _original;
            private bool _disposed;

            internal FixtureRestoreScope(string path, string original)
            {
                Debug.Assert(!string.IsNullOrEmpty(path), "path must not be empty.");
                Debug.Assert(original != null, "original must not be null.");
                _path = path;
                _original = original;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                File.WriteAllText(_path, _original);
                EditorApplication.UnlockReloadAssemblies();
            }
        }
    }
}
