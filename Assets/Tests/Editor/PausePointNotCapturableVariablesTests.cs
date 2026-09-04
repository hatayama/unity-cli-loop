using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the enable-time warning and the registry-to-response path for parameters that
    /// capture cannot box.
    /// </summary>
    [TestFixture]
    public sealed class PausePointNotCapturableVariablesTests
    {
        private const string ByRefEntry = "accumulator (ref/out/in parameter cannot be boxed)";
        private const string RefStructEntry = "scratch (ref struct cannot be boxed)";

        private const string ExpectedWarningForTwoEntries =
            "Parameters not captured because they cannot be boxed: "
            + "accumulator (ref/out/in parameter cannot be boxed), scratch (ref struct cannot be boxed). "
            + "Copy the value it refers to into a plain local (dereference a pointer, ToArray() a span), "
            + "or use --snapshot-timing post-line on the line that consumes it.";

        /// <summary>
        /// What: a non-empty list produces the warning naming every entry with its reason.
        /// </summary>
        [Test]
        public void BuildNotCapturableParametersWarningOrEmpty_WithEntries_NamesThemAndTheWorkaround()
        {
            string warning = PausePointNotCapturableWarnings.BuildNotCapturableParametersWarningOrEmpty(
                new[] { ByRefEntry, RefStructEntry });

            Assert.That(warning, Is.EqualTo(ExpectedWarningForTwoEntries));
        }

        /// <summary>
        /// What: an empty list produces no warning, so a fully capturable method stays quiet.
        /// </summary>
        [Test]
        public void BuildNotCapturableParametersWarningOrEmpty_WithEmptyList_ReturnsEmpty()
        {
            string warning = PausePointNotCapturableWarnings.BuildNotCapturableParametersWarningOrEmpty(
                Array.Empty<string>());

            Assert.That(warning, Is.Empty);
        }

        /// <summary>
        /// What: a null list produces no warning instead of throwing.
        /// </summary>
        [Test]
        public void BuildNotCapturableParametersWarningOrEmpty_WithNull_ReturnsEmpty()
        {
            string warning = PausePointNotCapturableWarnings.BuildNotCapturableParametersWarningOrEmpty(null);

            Assert.That(warning, Is.Empty);
        }

        /// <summary>
        /// What: SetNotCapturableVariables is visible on the next status snapshot.
        /// </summary>
        [Test]
        public void SetNotCapturableVariables_WhenStored_AppearsInStatusSnapshot()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetNotCapturableVariables(id, new[] { ByRefEntry });

                UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);

                Assert.That(snapshot.NotCapturableVariables, Is.EqualTo(new[] { ByRefEntry }));
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: clearing with an empty list drops a previously stored exclusion list, so a
        /// discarded resolution never leaves a stale list behind.
        /// </summary>
        [Test]
        public void SetNotCapturableVariables_WhenClearedWithEmptyList_DropsPreviousEntries()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetNotCapturableVariables(id, new[] { ByRefEntry });
                UloopPausePointRegistry.SetNotCapturableVariables(id, Array.Empty<string>());

                UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);

                Assert.That(snapshot.NotCapturableVariables, Is.Empty);
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: the status response carries the stored entries through FromSnapshot.
        /// </summary>
        [Test]
        public void StatusResponseFromSnapshot_WithStoredEntries_CarriesThem()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetNotCapturableVariables(id, new[] { ByRefEntry });

                PausePointStatusResponse response =
                    PausePointStatusResponse.FromSnapshot(UloopPausePointRegistry.GetStatus(id));

                Assert.That(response.NotCapturableVariables, Is.EqualTo(new[] { ByRefEntry }));
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: with nothing to report the status response omits the field from its JSON, so the
        /// shared contract shape stays unchanged for fully capturable methods.
        /// </summary>
        [Test]
        public void StatusResponseFromSnapshot_WithNoEntries_OmitsFieldFromJson()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);

                PausePointStatusResponse response =
                    PausePointStatusResponse.FromSnapshot(UloopPausePointRegistry.GetStatus(id));
                string json = JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings);

                Assert.That(JObject.Parse(json).ContainsKey("NotCapturableVariables"), Is.False);
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: the enable response carries the stored entries through FromSnapshot.
        /// </summary>
        [Test]
        public void EnableResponseFromSnapshot_WithStoredEntries_CarriesThem()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetNotCapturableVariables(id, new[] { ByRefEntry, RefStructEntry });

                PausePointResponse response =
                    PausePointResponse.FromSnapshot(UloopPausePointRegistry.GetStatus(id));

                Assert.That(
                    response.NotCapturableVariables,
                    Is.EqualTo(new[] { ByRefEntry, RefStructEntry }));
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: with nothing to report the enable response omits the field from its JSON.
        /// </summary>
        [Test]
        public void EnableResponseFromSnapshot_WithNoEntries_OmitsFieldFromJson()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakeNotCapturablePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/Enemy.cs:42";
                UloopPausePointRegistry.Enable(id, 30);

                PausePointResponse response =
                    PausePointResponse.FromSnapshot(UloopPausePointRegistry.GetStatus(id));
                string json = JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings);

                Assert.That(JObject.Parse(json).ContainsKey("NotCapturableVariables"), Is.False);
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        private sealed class FakeNotCapturablePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }
    }
}
