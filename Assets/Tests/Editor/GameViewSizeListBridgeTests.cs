using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which Game View size entries the size list bridge treats as owned, the only guard in
    /// front of removing entries from the user-wide size list.
    /// </summary>
    public sealed class GameViewSizeListBridgeTests
    {
        [Test]
        public void IsOwnedSize_FixedResolutionWithExactBaseText_IsOwned()
        {
            // Verifies a fixed-resolution entry named exactly like the owned base text is claimed.
            Assert.That(GameViewSizeListBridge.IsOwnedSize("uloop", true, "uloop"), Is.True);
        }

        [Test]
        public void IsOwnedSize_BaseTextOnlyStartingWithOwnedName_IsNotOwned()
        {
            // Verifies a user entry whose name merely starts with the owned base text is left alone.
            Assert.That(GameViewSizeListBridge.IsOwnedSize("uloop-phone", true, "uloop"), Is.False);
        }

        [Test]
        public void IsOwnedSize_AspectRatioWithExactBaseText_IsNotOwned()
        {
            // Verifies an aspect-ratio entry is never claimed, because the tool only adds fixed resolutions.
            Assert.That(GameViewSizeListBridge.IsOwnedSize("uloop", false, "uloop"), Is.False);
        }
    }
}
