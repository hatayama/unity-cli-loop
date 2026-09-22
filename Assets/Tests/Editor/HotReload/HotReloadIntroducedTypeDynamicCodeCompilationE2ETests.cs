using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a dynamic-code snippet that names a type an earlier reload
    /// introduced: the snippet compiles when the reload's retained artifact is among the
    /// compilation's references, and stops compiling when it is not.
    /// </summary>
    /// <remarks>
    /// Why compile-only: the dynamic-code guardrails forbid running a real compile-and-run flow
    /// from an EditMode test, and what this design has to prove is a reference the compiler either
    /// has or lacks, which the compilation result alone answers.
    /// </remarks>
    public class HotReloadIntroducedTypeDynamicCodeCompilationE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";
        private const string IntroducedTypeSimpleName = "HotReloadDynamicCodeIntroducedValue";
        private const string IntroducedTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload." + IntroducedTypeSimpleName;
        private const int IntroducedSeed = 11;
        private const string SnippetClassName = "DynamicCodeIntroducedTypeReferenceCommand";

        // The snippet names the introduced type by its full name, so the compiler has to find it
        // through a reference rather than through a using directive this test could have gotten
        // wrong.
        private const string Snippet =
            "return new " + IntroducedTypeFullName + "().Compute();";

        /// <summary>
        /// What: a snippet naming a type an earlier reload introduced compiles when the active
        /// artifact paths are among the additional references, and fails with the missing-type
        /// error when the same snippet is compiled without them - so the reference is what makes
        /// the type nameable, not anything else the domain happens to hold.
        /// </summary>
        [Test]
        public async Task CompileSnippetNamingAnIntroducedType_SucceedsOnlyWithTheArtifactReference()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateIntroducingEdits(hostPath, callerPath));
                Assert.That(
                    CountFailures(introducing),
                    Is.EqualTo(0),
                    "Precondition: introducing the type must not fail the reload.\n"
                    + DescribeOutcomes(introducing));
                Assert.That(
                    readArtifact(),
                    Is.Not.Null,
                    "Precondition: the reload must have retained an artifact.\n"
                    + DescribeOutcomes(introducing));

                List<string> references = ReadActiveArtifactReferences();
                Assert.That(
                    references,
                    Is.Not.Empty,
                    "Precondition: the active artifact must be offered as a reference.");

                using (DynamicCodeCompiler compiler = new DynamicCodeCompiler())
                {
                    CompilationResult withReference = await CompileAsync(compiler, references);
                    Assert.That(
                        withReference.Success,
                        Is.True,
                        "The snippet must compile against the retained artifact.\n"
                        + DescribeErrors(withReference));

                    CompilationResult withoutReference = await CompileAsync(compiler, new List<string>());
                    Assert.That(
                        withoutReference.Success,
                        Is.False,
                        "Without the artifact reference the introduced type must not be nameable.");
                    Assert.That(
                        DescribeErrors(withoutReference),
                        Does.Contain("CS0234").Or.Contain("CS0246"),
                        "The failure must be the compiler not finding the type.\n"
                        + DescribeErrors(withoutReference));
                }
            });
        }

        private static Task<CompilationResult> CompileAsync(
            DynamicCodeCompiler compiler,
            List<string> additionalReferences)
        {
            return compiler.CompileAsync(
                new CompilationRequest
                {
                    Code = Snippet,
                    ClassName = SnippetClassName,
                    Namespace = DynamicCodeConstants.DEFAULT_NAMESPACE,
                    AdditionalReferences = additionalReferences
                },
                CancellationToken.None);
        }

        private static List<string> ReadActiveArtifactReferences()
        {
            Func<IReadOnlyList<string>> describe =
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths;
            Assert.That(describe, Is.Not.Null, "The installed services must offer the reference paths.");
            return new List<string>(describe());
        }

        private static string DescribeErrors(CompilationResult result)
        {
            string description = "Errors:";
            foreach (CompilationError error in result.Errors)
            {
                description += "\n  " + error.ErrorCode + " " + error.Message;
            }

            return description;
        }

        private static Dictionary<string, string> CreateIntroducingEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "DynamicCodeIntroducedTypeHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "DynamicCodeIntroducedTypeCaller.cs",
                    File.ReadAllText(callerPath))
            };
        }

        private static string InsertIntroducedType(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + IntroducedSeed.ToString() + ";\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }
    }
}
