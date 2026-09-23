using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a compiled file that references a type the same reload refused to
    /// introduce: the shim compile cannot find the type, and the failure has to say why.
    /// </summary>
    /// <remarks>
    /// Why the owner is not a fixture on disk: a .cs under Assets/ is compiled into the test
    /// assembly, so the type would exist and the reference would compile.
    /// </remarks>
    public class HotReloadIntroducedTypeRefusedReferrerE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        // The path the reload is asked for. Nothing is ever written here.
        private const string OwnerRequestedPath =
            "Assets/Tests/Editor/HotReload/UncompiledRefusedUnityObjectOwner.cs";

        private const string RefusedTypeSimpleName = "HotReloadRefusedUnityObjectValue";
        private const string CallerDeclarationAnchor =
            "        [MethodImpl(MethodImplOptions.NoInlining)]\n        public int Call(";
        private const string RefusedTypeNotePart =
            "'" + RefusedTypeSimpleName + "' was refused by this hot reload run (";

        // A Unity object type is refused introduction, because hot reload cannot give it a script
        // asset for Unity to serialize against.
        private const string OwnerSource =
            "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
            + "{\n"
            + "    public sealed class " + RefusedTypeSimpleName + " : UnityEngine.ScriptableObject\n"
            + "    {\n"
            + "        public static int Count = 3;\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: a method added to a compiled file that names a type the same reload refused
        /// fails, and its reason says the refusal caused the compile error and that 'uloop compile'
        /// clears it. The compile error alone says the type does not exist, while the source the
        /// reader sees still declares it. A type reference and a static member access fail with
        /// different compile errors, so both are covered.
        /// </summary>
        [TestCase("typeof(" + RefusedTypeSimpleName + ").Name.Length")]
        [TestCase(RefusedTypeSimpleName + ".Count")]
        public async Task Run_AddedMethodNamingARefusedType_FailureReasonNamesTheRefusal(string referrerExpression)
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunReloadAsync(
                    OwnerRequestedPath,
                    callerPath,
                    CreateEdits(callerPath, referrerExpression));

                foreach (HotReloadMethodOutcome outcome in result.Methods)
                {
                    if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                        && outcome.Reason != null
                        && outcome.Reason.Contains(RefusedTypeNotePart, StringComparison.Ordinal))
                    {
                        Assert.That(outcome.Reason, Does.Contain("run 'uloop compile'."));
                        return;
                    }
                }

                Assert.Fail("A Failed row must name the refused type as the cause.\n" + DescribeOutcomes(result));
            });
        }

        private static Dictionary<string, string> CreateEdits(string callerPath, string referrerExpression)
        {
            string addedReferrerMember =
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int UseRefused()\n"
                + "        {\n"
                + "            return " + referrerExpression + ";\n"
                + "        }\n\n";
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerDeclarationAnchor), "Precondition: caller declaration anchor must exist.");
            return new Dictionary<string, string>
            {
                [OwnerRequestedPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "RefusedUnityObjectOwner.cs",
                    OwnerSource),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "RefusedUnityObjectReferrer.cs",
                    callerSource.Replace(
                        CallerDeclarationAnchor,
                        addedReferrerMember + CallerDeclarationAnchor,
                        StringComparison.Ordinal))
            };
        }
    }
}
