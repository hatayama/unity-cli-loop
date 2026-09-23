using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the apply response's Warnings collection keeps line order and decides the
    /// single-compile resolution suffix from how each line is cleared.
    /// </summary>
    public sealed class HotReloadResponseWarningsTests
    {
        /// <summary>
        /// Verifies two lines a compile clears allow the single-compile suffix.
        /// </summary>
        [Test]
        public void AllowsSingleCompileResolution_TwoClearedByCompile_IsTrue()
        {
            HotReloadResponseWarnings warnings = new HotReloadResponseWarnings();

            warnings.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "a", "b" });

            Assert.That(warnings.AllowsSingleCompileResolution, Is.True);
        }

        /// <summary>
        /// Verifies a single line a compile clears does not allow the suffix.
        /// </summary>
        [Test]
        public void AllowsSingleCompileResolution_OneClearedByCompile_IsFalse()
        {
            HotReloadResponseWarnings warnings = new HotReloadResponseWarnings();

            warnings.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "a" });

            Assert.That(warnings.AllowsSingleCompileResolution, Is.False);
        }

        /// <summary>
        /// Verifies one line that needs the caller to act rules the suffix out.
        /// </summary>
        [Test]
        public void AllowsSingleCompileResolution_NeedsCallerActionBesideClearedByCompile_IsFalse()
        {
            HotReloadResponseWarnings warnings = new HotReloadResponseWarnings();

            warnings.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "a", "b" });
            warnings.Add(HotReloadWarningResolution.NeedsCallerAction, new[] { "c" });

            Assert.That(warnings.AllowsSingleCompileResolution, Is.False);
        }

        /// <summary>
        /// Verifies not-counted lines neither allow nor rule out the suffix but still count as warnings.
        /// </summary>
        [Test]
        public void AllowsSingleCompileResolution_NotCountedLines_DoNotChangeTheDecision()
        {
            HotReloadResponseWarnings allowed = new HotReloadResponseWarnings();
            allowed.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "a", "b" });
            allowed.Add(HotReloadWarningResolution.NotCounted, new[] { "hold" });
            HotReloadResponseWarnings notAllowed = new HotReloadResponseWarnings();
            notAllowed.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "a" });
            notAllowed.Add(HotReloadWarningResolution.NotCounted, new[] { "hold-1", "hold-2" });

            Assert.That(allowed.AllowsSingleCompileResolution, Is.True);
            Assert.That(allowed.Count, Is.EqualTo(3));
            Assert.That(notAllowed.AllowsSingleCompileResolution, Is.False);
        }

        /// <summary>
        /// Verifies the lines come out in the order they were added, across kinds.
        /// </summary>
        [Test]
        public void ToList_LinesOfEveryKind_KeepsAddOrder()
        {
            HotReloadResponseWarnings warnings = new HotReloadResponseWarnings();

            warnings.Add(HotReloadWarningResolution.NotCounted, new[] { "hold" });
            warnings.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "compile-1" });
            warnings.Add(HotReloadWarningResolution.NeedsCallerAction, new[] { "caller" });
            warnings.Add(HotReloadWarningResolution.ClearedByCompile, new[] { "compile-2" });
            List<string> lines = warnings.ToList();

            Assert.That(lines, Is.EqualTo(new[] { "hold", "compile-1", "caller", "compile-2" }));
        }
    }
}
