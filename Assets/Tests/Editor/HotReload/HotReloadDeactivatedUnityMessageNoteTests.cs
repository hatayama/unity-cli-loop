using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the sentence the deactivated-added-members warning adds when one of
    /// the deactivated members is a Unity message a proxy was forwarding.
    /// </summary>
    public class HotReloadDeactivatedUnityMessageNoteTests
    {
        private const string FilePath = "Assets/Scripts/Player.cs";
        private const string UpdateKey = "Player.Update()";
        private const string HelperKey = "Player.Helper(string)";

        /// <summary>
        /// What: only the added members whose shims a proxy forwards are collected, by method key.
        /// </summary>
        [Test]
        public void CollectForwardedLabels_KeepsOnlyForwardedUnityMessages()
        {
            MethodInfo forwardedShim = typeof(HotReloadUnityMessageWorkerNamedFixtureShims).GetMethod("Update__shim0");
            MethodInfo ordinaryMethod = typeof(string).GetMethod(nameof(string.IsNullOrEmpty));
            List<HotReloadAddedMemberInfo> members = new List<HotReloadAddedMemberInfo>
            {
                new HotReloadAddedMemberInfo(UpdateKey, FilePath, forwardedShim),
                new HotReloadAddedMemberInfo(HelperKey, FilePath, ordinaryMethod),
                new HotReloadAddedMemberInfo("Player.Pending()", FilePath, null),
            };

            HashSet<string> labels = HotReloadDeactivatedUnityMessageNote.CollectForwardedLabels(members);

            Assert.That(labels, Is.EquivalentTo(new[] { UpdateKey }));
        }

        /// <summary>
        /// What: a deactivated forwarded message gets a sentence that names it and says Unity stops
        /// invoking it, while an ordinary deactivated member is not named there.
        /// </summary>
        [Test]
        public void Describe_WhenAForwardedMessageIsDeactivated_NamesOnlyThatMessage()
        {
            string sentence = HotReloadDeactivatedUnityMessageNote.Describe(
                new List<string> { HelperKey, UpdateKey },
                new HashSet<string> { UpdateKey });

            Assert.That(
                sentence,
                Is.EqualTo(
                    "Unity no longer invokes the deactivated Unity message(s) " + UpdateKey
                    + " on live instances; if the type still adds other forwarded messages, its "
                    + "proxy is rebuilt, so an added Start that remains runs again on each instance."));
        }

        /// <summary>
        /// What: deactivating no forwarded message adds no sentence.
        /// </summary>
        [Test]
        public void Describe_WhenNoForwardedMessageIsDeactivated_ReturnsNull()
        {
            string sentence = HotReloadDeactivatedUnityMessageNote.Describe(
                new List<string> { HelperKey },
                new HashSet<string> { UpdateKey });

            Assert.That(sentence, Is.Null);
        }
    }
}
