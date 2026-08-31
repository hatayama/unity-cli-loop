using System;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that the Script Updating Consent Harmony prefix is installed on DisplayDialogComplex
    /// and that Prefix records a decline. Does not invoke DisplayDialogComplex itself — that
    /// would freeze the Editor in a modal loop.
    /// </summary>
    public sealed class CompileApiUpdaterConsentPatchWiringTests
    {
        [TearDown]
        public void TearDown()
        {
            CompileApiUpdaterConsentState.EndCliCompile();
        }

        /// <summary>
        /// What: EditorUtility.DisplayDialogComplex has the compile consent prefix after startup.
        /// </summary>
        [Test]
        public void DisplayDialogComplex_AfterStartup_HasCompileConsentPrefix()
        {
            MethodInfo original = typeof(EditorUtility).GetMethod(
                nameof(EditorUtility.DisplayDialogComplex),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) },
                modifiers: null);

            Assert.That(original, Is.Not.Null);
            Patches patchInfo = Harmony.GetPatchInfo(original);
            Assert.That(patchInfo, Is.Not.Null);
            Patch[] ownedPrefixes = patchInfo.Prefixes
                .Where(patch => patch.owner == "io.github.hatayama.uloop.compile-api-updater-consent")
                .ToArray();
            Assert.That(ownedPrefixes.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: Prefix declines Script Updating Consent while a CLI compile is in flight and
        /// records that decline onto the compile result.
        /// </summary>
        [Test]
        public void Prefix_WhenCliCompileIsInFlight_DeclinesAndRecordsConsent()
        {
            MethodInfo prefix = typeof(CompileApiUpdaterConsentPatcher).GetMethod(
                "Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(prefix, Is.Not.Null);

            CompileApiUpdaterConsentState.BeginCliCompile();
            object[] arguments = { "Script Updating Consent", 0 };
            object invokeResult = prefix.Invoke(null, arguments);

            Assert.That(invokeResult, Is.EqualTo(false));
            Assert.That(arguments[1], Is.EqualTo(1));

            CompileResult attached = CompileApiUpdaterConsentState.AttachDeclined(
                new CompileResult(
                    success: true,
                    errorCount: 0,
                    warningCount: 0,
                    completedAt: DateTime.Now,
                    messages: Array.Empty<CompilerMessage>(),
                    errors: Array.Empty<CompilerMessage>(),
                    warnings: Array.Empty<CompilerMessage>()));
            Assert.That(attached.ApiUpdaterConsentDeclined, Is.True);
        }
    }
}
