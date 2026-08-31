using UnityEditor;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the persistent Code Optimization bridge contract while restoring Editor preferences.
    /// </summary>
    [TestFixture]
    public sealed class SetCodeOptimizationBridgeCommandTests
    {
        /// <summary>
        /// Verifies the startup bridge command is registered as an internal command.
        /// </summary>
        [Test]
        public void InternalBridgeCommandRouter_StartupCodeOptimizationCommand_IsInternal()
        {
            bool isInternal = InternalBridgeCommandRouter.IsInternalCommand(
                UnityCliLoopConstants.COMMAND_NAME_SET_CODE_OPTIMIZATION_DEBUG_STARTUP);

            Assert.That(isInternal, Is.True);
        }

        /// <summary>
        /// Verifies startup Debug persistence reports the previous value and confirms its readback.
        /// </summary>
        [Test]
        public void PersistStartupDebugPreference_PersistsAndReportsVerifiedDebug()
        {
            const string editorPrefsKey = "ScriptDebugInfoEnabled";
            bool hadOriginalValue = EditorPrefs.HasKey(editorPrefsKey);
            bool originalValue = EditorPrefs.GetBool(editorPrefsKey, false);

            try
            {
                EditorPrefs.SetBool(editorPrefsKey, false);

                StartupDebugPreferenceResult result =
                    SetCodeOptimizationDebugStartupBridgeCommand.PersistStartupDebugPreference();

                Assert.That(result.Previous, Is.False);
                Assert.That(result.Current, Is.True);
                Assert.That(result.Verified, Is.True);
                Assert.That(EditorPrefs.GetBool(editorPrefsKey, false), Is.True);
            }
            finally
            {
                if (hadOriginalValue)
                {
                    EditorPrefs.SetBool(editorPrefsKey, originalValue);
                }
                else
                {
                    EditorPrefs.DeleteKey(editorPrefsKey);
                }
            }
        }

        /// <summary>
        /// Verifies the Release recovery action ends with the exact approved persistence guidance.
        /// </summary>
        [Test]
        public void ReleaseCodeOptimizationRecommendedNextAction_EndsWithApprovedPersistenceGuidance()
        {
            const string expectedSuffix =
                "To make Unity start in Debug permanently, run: uloop set-code-optimization debug --startup "
                + "(machine-wide: applies to every Unity project on this machine; only your project's C# script "
                + "execution slows down, mainly during Play Mode - the Unity Editor itself is not slowed).";

            Assert.That(
                SourcePausePointConstants.ReleaseCodeOptimizationRecommendedNextAction,
                Does.EndWith(expectedSuffix));
        }
    }
}
