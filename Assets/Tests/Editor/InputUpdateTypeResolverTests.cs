#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using NUnit.Framework;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure unit tests for the gameplay update-type classification shared by press-edge observation and diagnostics.
    /// </summary>
    public sealed class InputUpdateTypeResolverTests
    {
        [TestCase(InputUpdateType.Dynamic)]
        [TestCase(InputUpdateType.Fixed)]
        [TestCase(InputUpdateType.Manual)]
        public void IsGameplayUpdate_WhenGameplayInputUpdateType_ReturnsTrue(InputUpdateType updateType)
        {
            // Verifies every update type gameplay polling can observe a press edge in counts as a gameplay update.
            Assert.That(InputUpdateTypeResolver.IsGameplayUpdate(updateType), Is.True);
        }

        [TestCase(InputUpdateType.Editor)]
        [TestCase(InputUpdateType.None)]
        public void IsGameplayUpdate_WhenNotGameplayInputUpdateType_ReturnsFalse(InputUpdateType updateType)
        {
            // Verifies Editor ticks and the absence of an update are not mistaken for gameplay input updates.
            Assert.That(InputUpdateTypeResolver.IsGameplayUpdate(updateType), Is.False);
        }
    }
}
#endif
