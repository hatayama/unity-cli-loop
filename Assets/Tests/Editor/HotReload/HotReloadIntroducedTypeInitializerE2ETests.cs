using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a field added to a compiled type whose initializer constructs a type
    /// an earlier reload introduced, in the same reload that edits a body of that introduced type:
    /// the instance the initializer creates comes from the assembly the earlier reload retained and
    /// runs the edited body.
    /// </summary>
    public class HotReloadIntroducedTypeInitializerE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";
        private const string HostValueAnchor = "        public int Value()";
        private const string HostScaledBodyAnchor = "            return factor;\n";
        private const string CallerBodyAnchor = "return host.Value();";
        private const string IntroducedTypeSimpleName = "HotReloadInitializerIntroducedValue";
        private const int IntroducedSeed = 5;
        private const int EditedComputedValue = IntroducedSeed * 2;
        private const string SeedExpression = "_seed";
        private const string EditedExpression = "_seed * 2";

        // The added field and the compiled body that reads it. The reader is an existing compiled
        // method so the test can call it directly instead of reaching for the added method through
        // reflection, and the field is what carries the initializer this test is about.
        private const string CachedFieldMember =
            "        private " + IntroducedTypeSimpleName + " _cache = new " + IntroducedTypeSimpleName + "();\n"
            + "\n";

        private const string CachedScaledBody =
            "            return _cache.Compute() * factor;\n";

        /// <summary>
        /// Verifies that a field added to a compiled type with an initializer constructing an
        /// already introduced type applies in the reload that also edits that type's body: the
        /// reload reports no failure, and a call into the patched compiled body returns the value
        /// of the edited body, so the constructed instance is the retained artifact's type.
        /// </summary>
        [Test]
        public async Task Run_AddedFieldConstructsIntroducedTypeWhoseBodyThisReloadEdits_RunsTheEditedBody()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateIntroducingEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(first),
                    Is.EqualTo(0),
                    "Precondition: the introducing reload must apply.\n" + DescribeOutcomes(first));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(IntroducedSeed),
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult second = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateCachedFieldEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(second),
                    Is.EqualTo(0),
                    "A field whose initializer constructs the introduced type must not fail the "
                    + "reload.\n" + DescribeOutcomes(second));
                Assert.That(
                    FindSkipReason(second, "Scaled"),
                    Is.Null,
                    "The body reading the added field must be patched rather than skipped.\n"
                    + DescribeOutcomes(second));
                Assert.That(
                    new HotReloadCrossFileAddedMemberHost().Scaled(1),
                    Is.EqualTo(EditedComputedValue),
                    "The instance the initializer created must come from the retained assembly and "
                    + "run the body this reload edited.\n" + DescribeOutcomes(second));
            });
        }

        private static Dictionary<string, string> CreateIntroducingEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeInitializerHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeInitializerCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why distinct written file names for the caller whose text does not change: the run has to
        // be told about an edit, and reusing the first reload's path would let a cached read decide
        // the outcome instead of this reload's comparison.
        private static Dictionary<string, string> CreateCachedFieldEdits(string hostPath, string callerPath)
        {
            string hostSource = InsertIntroducedType(File.ReadAllText(hostPath), EditedExpression);
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeInitializerCachedHost.cs",
                    InsertCachedField(hostSource)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeInitializerCachedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why NoInlining on the introduced method: the test reads the body back through a call on
        // an instance the patched compiled body created.
        private static string InsertIntroducedType(string hostSource, string computedExpression)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        private readonly int _seed = " + IntroducedSeed.ToString() + ";\n"
                + "\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + computedExpression + ";\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        private static string InsertCachedField(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            Assert.That(hostSource, Does.Contain(HostScaledBodyAnchor), "Precondition: scaled body anchor must exist.");
            string withField = hostSource.Replace(
                HostValueAnchor,
                CachedFieldMember + HostValueAnchor,
                StringComparison.Ordinal);
            return withField.Replace(HostScaledBodyAnchor, CachedScaledBody, StringComparison.Ordinal);
        }

        private static string CallIntroducedType(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new " + IntroducedTypeSimpleName + "().Compute() + host.Value();",
                StringComparison.Ordinal);
        }

        private static int ReadComputedValue(HotReloadIntroducedTypeArtifact artifact)
        {
            Assert.That(artifact, Is.Not.Null, "The reload must have prepared an artifact.");
            Type introducedType = artifact.Assembly.GetType(
                typeof(HotReloadIntroducedTypeInitializerE2ETests).Namespace + "." + IntroducedTypeSimpleName,
                throwOnError: false);
            Assert.That(introducedType, Is.Not.Null, "The artifact must hold the introduced type.");
            MethodInfo compute = introducedType.GetMethod(
                "Compute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(compute, Is.Not.Null, "The introduced type must hold the method the test reads.");
            return (int)compute.Invoke(Activator.CreateInstance(introducedType), Array.Empty<object>());
        }

        private static string FindSkipReason(HotReloadOrchestratorResult result, string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(methodFragment))
                {
                    return outcome.Reason;
                }
            }

            return null;
        }
    }
}
