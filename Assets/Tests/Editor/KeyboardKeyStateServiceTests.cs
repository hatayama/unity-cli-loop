#if ULOOP_HAS_INPUT_SYSTEM
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure bookkeeping tests for KeyboardKeyStateService — no Input System updates or Editor pause.
    /// </summary>
    public class KeyboardKeyStateServiceTests
    {
        /// <summary>
        /// Verifies mixed held + transient bookkeeping is fully drained without touching the device.
        /// </summary>
        [Test]
        public void ClearTrackedKeys_WhenHeldAndTransientMixed_ReturnsAllKeysAndClearsHeldState()
        {
            KeyboardKeyStateService service = new KeyboardKeyStateService();
            service.SetKeyDown(Key.W);
            service.SetKeyDown(Key.LeftShift);
            service.RegisterTransientKey(Key.Space);

            IReadOnlyList<Key> released = service.ClearTrackedKeys();

            Assert.That(released, Is.EquivalentTo(new[] { Key.W, Key.LeftShift, Key.Space }));
            Assert.That(service.IsKeyHeld(Key.W), Is.False);
            Assert.That(service.IsKeyHeld(Key.LeftShift), Is.False);
            Assert.That(service.IsKeyHeld(Key.Space), Is.False);
            Assert.That(service.HeldKeys, Is.Empty);
        }

        /// <summary>
        /// Verifies a no-op drain returns an empty list rather than null.
        /// </summary>
        [Test]
        public void ClearTrackedKeys_WhenEmpty_ReturnsEmptyList()
        {
            KeyboardKeyStateService service = new KeyboardKeyStateService();

            IReadOnlyList<Key> released = service.ClearTrackedKeys();

            Assert.That(released, Is.Empty);
        }
    }
}
#endif
