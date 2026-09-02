using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the ready-to-write test .asmdef proposal built on zero discovery.
    /// </summary>
    public sealed class RunTestsTestAsmdefProposalBuilderTests
    {
        private const string ProductName = "My Game";

        [Test]
        public void Build_WhenAnEditorTestAsmdefExists_ReturnsNullForEditMode()
        {
            // Verifies no proposal is made when the project already has a test assembly for the requested mode.
            RunTestsAsmdefInfo[] asmdefs =
            {
                Runtime("Assets/Scripts/Game.asmdef", "Game"),
                TestAsmdef("Assets/Tests/Editor/Game.Tests.Editor.asmdef", "Game.Tests.Editor", editorOnly: true)
            };

            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.EditMode, ProductName);

            Assert.That(proposal, Is.Null);
        }

        [Test]
        public void Build_WhenOnlyAPlayModeTestAsmdefExists_StillProposesAnEditModeOne()
        {
            // Verifies mode relevance is decided by the Editor platform: a runtime test assembly cannot host EditMode tests.
            RunTestsAsmdefInfo[] asmdefs =
            {
                Runtime("Assets/Scripts/Game.asmdef", "Game"),
                TestAsmdef("Assets/Tests/PlayMode/Game.Tests.PlayMode.asmdef", "Game.Tests.PlayMode", editorOnly: false)
            };

            RunTestsTestAsmdefProposal editMode = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.EditMode, ProductName);
            RunTestsTestAsmdefProposal playMode = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.PlayMode, ProductName);

            Assert.That(editMode, Is.Not.Null);
            Assert.That(playMode, Is.Null);
        }

        [Test]
        public void Build_WithSingleRuntimeAsmdef_NamesTheProposalAfterItAndReferencesIt()
        {
            // Verifies the proposal is concrete: named after the one assembly under test, saved under
            // Assets/Tests/Editor, marked as a test assembly, Editor-only, and referencing that assembly.
            RunTestsAsmdefInfo[] asmdefs = { Runtime("Assets/Scripts/Game.asmdef", "Game") };

            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.EditMode, ProductName);
            JObject content = JObject.Parse(proposal.Content);

            Assert.That(proposal.AssetPath, Is.EqualTo("Assets/Tests/Editor/Game.Tests.Editor.asmdef"));
            Assert.That(content["name"].Value<string>(), Is.EqualTo("Game.Tests.Editor"));
            Assert.That(content["references"].Values<string>(), Is.EqualTo(new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner", "Game" }));
            Assert.That(content["includePlatforms"].Values<string>(), Is.EqualTo(new[] { "Editor" }));
            Assert.That(content["overrideReferences"].Value<bool>(), Is.True);
            Assert.That(content["precompiledReferences"].Values<string>(), Is.EqualTo(new[] { "nunit.framework.dll" }));
            Assert.That(content["defineConstraints"].Values<string>(), Is.EqualTo(new[] { "UNITY_INCLUDE_TESTS" }));
        }

        [Test]
        public void Build_ForPlayMode_TargetsAllPlatformsAndSkipsEditorOnlyAsmdefs()
        {
            // Verifies a PlayMode proposal is not Editor-only and does not reference editor assemblies,
            // which a runtime test assembly cannot compile against.
            RunTestsAsmdefInfo[] asmdefs =
            {
                Runtime("Assets/Scripts/Game.asmdef", "Game"),
                EditorAsmdef("Assets/Editor/Game.Editor.asmdef", "Game.Editor")
            };

            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.PlayMode, ProductName);
            JObject content = JObject.Parse(proposal.Content);

            Assert.That(proposal.AssetPath, Is.EqualTo("Assets/Tests/PlayMode/Game.Tests.PlayMode.asmdef"));
            Assert.That(content["includePlatforms"].Values<string>(), Is.Empty);
            Assert.That(content["references"].Values<string>(), Is.EqualTo(new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner", "Game" }));
        }

        [Test]
        public void Build_WithMultipleRuntimeAsmdefs_UsesSanitizedProductNameAndReferencesAll()
        {
            // Verifies the product name (with non-identifier characters removed) names the proposal when no
            // single assembly under test stands out, and every assembly under test is referenced.
            RunTestsAsmdefInfo[] asmdefs =
            {
                Runtime("Assets/Scripts/Game.Core.asmdef", "Game.Core"),
                Runtime("Assets/Scripts/Game.UI.asmdef", "Game.UI")
            };

            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(asmdefs, UnityCliLoopTestMode.EditMode, ProductName);
            JObject content = JObject.Parse(proposal.Content);

            Assert.That(content["name"].Value<string>(), Is.EqualTo("MyGame.Tests.Editor"));
            Assert.That(content["references"].Values<string>().Skip(2), Is.EqualTo(new[] { "Game.Core", "Game.UI" }));
        }

        [Test]
        public void Build_WithNoAsmdefsAndUnusableProductName_FallsBackToAGenericName()
        {
            // Verifies a project with no asmdefs and a product name made of symbols still gets a valid proposal.
            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(Array.Empty<RunTestsAsmdefInfo>(), UnityCliLoopTestMode.EditMode, "!!!");
            JObject content = JObject.Parse(proposal.Content);

            Assert.That(content["name"].Value<string>(), Is.EqualTo("Project.Tests.Editor"));
            Assert.That(content["references"].Values<string>(), Is.EqualTo(new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner" }));
        }

        [Test]
        public void AppendNotice_AddsAPeriodBeforeTheNoticeWhenTheMessageHasNone()
        {
            // Verifies the notice names the proposed path and joins the existing message with a sentence break.
            RunTestsTestAsmdefProposal proposal = RunTestsTestAsmdefProposalBuilder.Build(Array.Empty<RunTestsAsmdefInfo>(), UnityCliLoopTestMode.EditMode, ProductName);

            string message = RunTestsTestAsmdefProposal.AppendNotice(RunTestsResponse.NoTestsFoundMessage, proposal);

            Assert.That(message, Does.StartWith(RunTestsResponse.NoTestsFoundMessage + ". "));
            Assert.That(message, Does.Contain("ProposedTestAsmdef"));
            Assert.That(message, Does.Contain("Assets/Tests/Editor/MyGame.Tests.Editor.asmdef"));
        }

        private static RunTestsAsmdefInfo Runtime(string assetPath, string name)
        {
            return new RunTestsAsmdefInfo(assetPath, "guid-" + name, name, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), testAssemblies: false);
        }

        private static RunTestsAsmdefInfo EditorAsmdef(string assetPath, string name)
        {
            return new RunTestsAsmdefInfo(assetPath, "guid-" + name, name, Array.Empty<string>(), Array.Empty<string>(), new[] { "Editor" }, testAssemblies: false);
        }

        private static RunTestsAsmdefInfo TestAsmdef(string assetPath, string name, bool editorOnly)
        {
            string[] includePlatforms = editorOnly ? new[] { "Editor" } : Array.Empty<string>();
            return new RunTestsAsmdefInfo(assetPath, "guid-" + name, name, new[] { "UnityEngine.TestRunner" }, Array.Empty<string>(), includePlatforms, testAssemblies: false);
        }
    }
}
