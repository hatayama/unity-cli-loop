using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for bodies that use <c>base.</c>: a direct base method call in a compiled
    /// method's own statements becomes a transplant entry whose shim calls a stand-in, and every
    /// other base use is a Skipped row carrying the reason code for that use.
    /// </summary>
    public class TransformWorkerBaseCallTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadBaseCallFixtures.cs";
        private const string DerivedLabelPrefix = "HotReloadBaseCallDerivedFixture.";
        private const string ComputeStandInNamePrefix = "__uloopBaseCall__d2__Compute__";
        private const string ComputeStandInDeclaration = "public static int __uloopBaseCall__d2__Compute__0(";
        private const string CallGenericDeclaration =
            "        public int CallGeneric(int input) { return base.Echo(input) + 6124; }";

        /// <summary>
        /// What: an override whose own statements call its base method emits a transplant entry
        /// whose shim calls one stand-in instead of writing base.
        /// </summary>
        [Test]
        public async Task Run_OverrideCallingBaseMethod_EmitsTransplantEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallCompute.cs",
                source => ReplaceUniqueFragment(source, "6101", "7101"));

            TransformWorkerEntryDto entry = AssertTransplantEntryWithoutSkip(
                result,
                nameof(HotReloadBaseCallDerivedFixture.Compute));
            AssertShimCallsComputeStandIn(result, entry);
            Assert.That(
                CountOccurrences(result.Output.shimSource, ComputeStandInDeclaration),
                Is.EqualTo(1),
                "The shim type must declare the Compute stand-in exactly once.");
        }

        /// <summary>
        /// What: a base call next to a lambda that reads a private field still emits a transplant
        /// entry whose shim calls the stand-in instead of writing base.
        /// </summary>
        [Test]
        public async Task Run_BaseCallBesidePrivateLambda_EmitsTransplantEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallBesidePrivateLambda.cs",
                source => ReplaceUniqueFragment(source, "6107", "7107"));

            TransformWorkerEntryDto entry = AssertTransplantEntryWithoutSkip(
                result,
                nameof(HotReloadBaseCallDerivedFixture.CallBesidePrivateLambda));
            AssertShimCallsComputeStandIn(result, entry);
        }

        /// <summary>What: reading a base property is a non-call base use and stays Skipped.</summary>
        [Test]
        public async Task Run_BasePropertyRead_IsSkippedAsNonCallBaseUse()
        {
            await AssertEditIsSkippedWithCode(
                "6111",
                nameof(HotReloadBaseCallDerivedFixture.ReadBaseProperty),
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall);
        }

        /// <summary>What: taking a base method group is a non-call base use and stays Skipped.</summary>
        [Test]
        public async Task Run_BaseMethodGroup_IsSkippedAsNonCallBaseUse()
        {
            await AssertEditIsSkippedWithCode(
                "6112",
                nameof(HotReloadBaseCallDerivedFixture.UseMethodGroup),
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall);
        }

        /// <summary>What: a base call inside a lambda is Skipped as outside the method's own statements.</summary>
        [Test]
        public async Task Run_BaseCallInLambda_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6113",
                nameof(HotReloadBaseCallDerivedFixture.CallInLambda),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>What: a base call inside a local function is Skipped as outside the method's own statements.</summary>
        [Test]
        public async Task Run_BaseCallInLocalFunction_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6114",
                nameof(HotReloadBaseCallDerivedFixture.CallInLocalFunction),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>What: a base call in an async method is Skipped as outside the method's own statements.</summary>
        [Test]
        public async Task Run_BaseCallInAsyncMethod_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6115",
                nameof(HotReloadBaseCallDerivedFixture.CallInAsync),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>What: a base call in an iterator is Skipped as outside the method's own statements.</summary>
        [Test]
        public async Task Run_BaseCallInIterator_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6116",
                nameof(HotReloadBaseCallDerivedFixture.CallInIterator),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>What: a base call in a property getter is Skipped as outside a method's own statements.</summary>
        [Test]
        public async Task Run_BaseCallInPropertyGetter_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6117",
                "get_" + nameof(HotReloadBaseCallDerivedFixture.ViaGetter),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>
        /// What: a body that raises an event is patched through a delegating shim, so its base call
        /// is Skipped as outside the method's own statements.
        /// </summary>
        [Test]
        public async Task Run_BaseCallInDelegatedBody_IsSkippedAsOutsideOwnStatements()
        {
            await AssertEditIsSkippedWithCode(
                "6118",
                nameof(HotReloadBaseCallDerivedFixture.CallAndRaise),
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>What: a base call in a method this reload adds is Skipped as outside a compiled method.</summary>
        [Test]
        public async Task Run_BaseCallInAddedMethod_IsSkippedAsOutsideOwnStatements()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallInAddedMethod.cs",
                source => WithDerivedMember(
                    source,
                    "public int AddedCaller(int input) { return base.Compute(input) + 6141; }"));

            AssertSkippedWithCode(
                result,
                "AddedCaller",
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        /// <summary>
        /// What: a non-call base use in an added method names the non-call reason, which no change
        /// of placement fixes, ahead of the placement reason.
        /// </summary>
        [Test]
        public async Task Run_BasePropertyReadInAddedMethod_IsSkippedAsNonCallBaseUse()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BasePropertyReadInAddedMethod.cs",
                source => WithDerivedMember(
                    source,
                    "public int AddedReader() { return base.Value + 6143; }"));

            AssertSkippedWithCode(
                result,
                "AddedReader",
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall);
        }

        /// <summary>What: a base call that omits an optional argument is Skipped for its argument shape.</summary>
        [Test]
        public async Task Run_BaseCallOmittingOptionalArgument_IsSkippedAsArgumentShape()
        {
            await AssertEditIsSkippedWithCode(
                "6121",
                nameof(HotReloadBaseCallDerivedFixture.CallOptionalOmitted),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>What: a base call with a named argument is Skipped for its argument shape.</summary>
        [Test]
        public async Task Run_BaseCallWithNamedArgument_IsSkippedAsArgumentShape()
        {
            await AssertEditIsSkippedWithCode(
                "6122",
                nameof(HotReloadBaseCallDerivedFixture.CallNamed),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>What: a base call to a params method is Skipped for its argument shape.</summary>
        [Test]
        public async Task Run_BaseCallToParamsMethod_IsSkippedAsArgumentShape()
        {
            await AssertEditIsSkippedWithCode(
                "6123",
                nameof(HotReloadBaseCallDerivedFixture.CallParams),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>
        /// What: a base call to a params method in expanded form with as many arguments as parameters
        /// is Skipped for its argument shape, which only the params rule catches.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToParamsMethodWithOneArgument_IsSkippedAsArgumentShape()
        {
            await AssertEditIsSkippedWithCode(
                "6126",
                nameof(HotReloadBaseCallDerivedFixture.CallParamsExpanded),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>
        /// What: a base call to a method marked [Conditional] is Skipped, because the compiler drops the
        /// call and its argument evaluation when the symbol is undefined and hot reload cannot.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToConditionalMethod_IsSkippedAsConditionalTarget()
        {
            await AssertEditIsSkippedWithCode(
                "6127",
                nameof(HotReloadBaseCallDerivedFixture.CallConditional),
                HotReloadWorkerReasonCode.MethodTransformBaseCallConditionalTarget);
        }

        /// <summary>
        /// What: a base call to an override of a [Conditional] method is Skipped too, although the
        /// override itself carries no attribute.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToOverrideOfConditionalMethod_IsSkippedAsConditionalTarget()
        {
            await AssertEditIsSkippedWithCode(
                "6128",
                nameof(HotReloadBaseCallDerivedFixture.CallConditionalOverride),
                HotReloadWorkerReasonCode.MethodTransformBaseCallConditionalTarget);
        }

        /// <summary>
        /// What: a base call to a method taking an array of pointers is Skipped for its argument shape,
        /// because the stand-in cannot declare that parameter in a shim compiled without unsafe code.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToPointerArrayParameter_IsSkippedAsArgumentShape()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallToPointerArrayParameter.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        source,
                        "        public virtual int Keyword(int @event) { return @event + 600; }\n",
                        "        public virtual int Keyword(int @event) { return @event + 600; }\n"
                        + "        public virtual unsafe int Pointers(int*[] values) { return 0; }\n"),
                    "return base.Keyword(input) + 6125;",
                    "return base.Pointers(null) + 7125;"));

            AssertSkippedWithCode(
                result,
                nameof(HotReloadBaseCallDerivedFixture.CallKeywordParameter),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>
        /// What: a base call to Pick(int) on Base&lt;int&gt;, whose base also declares Pick(T), is Skipped
        /// as ambiguous, because the two overloads have the same parameter types once T is int.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToOverloadCollidingAfterSubstitution_IsSkippedAsAmbiguousTarget()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallCollisionPick.cs",
                source => ReplaceUniqueFragment(source, "6141", "7141"));

            AssertCollisionSkippedWithCode(result, nameof(HotReloadBaseCallCollisionDerivedFixture.CallPick));
        }

        /// <summary>
        /// What: a base call to Take(ref int) on Base&lt;int&gt;, whose base also declares Take(out T), is
        /// Skipped as ambiguous rather than as a target the compiled assembly does not hold.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToRefOverloadCollidingWithOut_IsSkippedAsAmbiguousTarget()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallCollisionTake.cs",
                source => ReplaceUniqueFragment(source, "6142", "7142"));

            AssertCollisionSkippedWithCode(result, nameof(HotReloadBaseCallCollisionDerivedFixture.CallTakeByRef));
        }

        /// <summary>
        /// What: on the same generic base, a base call to a method with no colliding overload is still
        /// emitted as a transplant entry.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToNonCollidingMethodOnSameBase_EmitsTransplantEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallCollisionPlain.cs",
                source => ReplaceUniqueFragment(source, "6143", "7143"));

            TransformWorkerEntryDto entry = result.Output.entries.FirstOrDefault(candidate =>
                candidate.typeMetadataName == typeof(HotReloadBaseCallCollisionDerivedFixture).FullName
                && candidate.methodName == nameof(HotReloadBaseCallCollisionDerivedFixture.CallPlain));
            Assert.That(entry, Is.Not.Null, "Skipped=" + FormatSkipped(result.Output.skipped));
        }

        /// <summary>
        /// What: a stand-in for a base method whose parameter name is a C# keyword names its
        /// parameters by position, so the shim source stays valid C#.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToKeywordNamedParameter_StandInNamesParametersByPosition()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallKeywordParameter.cs",
                source => ReplaceUniqueFragment(source, "6125", "7125"));

            AssertTransplantEntryWithoutSkip(result, nameof(HotReloadBaseCallDerivedFixture.CallKeywordParameter));
            Assert.That(
                result.Output.shimSource,
                Does.Contain("__uloopBaseCall__d2__Keyword__0(global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBaseCallDerivedFixture __uloopInstance, int p0)"),
                result.Output.shimSource);
        }

        /// <summary>What: a base call to a generic method is Skipped for its argument shape.</summary>
        [Test]
        public async Task Run_BaseCallToGenericMethod_IsSkippedAsArgumentShape()
        {
            await AssertEditIsSkippedWithCode(
                "6124",
                nameof(HotReloadBaseCallDerivedFixture.CallGeneric),
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
        }

        /// <summary>
        /// What: a base call that now binds to an override added to the intermediate type since the
        /// last compile is Skipped because the compiled assembly does not hold that target.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToMethodAddedSinceCompile_IsSkippedAsTargetNotCompiled()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallToMethodAddedSinceCompile.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, "6101", "7101"),
                    "        public override void CheckThroughOverride(int value) { }\n",
                    "        public override void CheckThroughOverride(int value) { }\n"
                    + "        public override int Compute(int input) { return input + 6142; }\n"));

            AssertSkippedWithCode(
                result,
                nameof(HotReloadBaseCallDerivedFixture.Compute) + "(",
                HotReloadWorkerReasonCode.MethodTransformBaseCallTargetNotCompiled);
        }

        /// <summary>
        /// What: a base call whose target changed a parameter from out to ref since the last compile is
        /// Skipped because the compiled assembly does not hold that signature.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToTargetWithChangedRefKind_IsSkippedAsTargetNotCompiled()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCallToTargetWithChangedRefKind.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        ReplaceUniqueFragment(source, "6103", "7103"),
                        "public virtual void Fill(int input, out int result, ref int counter)",
                        "public virtual void Fill(int input, ref int result, ref int counter)"),
                    "            base.Fill(input, out int result, ref counter);",
                    "            int result = 0;\n            base.Fill(input, ref result, ref counter);"));

            AssertSkippedWithCode(
                result,
                nameof(HotReloadBaseCallDerivedFixture.CallFill),
                HotReloadWorkerReasonCode.MethodTransformBaseCallTargetNotCompiled);
        }

        private static async Task AssertEditIsSkippedWithCode(
            string token,
            string methodName,
            HotReloadWorkerReasonCode expectedCode)
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "BaseCall_" + methodName + ".cs",
                source => ReplaceUniqueFragment(source, token, "7" + token.Substring(1)));

            AssertSkippedWithCode(result, methodName, expectedCode);
        }

        private static void AssertCollisionSkippedWithCode(TransformWorkerClientResult result, string methodName)
        {
            string labelFragment = "HotReloadBaseCallCollisionDerivedFixture." + methodName + "(";
            TransformWorkerSkippedDto skipped = FindSkipped(result, labelFragment);
            Assert.That(
                skipped,
                Is.Not.Null,
                "Expected a Skipped row for '" + labelFragment + "'. Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformBaseCallAmbiguousTarget),
                "Skipped=" + FormatSkipped(result.Output.skipped));
        }

        private static void AssertSkippedWithCode(
            TransformWorkerClientResult result,
            string methodNameFragment,
            HotReloadWorkerReasonCode expectedCode)
        {
            string labelFragment = DerivedLabelPrefix + methodNameFragment;
            TransformWorkerSkippedDto skipped = FindSkipped(result, labelFragment);
            Assert.That(
                skipped,
                Is.Not.Null,
                "Expected a Skipped row for '" + labelFragment + "'. Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(skipped.reason, Is.Not.Null, "The Skipped row must carry a reason.");
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(expectedCode),
                "Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(
                FindDerivedEntry(result, methodNameFragment.TrimEnd('(')),
                Is.Null,
                "A Skipped method must not also be an entry.");
        }

        private static TransformWorkerEntryDto AssertTransplantEntryWithoutSkip(
            TransformWorkerClientResult result,
            string methodName)
        {
            TransformWorkerEntryDto entry = FindDerivedEntry(result, methodName);
            Assert.That(
                entry,
                Is.Not.Null,
                "Expected an entry for '" + methodName + "'. Skipped=" + FormatSkipped(result.Output.skipped));
            // Why the literal: the Editor has no constant for the transplant wire value; the
            // worker's PatchKinds.Transplant is the source of truth.
            Assert.That(entry.patchKind, Is.EqualTo("transplant"));
            Assert.That(
                FindSkipped(result, DerivedLabelPrefix + methodName + "("),
                Is.Null,
                "An emitted method must not also be Skipped.");
            return entry;
        }

        // Why check the shim text: the worker only returns shim source, so a leftover base. would
        // surface only later as a shim compile error in the Editor.
        private static void AssertShimCallsComputeStandIn(
            TransformWorkerClientResult result,
            TransformWorkerEntryDto entry)
        {
            string shimMethod = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(shimMethod, Does.Not.Contain("base."), shimMethod);
            Assert.That(shimMethod, Does.Contain(ComputeStandInNamePrefix), shimMethod);
        }

        private static TransformWorkerEntryDto FindDerivedEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == typeof(HotReloadBaseCallDerivedFixture).FullName
                    && entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string labelFragment)
        {
            if (result.Output.skipped == null)
            {
                return null;
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(labelFragment, StringComparison.Ordinal))
                {
                    return skipped;
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerSkippedDto[] skipped)
        {
            if (skipped == null || skipped.Length == 0)
            {
                return "(none)";
            }

            List<string> rows = new List<string>();
            foreach (TransformWorkerSkippedDto entry in skipped)
            {
                rows.Add(entry.method + " :: " + (entry.reason == null ? "(no reason)" : entry.reason.code.ToString()));
            }

            return string.Join("\n", rows);
        }

        private static int CountOccurrences(string text, string fragment)
        {
            int count = 0;
            int index = text.IndexOf(fragment, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static string SliceShimMethod(string shimSource, string shimMethodName)
        {
            // Why the '(': a shim type may also declare a counter whose name starts with the shim
            // method name.
            int nameIndex = shimSource.IndexOf(shimMethodName + "(", StringComparison.Ordinal);
            Assert.That(nameIndex, Is.GreaterThanOrEqualTo(0), "Shim method missing: " + shimMethodName);
            int declarationStart = shimSource.LastIndexOf("public static", nameIndex, StringComparison.Ordinal);
            int openBrace = shimSource.IndexOf('{', nameIndex);
            Assert.That(openBrace, Is.GreaterThan(0));
            int depth = 0;
            for (int index = openBrace; index < shimSource.Length; index++)
            {
                if (shimSource[index] == '{')
                {
                    depth++;
                }
                else if (shimSource[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return shimSource.Substring(declarationStart, index - declarationStart + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced shim method: " + shimMethodName);
            return string.Empty;
        }

        private static string WithDerivedMember(string source, string member)
        {
            return ReplaceUniqueFragment(
                source,
                CallGenericDeclaration,
                CallGenericDeclaration + "\n\n        " + member);
        }

        // Why the uniqueness check: Replace edits every occurrence, and a token that also landed
        // somewhere else would edit a method other than the one the test is about.
        private static string ReplaceUniqueFragment(string source, string fragment, string replacement)
        {
            int index = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Precondition: the fixture must hold " + fragment);
            Assert.That(
                source.LastIndexOf(fragment, StringComparison.Ordinal),
                Is.EqualTo(index),
                "Precondition: the fixture must hold " + fragment + " exactly once.");
            return source.Replace(fragment, replacement, StringComparison.Ordinal);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnEditedFixtureAsync(
            string fileName,
            Func<string, string> edit)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fixturePath = Path.Combine(
                projectRoot,
                FixtureProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = edit(onDisk);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(sourcePath, onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string snapshotSource)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            List<string> referencePaths = new List<string>();
            if (compilationAssembly.allReferences != null)
            {
                foreach (string reference in compilationAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        referencePaths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!referencePaths.Contains(fullTarget))
            {
                referencePaths.Add(fullTarget);
            }

            List<string> assemblySourcePaths = new List<string>();
            if (compilationAssembly.sourceFiles != null)
            {
                foreach (string sourceFile in compilationAssembly.sourceFiles)
                {
                    assemblySourcePaths.Add(Path.GetFullPath(Path.Combine(projectRoot, sourceFile)));
                }
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = FixtureProjectRelativePath,
                        snapshotSource = snapshotSource
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = assemblySourcePaths.ToArray(),
                // No sibling is passed, which stands for a scan that compared every sibling and
                // found none changed.
                changedSiblingScanComplete = true
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }
    }
}
