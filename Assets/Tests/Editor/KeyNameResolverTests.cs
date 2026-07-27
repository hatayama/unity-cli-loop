#if ULOOP_HAS_INPUT_SYSTEM
using NUnit.Framework;
using UnityEngine.InputSystem;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies key names resolve only through defined Key enum names.
    /// </summary>
    public sealed class KeyNameResolverTests
    {
        /// <summary>
        /// Tests that a defined key name resolves regardless of the casing it was written in.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenADefinedNameInAnyCasing_ResolvesTheKey()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("space");

            Assert.IsTrue(resolved);
            Assert.AreEqual(Key.Space, key);
        }

        /// <summary>
        /// Tests that surrounding whitespace does not stop a defined name from resolving.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenAPaddedName_ResolvesTheKey()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("  W  ");

            Assert.IsTrue(resolved);
            Assert.AreEqual(Key.W, key);
        }

        /// <summary>
        /// Tests that "Return" keeps resolving to Enter, the alias callers already relied on.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenTheReturnAlias_ResolvesEnter()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("Return");

            Assert.IsTrue(resolved);
            Assert.AreEqual(Key.Enter, key);
        }

        /// <summary>
        /// Tests that a bare digit is rejected instead of being read as an enum ordinal.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenAnOrdinal_IsRejected()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("3");

            Assert.IsFalse(resolved);
            Assert.AreEqual(Key.None, key);
        }

        /// <summary>
        /// Tests that a signed ordinal is rejected: Enum.TryParse accepted it as a value too.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenASignedOrdinal_IsRejected()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("-1");

            Assert.IsFalse(resolved);
            Assert.AreEqual(Key.None, key);
        }

        /// <summary>
        /// Tests that an ordinal outside the enum is rejected rather than producing an undefined key.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenAnUndefinedOrdinal_IsRejected()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("300");

            Assert.IsFalse(resolved);
            Assert.AreEqual(Key.None, key);
        }

        /// <summary>
        /// Tests that comma-separated names are rejected instead of being OR-ed into one value.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenCommaSeparatedNames_IsRejected()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("Space,Enter");

            Assert.IsFalse(resolved);
            Assert.AreEqual(Key.None, key);
        }

        /// <summary>
        /// Tests that the placeholder None value is rejected: it names no physical key.
        /// </summary>
        [Test]
        public void Resolve_WhenGivenNone_IsRejected()
        {
            (bool resolved, Key key) = KeyNameResolver.Resolve("None");

            Assert.IsFalse(resolved);
            Assert.AreEqual(Key.None, key);
        }
    }
}
#endif
