using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// What end-to-end tests of introduced types that reach the internals of the test assembly
    /// share: a reload that introduces the types of the given owner sources while it points the
    /// compiled cross-file caller at an expression using them, and the questions asked of it.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public abstract class HotReloadIntroducedTypeCallerE2ETestBase : HotReloadIntroducedTypeE2ETestBase
    {
        private protected const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private protected const string CompilationFailurePrefix = "Introduced-type compilation failed: ";

        /// <summary>What the caller returns before any run edits its body.</summary>
        private protected const int HostValue = 1;

        private const string CallerBodyAnchor = "return host.Value();";

        private protected static Dictionary<string, string> Owner(string ownerPath, string declarations)
        {
            return new Dictionary<string, string> { [ownerPath] = TypeSource(declarations) };
        }

        private protected static string TypeSource(string declarations)
        {
            return "namespace " + Namespace + "\n{\n    " + declarations + "\n}\n";
        }

        // Only the files named in the map take part in the run, and the caller body is replaced by
        // the expression, so the value the caller returns is the value the introduced types made.
        private protected static Task<HotReloadOrchestratorResult> RunAsync(
            string label,
            string callerExpression,
            Dictionary<string, string> sources)
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            List<string> paths = new List<string> { callerPath };
            Dictionary<string, string> edits = new Dictionary<string, string>
            {
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "InternalAccessCaller" + label + ".cs",
                    callerSource.Replace(CallerBodyAnchor, "return " + callerExpression + ";", StringComparison.Ordinal))
            };
            foreach (KeyValuePair<string, string> source in sources)
            {
                paths.Add(source.Key);
                edits[source.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(source.Key) + label + ".cs",
                    source.Value);
            }

            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                paths.ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private protected static int CallTheCaller()
        {
            return new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
        }

        private protected static int ActiveTypeCount()
        {
            return HotReloadCompositionRoot.Services.Domain.IntroducedTypes.ActiveTypeCount;
        }

        private protected static void AssertIntroduced(HotReloadOrchestratorResult result, string simpleName)
        {
            Assert.That(CountFailures(result), Is.Zero, DescribeOutcomes(result));
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Introduced
                    && outcome.MetadataName == Namespace + "." + simpleName)
                {
                    return;
                }
            }

            Assert.Fail(simpleName + " must be reported as introduced.\n" + DescribeOutcomes(result));
        }

        private protected static string FindFailedIntroducedTypeReason(HotReloadOrchestratorResult result, string fragment)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason != null
                    && outcome.Reason.Contains(fragment, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            return null;
        }
    }
}
