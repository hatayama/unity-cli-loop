using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies title matching modes against a probe window that is never shown.
    /// </summary>
    public sealed class EditorWindowFinderTests
    {
        private const string ProbeTitle = "UloopFinderProbe Alpha";

        private EditorWindowFinderProbeWindow _probe;

        [SetUp]
        public void SetUp()
        {
            // Not shown on purpose: Resources.FindObjectsOfTypeAll also enumerates hidden instances,
            // so the matching modes can be exercised without disturbing the Editor layout.
            _probe = ScriptableObject.CreateInstance<EditorWindowFinderProbeWindow>();
            _probe.titleContent = new GUIContent(ProbeTitle);
        }

        [TearDown]
        public void TearDown()
        {
            if (_probe == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(_probe);
            _probe = null;
        }

        /// <summary>
        /// What: exact matching accepts the full title regardless of letter case.
        /// </summary>
        [Test]
        public void FindWindowsByName_Exact_MatchesFullTitleCaseInsensitively()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "uloopfinderprobe alpha",
                WindowMatchMode.exact);

            Assert.That(Array.IndexOf(result, _probe), Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// What: exact matching rejects a title prefix, so it is not silently a prefix match.
        /// </summary>
        [Test]
        public void FindWindowsByName_Exact_DoesNotMatchPrefix()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "UloopFinderProbe",
                WindowMatchMode.exact);

            Assert.That(Array.IndexOf(result, _probe), Is.LessThan(0));
        }

        /// <summary>
        /// What: prefix matching accepts the start of the title.
        /// </summary>
        [Test]
        public void FindWindowsByName_Prefix_MatchesTitleStart()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "uloopfinderprobe",
                WindowMatchMode.prefix);

            Assert.That(Array.IndexOf(result, _probe), Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// What: contains matching accepts a fragment from the middle of the title.
        /// </summary>
        [Test]
        public void FindWindowsByName_Contains_MatchesTitleMiddle()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "probe alp",
                WindowMatchMode.contains);

            Assert.That(Array.IndexOf(result, _probe), Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// What: prefix matching rejects a fragment from the middle, so it is not a contains match.
        /// </summary>
        [Test]
        public void FindWindowsByName_Prefix_DoesNotMatchTitleMiddle()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "probe alp",
                WindowMatchMode.prefix);

            Assert.That(Array.IndexOf(result, _probe), Is.LessThan(0));
        }

        /// <summary>
        /// What: an empty name matches nothing instead of every open window.
        /// </summary>
        [Test]
        public void FindWindowsByName_EmptyName_ReturnsEmpty()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName("", WindowMatchMode.contains);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// What: a title that no window carries matches nothing.
        /// </summary>
        [Test]
        public void FindWindowsByName_UnknownName_ReturnsEmpty()
        {
            EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                "NoSuchWindow-uloop-test",
                WindowMatchMode.exact);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// What: the open-window name list includes the probe title used by the matching tests.
        /// </summary>
        [Test]
        public void GetOpenWindowNames_ContainsProbeTitle()
        {
            string[] names = EditorWindowFinder.GetOpenWindowNames();

            Assert.That(names, Does.Contain(ProbeTitle));
        }

        /// <summary>
        /// What: a window whose title text is null is skipped instead of throwing.
        /// </summary>
        [Test]
        public void FindWindowsByName_WhenAWindowHasNullTitleText_DoesNotThrow()
        {
            EditorWindowFinderProbeWindow nullTitled =
                ScriptableObject.CreateInstance<EditorWindowFinderProbeWindow>();
            nullTitled.titleContent = new GUIContent { text = null };
            try
            {
                EditorWindow[] result = EditorWindowFinder.FindWindowsByName(
                    "uloopfinderprobe",
                    WindowMatchMode.prefix);

                Assert.That(Array.IndexOf(result, nullTitled), Is.LessThan(0));
                Assert.That(Array.IndexOf(result, _probe), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(nullTitled);
            }
        }

        /// <summary>
        /// Titled placeholder window used only to exercise the matching modes.
        /// </summary>
        private sealed class EditorWindowFinderProbeWindow : EditorWindow
        {
        }
    }
}
