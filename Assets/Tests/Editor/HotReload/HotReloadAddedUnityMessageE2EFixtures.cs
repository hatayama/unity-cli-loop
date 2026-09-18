using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    // Why the MonoBehaviour is not the type this file is named after: Unity binds the file's
    // MonoScript to the class whose name matches the file, and refuses AddComponent for a
    // MonoScript that lives under an Editor folder. Naming the file after the companion type below
    // leaves the MonoBehaviour without a MonoScript of its own, which is what lets a test put it on
    // a GameObject.

    /// <summary>
    /// A compiled MonoBehaviour with no Unity message of its own. An end-to-end test adds one to a
    /// copy of this source and expects the run to forward it to the instances in the open scene.
    /// </summary>
    public sealed class HotReloadAddedUnityMessageFixture : MonoBehaviour
    {
        public int Counter;
    }

    /// <summary>
    /// What an end-to-end test needs to know about this file's text, kept beside the declaration it
    /// describes so an edit here cannot silently break the test's insertion point.
    /// </summary>
    public static class HotReloadAddedUnityMessageE2EFixtures
    {
        /// <summary>The counter field's line, which added members are inserted after.</summary>
        // Why built from nameof rather than written out: a literal copy of the line would be a
        // second occurrence of the anchor in this very file, and the test's insertion would land in
        // both places and break the source it was about to reload.
        public const string CounterFieldDeclaration =
            "        public int " + nameof(HotReloadAddedUnityMessageFixture.Counter) + ";";
    }
}
