using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests language-version NextAction detection, wording, append behavior, and factory wiring.
    /// </summary>
    [TestFixture]
    public sealed class CompileErrorNextActionsComposerTests
    {
        private const string FileScopedNamespaceError =
            "error CS8370: Feature 'file-scoped namespace' is not available in C# 9.0. Please use language version 10.0 or greater.";

        private const string PrefixlessFileScopedNamespaceError =
            "CS8370: Feature 'file-scoped namespace' is not available in C# 9.0. Please use language version 10.0 or greater.";

        private const string FileScopedNamespaceNextAction =
            "error CS8370: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'file-scoped namespace' feature so the code compiles under C# 9.0.";

        private const string RecordsError =
            "error CS8400: Feature 'records' is not available in C# 8.0. Please use language version 9.0 or greater.";

        private const string RecordsNextAction =
            "error CS8400: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'records' feature so the code compiles under C# 8.0.";

        private const string RequiredMembersError =
            "error CS8652: Feature 'required members' is not available in C# 10. Please use language version 11.0 or greater.";

        private const string RequiredMembersNextAction =
            "error CS8652: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'required members' feature so the code compiles under C# 10.";

        private const string RawStringLiteralsError =
            "error CS8936: Feature 'raw string literals' is not available in C# 10.0. Please use language version 11.0 or greater.";

        private const string RawStringLiteralsNextAction =
            "error CS8936: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'raw string literals' feature so the code compiles under C# 10.0.";

        private const string UnrelatedError = "error CS0000: sample compile error";

        private const string ExistingNextAction =
            "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.";

        private const string ApiUpdaterNextAction =
            "Fix the obsolete API usages reported in Errors, or ask the user to accept the Script Updating Consent dialog in an interactive Unity session.";

        private const string InputSystemCs0234Error =
            "error CS0234: The type or namespace name 'InputSystem' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)";

        private const string PrefixlessInputSystemCs0234Error =
            "CS0234: The type or namespace name 'InputSystem' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)";

        private const string InputSystemNextAction =
            "error CS0234: 'UnityEngine.InputSystem' is declared in assembly 'Unity.InputSystem'. Add the assembly to the failing script's .asmdef references and run 'uloop compile' again. If the failing script has no .asmdef, the declaring assembly may have Auto Referenced disabled or its package may not be installed.";

        private const string DualAssemblyNextAction =
            "error CS0234: 'UnityEngine.InputSystem' is declared in assemblies 'Alpha.Assembly', 'Zebra.Assembly'. Add the assembly to the failing script's .asmdef references and run 'uloop compile' again. If the failing script has no .asmdef, the declaring assembly may have Auto Referenced disabled or its package may not be installed.";

        private const string TripleAssemblyNextAction =
            "error CS0234: 'UnityEngine.InputSystem' is declared in assemblies 'A.Assembly', 'B.Assembly', 'C.Assembly'. Add the assembly to the failing script's .asmdef references and run 'uloop compile' again. If the failing script has no .asmdef, the declaring assembly may have Auto Referenced disabled or its package may not be installed.";

        private const string Cs0246Error =
            "error CS0246: The type or namespace name 'InputSystem' could not be found (are you missing a using directive or an assembly reference?)";

        private const string NUnitCs0234Error =
            "error CS0234: The type or namespace name 'Framework' does not exist in the namespace 'NUnit' (are you missing an assembly reference?)";

        private const string NUnitFrameworkNextAction =
            "error CS0234: 'NUnit.Framework' is declared in assembly 'nunit.framework'. Add the assembly to the failing script's .asmdef references and run 'uloop compile' again. If the failing script has no .asmdef, the declaring assembly may have Auto Referenced disabled or its package may not be installed.";

        private const string UnknownCs0234Error =
            "error CS0234: The type or namespace name 'NoSuchInner' does not exist in the namespace 'NoSuchOuter' (are you missing an assembly reference?)";

        /// <summary>
        /// What: a language-version error produces the pinned-rewrite NextAction as an exact literal.
        /// </summary>
        [Test]
        public void Build_WhenLanguageVersionError_ReturnsPinnedRewriteAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { FileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: a prefix-less CS#### message still produces the same pinned-rewrite NextAction.
        /// </summary>
        [Test]
        public void Build_WhenPrefixlessErrorCode_ReturnsPinnedRewriteAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { PrefixlessFileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: unmatched error messages produce no NextActions.
        /// </summary>
        [Test]
        public void Build_WhenNoMatch_ReturnsEmpty()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { UnrelatedError });

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: a language-version sentence without a CS#### code is skipped (fail-open).
        /// </summary>
        [Test]
        public void Build_WhenMessageHasNoErrorCode_ReturnsEmpty()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { "Feature 'file-scoped namespace' is not available in C# 9.0." });

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: identical generated NextActions are appended only once.
        /// </summary>
        [Test]
        public void Build_WhenDuplicateGeneratedActions_Dedups()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { FileScopedNamespaceError, PrefixlessFileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: at most three generated NextActions are returned even when more messages match.
        /// </summary>
        [Test]
        public void Build_WhenMoreThanThreeMatches_ReturnsAtMostThree()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[]
                {
                    FileScopedNamespaceError,
                    RecordsError,
                    RequiredMembersError,
                    RawStringLiteralsError
                });

            Assert.That(
                nextActions,
                Is.EqualTo(new[]
                {
                    FileScopedNamespaceNextAction,
                    RecordsNextAction,
                    RequiredMembersNextAction
                }));
        }

        /// <summary>
        /// What: only the first ten error messages are scanned for NextAction generation.
        /// </summary>
        [Test]
        public void Build_WhenMatchIsAfterFirstTenMessages_ReturnsEmpty()
        {
            string[] errorMessages = new string[11];
            for (int index = 0; index < 10; index++)
            {
                errorMessages[index] = UnrelatedError;
            }

            errorMessages[10] = FileScopedNamespaceError;

            string[] nextActions = CompileErrorNextActionsBuilder.Build(errorMessages);

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: a successful compile leaves NextActions unchanged even when errors look matchable.
        /// </summary>
        [Test]
        public void Apply_WhenSuccess_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: true);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(FileScopedNamespaceError) });

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: a null error list leaves NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenErrorsNull_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, errors: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: unmatched errors leave existing NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenNoMatch_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(UnrelatedError) });

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: existing NextActions are kept and the language-version action is appended at the end.
        /// </summary>
        [Test]
        public void Apply_WhenExistingNextActions_AppendsLanguageVersionAction()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(FileScopedNamespaceError) });

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ExistingNextAction, FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: CreateResponse emits the language-version NextAction as the entire NextActions array.
        /// </summary>
        [Test]
        public void CreateResponse_WhenLanguageVersionError_ReturnsExactNextActions()
        {
            CompileResult result = CreateFailedResult(CreateError(FileScopedNamespaceError));

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: a determinate force-compile result keeps only the wait NextAction even when errors match.
        /// </summary>
        [Test]
        public void CreateResponse_WhenForceCompileWithLanguageVersionError_DoesNotAddRewriteAction()
        {
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { CreateError(FileScopedNamespaceError) },
                errors: new[] { CreateError(FileScopedNamespaceError) },
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: false,
                message: null);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: true,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: indeterminate non-force results do not append a language-version rewrite NextAction.
        /// </summary>
        [Test]
        public void CreateResponse_WhenIndeterminateWithLanguageVersionError_DoesNotAddRewriteAction()
        {
            CompileResult result = new CompileResult(
                success: null,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { CreateError(FileScopedNamespaceError) },
                errors: new[] { CreateError(FileScopedNamespaceError) },
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: true,
                message: null);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.Null);
        }

        /// <summary>
        /// What: CreateResponse appends the language-version NextAction after the API Updater action.
        /// </summary>
        [Test]
        public void CreateResponse_WhenLanguageVersionErrorAndConsentDeclined_AppendsAfterExistingNextActions()
        {
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: new[] { CreateError(FileScopedNamespaceError) },
                warnings: Array.Empty<CompilerMessage>(),
                apiUpdaterConsentDeclined: true);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ApiUpdaterNextAction, FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: a CS0234 error produces the declaring-assembly NextAction from the injected lookup.
        /// </summary>
        [Test]
        public void Build_WhenCs0234Error_ReturnsDeclaringAssemblyAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { InputSystemCs0234Error },
                searchName => searchName == "UnityEngine.InputSystem"
                    ? new[] { "Unity.InputSystem" }
                    : Array.Empty<string>());

            Assert.That(nextActions, Is.EqualTo(new[] { InputSystemNextAction }));
        }

        /// <summary>
        /// What: a prefix-less CS0234 message still produces the declaring-assembly NextAction.
        /// </summary>
        [Test]
        public void Build_WhenPrefixlessCs0234Error_ReturnsDeclaringAssemblyAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { PrefixlessInputSystemCs0234Error },
                searchName => new[] { "Unity.InputSystem" });

            Assert.That(nextActions, Is.EqualTo(new[] { InputSystemNextAction }));
        }

        /// <summary>
        /// What: CS0246 never produces a missing-reference NextAction.
        /// </summary>
        [Test]
        public void Build_WhenCs0246Error_ReturnsEmpty()
        {
            int lookupCalls = 0;
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { Cs0246Error },
                searchName =>
                {
                    lookupCalls++;
                    return new[] { "Unity.InputSystem" };
                });

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
            Assert.That(lookupCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a CS0234 match with zero declaring assemblies stays fail-open.
        /// </summary>
        [Test]
        public void Build_WhenCs0234LookupReturnsEmpty_ReturnsEmpty()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { InputSystemCs0234Error },
                searchName => Array.Empty<string>());

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: multiple declaring assemblies are named in ordinal order.
        /// </summary>
        [Test]
        public void Build_WhenCs0234HasMultipleAssemblies_NamesThemInOrdinalOrder()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { InputSystemCs0234Error },
                searchName => new[] { "Zebra.Assembly", "Alpha.Assembly" });

            Assert.That(nextActions, Is.EqualTo(new[] { DualAssemblyNextAction }));
        }

        /// <summary>
        /// What: more than three declaring assemblies are truncated after the first three sorted names.
        /// </summary>
        [Test]
        public void Build_WhenCs0234HasMoreThanThreeAssemblies_NamesAtMostThree()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { InputSystemCs0234Error },
                searchName => new[] { "D.Assembly", "C.Assembly", "B.Assembly", "A.Assembly" });

            Assert.That(nextActions, Is.EqualTo(new[] { TripleAssemblyNextAction }));
        }

        /// <summary>
        /// What: identical CS0234 NextActions are appended only once.
        /// </summary>
        [Test]
        public void Build_WhenDuplicateCs0234Actions_Dedups()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { InputSystemCs0234Error, PrefixlessInputSystemCs0234Error },
                searchName => new[] { "Unity.InputSystem" });

            Assert.That(nextActions, Is.EqualTo(new[] { InputSystemNextAction }));
        }

        /// <summary>
        /// What: language-version and CS0234 actions are appended in B-then-A order.
        /// </summary>
        [Test]
        public void Build_WhenLanguageVersionAndCs0234_AppendsLanguageVersionFirst()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { FileScopedNamespaceError, InputSystemCs0234Error },
                searchName => new[] { "Unity.InputSystem" });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction, InputSystemNextAction }));
        }

        /// <summary>
        /// What: a CS0234 with no declaring assembly leaves existing NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenCs0234HasNoDeclaringAssembly_LeavesExistingNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(UnknownCs0234Error) });

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: existing NextActions are kept and a resolved CS0234 action is appended at the end.
        /// </summary>
        [Test]
        public void Apply_WhenExistingNextActionsAndResolvedCs0234_AppendsDeclaringAssemblyAction()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(NUnitCs0234Error) });

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ExistingNextAction, NUnitFrameworkNextAction }));
        }

        /// <summary>
        /// What: CreateResponse names nunit.framework for a real CS0234 against NUnit.Framework.
        /// </summary>
        [Test]
        public void CreateResponse_WhenCs0234ForNUnitFramework_IncludesNunitFrameworkAssembly()
        {
            CompileResult result = CreateFailedResult(CreateError(NUnitCs0234Error));

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { NUnitFrameworkNextAction }));
        }

        /// <summary>
        /// What: CreateResponse stays fail-open when CS0234 names a namespace TypeCache does not declare.
        /// </summary>
        [Test]
        public void CreateResponse_WhenCs0234HasNoDeclaringAssembly_ReturnsNoNextActions()
        {
            CompileResult result = CreateFailedResult(CreateError(UnknownCs0234Error));

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.Null);
        }

        /// <summary>
        /// What: CreateResponse does not add a missing-reference NextAction for CS0246.
        /// </summary>
        [Test]
        public void CreateResponse_WhenCs0246Error_ReturnsNoNextActions()
        {
            CompileResult result = CreateFailedResult(CreateError(Cs0246Error));

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(response.NextActions, Is.Null);
        }

        /// <summary>
        /// What: a CS0246 raised from a script that belongs to an asmdef appends the reference-gap hint
        /// naming that asmdef, resolved through Unity's CompilationPipeline.
        /// </summary>
        [Test]
        public void Apply_WhenCs0246FromScriptUnderAsmdef_AppendsAssemblyDefinitionReferenceHint()
        {
            const string scriptPath = "Assets/Tests/Editor/CompileErrorNextActionsComposerTests.cs";
            const string expectedHint =
                "error CS0246: 'InputSystem' could not be found from a script under 'Assets/Tests/Editor/UnityCLILoop.Tests.Editor.asmdef'. If that type lives in another assembly (for example the script was recently moved under a new asmdef), add the declaring assembly to that asmdef's references and run 'uloop compile' again; if the name is a typo, fix the name instead.";
            CompileResponse response = CreateResponse(success: false);
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = Cs0246Error,
                file = scriptPath,
                line = 1
            };

            CompileErrorNextActionsComposer.Apply(response, new[] { error });

            Assert.That(response.NextActions, Is.EqualTo(new[] { expectedHint }));
        }

        /// <summary>
        /// What: CreateResponse appends the TypeCache CS0234 action after the API Updater action.
        /// </summary>
        [Test]
        public void CreateResponse_WhenCs0234AndConsentDeclined_AppendsAfterExistingNextActions()
        {
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: new[] { CreateError(NUnitCs0234Error) },
                warnings: Array.Empty<CompilerMessage>(),
                apiUpdaterConsentDeclined: true);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: null);

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ApiUpdaterNextAction, NUnitFrameworkNextAction }));
        }

        /// <summary>
        /// What: TypeCache.GetTypesDerivedFrom(typeof(object)) lists NUnit.Framework types in nunit.framework.
        /// </summary>
        [Test]
        public void TypeCache_GetTypesDerivedFromObject_IncludesNunitFrameworkForNUnitFrameworkNamespace()
        {
            List<string> assemblyNames = new List<string>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom(typeof(object)))
            {
                if (type.Namespace != "NUnit.Framework")
                {
                    continue;
                }

                string assemblyName = type.Assembly.GetName().Name;
                if (assemblyNames.Contains(assemblyName))
                {
                    continue;
                }

                assemblyNames.Add(assemblyName);
            }

            Assert.That(assemblyNames, Does.Contain("nunit.framework"));
        }

        private static CompileResponse CreateResponse(bool success)
        {
            return new CompileResponse(
                success: success,
                errorCount: success ? 0 : 1,
                warningCount: 0,
                errors: null,
                warnings: null,
                message: null);
        }

        private static CompileResult CreateFailedResult(CompilerMessage error)
        {
            return new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { error },
                errors: new[] { error },
                warnings: Array.Empty<CompilerMessage>());
        }

        private static CompilerMessage CreateError(string message)
        {
            return new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = message,
                file = "Assets/Sample.cs",
                line = 1
            };
        }
    }
}
