using System.Linq;
using System.Reflection;

using HarmonyLib;

using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that the Script Updating Consent Harmony prefix is installed on DisplayDialogComplex.
    /// Does not invoke DisplayDialogComplex itself — that would freeze the Editor in a modal loop.
    /// </summary>
    public sealed class CompileApiUpdaterConsentPatchWiringTests
    {
        /// <summary>
        /// What: EditorUtility.DisplayDialogComplex has the compile consent prefix after startup.
        /// </summary>
        [Test]
        public void DisplayDialogComplex_AfterStartup_HasCompileConsentPrefix()
        {
            CompileEditorStartup.Initialize();
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
    }
}
