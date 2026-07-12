using System;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies surgical asmdef references edits preserve surrounding bytes.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAsmdefTextEditorTests
    {
        [Test]
        public void ReplaceReferencesArray_WhenSourceUsesCrlfAndFourSpaceIndent_PreservesBytesOutsideArray()
        {
            // Verifies CRLF/4-space asmdef text keeps every byte outside the references array.
            string source =
                "{\r\n" +
                "    \"name\": \"MyCompany.Tools.Editor\",\r\n" +
                "    \"references\": [\r\n" +
                "        \"uLoopMCP.Editor\"\r\n" +
                "    ],\r\n" +
                "    \"includePlatforms\": [\r\n" +
                "        \"Editor\"\r\n" +
                "    ]\r\n" +
                "}\r\n";
            string expected =
                "{\r\n" +
                "    \"name\": \"MyCompany.Tools.Editor\",\r\n" +
                "    \"references\": [\r\n" +
                "        \"GUID:fc3fd32eddbee40e39c2d76dc184957b\"\r\n" +
                "    ],\r\n" +
                "    \"includePlatforms\": [\r\n" +
                "        \"Editor\"\r\n" +
                "    ]\r\n" +
                "}\r\n";

            ThirdPartyToolMigrationAsmdefTextEditor.AsmdefReferencesEditResult result =
                ThirdPartyToolMigrationAsmdefTextEditor.ReplaceReferencesArray(
                    source,
                    new[] { "GUID:fc3fd32eddbee40e39c2d76dc184957b" });

            Assert.That(result.Replaced, Is.True);
            Assert.That(result.Content, Is.EqualTo(expected));
        }

        [Test]
        public void ReplaceReferencesArray_WhenSourceUsesTwoSpaceIndentAndLf_MatchesSourceStyle()
        {
            // Verifies 2-space LF asmdefs render migrated elements with matching indentation.
            string source =
                "{\n" +
                "  \"name\": \"MyCompany.Tools.Editor\",\n" +
                "  \"references\": [\n" +
                "    \"Old\"\n" +
                "  ]\n" +
                "}\n";

            ThirdPartyToolMigrationAsmdefTextEditor.AsmdefReferencesEditResult result =
                ThirdPartyToolMigrationAsmdefTextEditor.ReplaceReferencesArray(
                    source,
                    new[] { "New1", "New2" });

            Assert.That(result.Replaced, Is.True);
            Assert.That(result.Content, Does.Contain("  \"references\": [\n    \"New1\",\n    \"New2\"\n  ]"));
            Assert.That(result.Content, Does.Not.Contain("\r\n"));
        }

        [Test]
        public void ReplaceReferencesArray_WhenArrayIsSingleLine_KeepsSingleLineFormat()
        {
            // Verifies a single-line references array stays single-line after replacement.
            string source = "{\"name\":\"MyCompany.Tools.Editor\",\"references\": [\"Old\"]}";

            ThirdPartyToolMigrationAsmdefTextEditor.AsmdefReferencesEditResult result =
                ThirdPartyToolMigrationAsmdefTextEditor.ReplaceReferencesArray(
                    source,
                    new[] { "New1", "New2" });

            Assert.That(result.Replaced, Is.True);
            Assert.That(result.Content, Is.EqualTo(
                "{\"name\":\"MyCompany.Tools.Editor\",\"references\": [\"New1\", \"New2\"]}"));
        }

        [Test]
        public void ReplaceReferencesArray_WhenStringValueEqualsReferences_IgnoresDecoyValue()
        {
            // Verifies decoy string values and escaped text do not steal the references array match.
            string source =
                "{\n" +
                "  \"name\": \"references\",\n" +
                "  \"note\": \"\\\"references\\\":\",\n" +
                "  \"references\": [\"Old\"]\n" +
                "}\n";

            ThirdPartyToolMigrationAsmdefTextEditor.AsmdefReferencesEditResult result =
                ThirdPartyToolMigrationAsmdefTextEditor.ReplaceReferencesArray(
                    source,
                    new[] { "New" });

            Assert.That(result.Replaced, Is.True);
            Assert.That(result.Content, Does.Contain("\"name\": \"references\""));
            Assert.That(result.Content, Does.Contain("\"note\": \"\\\"references\\\":\""));
            Assert.That(result.Content, Does.Contain("\"references\": [\"New\"]"));
        }

        [Test]
        public void ReplaceReferencesArray_WhenReferencesPropertyIsMissing_ReturnsNotReplaced()
        {
            // Verifies asmdefs without a references property report NotReplaced.
            string source =
                "{\n" +
                "  \"name\": \"MyCompany.Tools.Editor\"\n" +
                "}\n";

            ThirdPartyToolMigrationAsmdefTextEditor.AsmdefReferencesEditResult result =
                ThirdPartyToolMigrationAsmdefTextEditor.ReplaceReferencesArray(
                    source,
                    new[] { "New" });

            Assert.That(result.Replaced, Is.False);
            Assert.That(result.Content, Is.EqualTo(string.Empty));
        }

        [Test]
        public void MigrateAsmdefSource_WhenReferencesPropertyIsMissing_FallsBackToReserialization()
        {
            // Verifies missing references still gains required entries through full re-serialization.
            string source =
                "{\n" +
                "    \"name\": \"MyCompany.Tools.Editor\"\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("\"references\": ["));
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenNothingChanges_ReturnsSourceUnchanged()
        {
            // Verifies the early-return path leaves the original asmdef text untouched.
            string source =
                "{\r\n" +
                "    \"name\": \"MyCompany.Tools.Editor\",\r\n" +
                "    \"references\": [\r\n" +
                "        \"GUID:fc3fd32eddbee40e39c2d76dc184957b\"\r\n" +
                "    ]\r\n" +
                "}\r\n";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(Encoding.UTF8.GetBytes(result.Content), Is.EqualTo(Encoding.UTF8.GetBytes(source)));
        }
    }
}
