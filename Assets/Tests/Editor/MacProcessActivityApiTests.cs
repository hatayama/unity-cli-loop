using System;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the macOS process activity adapter against the real operating system calls.
    /// </summary>
    public sealed class MacProcessActivityApiTests
    {
        /// <summary>
        /// Verifies that on macOS the real calls start an activity with a non-zero token and end it without
        /// throwing, three times in a row. Other platforms skip the test.
        /// </summary>
        [Test]
        public void BeginThenEnd_OnMacOS_ReturnsATokenAndDoesNotThrow()
        {
            // Why a runtime check instead of a platform attribute: this repository has no precedent for
            // narrowing EditMode tests by platform attribute, and how it behaves there is unverified.
            if (UnityEngine.Application.platform != RuntimePlatform.OSXEditor)
            {
                Assert.Ignore("macOS only");
            }

            MacProcessActivityApi api = new MacProcessActivityApi();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr token = api.Begin("Unity CLI Loop activity test");

                Assert.That(token, Is.Not.EqualTo(IntPtr.Zero));
                Assert.DoesNotThrow(() => api.End(token));
            }
        }

        /// <summary>
        /// Verifies ending with no token is rejected before any native call, so it holds on every platform.
        /// </summary>
        [Test]
        public void End_WithNoToken_Throws()
        {
            MacProcessActivityApi api = new MacProcessActivityApi();

            Assert.Throws<ArgumentException>(() => api.End(IntPtr.Zero));
        }
    }
}
