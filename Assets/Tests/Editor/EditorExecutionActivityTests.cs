using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the registry that holds an operating-system activity while a command runs: every activity it
    /// starts ends exactly once, it starts none after closing, and commands still run when none can start.
    /// </summary>
    public sealed class EditorExecutionActivityTests
    {
        private const string MissingEntryPointMessage = "native entry point missing in this test";

        /// <summary>
        /// Verifies one hold starts one activity and disposing the hold ends that same token.
        /// </summary>
        [Test]
        public void Hold_ThenDispose_BeginsOneActivityAndEndsThatToken()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);

            IDisposable hold = activity.Hold();

            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.LiveCount, Is.EqualTo(1));

            hold.Dispose();

            Assert.That(api.EndedTokens, Is.EqualTo(new[] { new IntPtr(1) }));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies disposing the same hold twice ends its activity only once.
        /// </summary>
        [Test]
        public void Dispose_CalledTwice_EndsTheActivityOnce()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable hold = activity.Hold();

            hold.Dispose();
            hold.Dispose();

            Assert.That(api.EndedTokens, Is.EqualTo(new[] { new IntPtr(1) }));
        }

        /// <summary>
        /// Verifies two overlapping holds each end their own token once, whichever is released first.
        /// </summary>
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void Hold_TwiceOverlapping_EndsEachTokenOnceInEitherOrder(int releasedFirst, int releasedSecond)
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable[] holds = { activity.Hold(), activity.Hold() };

            Assert.That(api.LiveCount, Is.EqualTo(2));

            holds[releasedFirst].Dispose();
            holds[releasedSecond].Dispose();

            // Tokens count up from 1, so the hold at index i owns token i + 1.
            Assert.That(
                api.EndedTokens,
                Is.EqualTo(new[] { new IntPtr(releasedFirst + 1), new IntPtr(releasedSecond + 1) }));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies closing ends every live activity, and disposing those holds afterwards ends nothing more.
        /// </summary>
        [Test]
        public void ReleaseAllAndClose_EndsEveryLiveActivity_AndALaterDisposeEndsNothing()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable first = activity.Hold();
            IDisposable second = activity.Hold();

            activity.ReleaseAllAndClose();

            Assert.That(api.EndedTokens, Is.EquivalentTo(new[] { new IntPtr(1), new IntPtr(2) }));
            Assert.That(api.LiveCount, Is.EqualTo(0));

            first.Dispose();
            second.Dispose();

            Assert.That(api.EndedTokens.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a hold requested after the registry closed starts no activity, while one requested
        /// before closing did.
        /// </summary>
        [Test]
        public void Hold_AfterReleaseAllAndClose_DoesNotBeginAnotherActivity()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable earlyHold = activity.Hold();

            Assert.That(api.BeginCount, Is.EqualTo(1));

            activity.ReleaseAllAndClose();
            IDisposable lateHold = activity.Hold();

            Assert.That(api.BeginCount, Is.EqualTo(1));

            lateHold.Dispose();
            earlyHold.Dispose();
        }

        /// <summary>
        /// Verifies a platform without the native entry points still lets commands run: the hold does not
        /// throw, one warning is logged, and later holds do not try the native call again.
        /// </summary>
        [TestCase(typeof(EntryPointNotFoundException))]
        [TestCase(typeof(DllNotFoundException))]
        public void Hold_WhenThePlatformLacksTheNativeEntryPoints_RunsWithoutAnActivity_AndDoesNotTryAgain(
            Type exceptionType)
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            api.BeginException = (Exception)Activator.CreateInstance(exceptionType, MissingEntryPointMessage);
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            LogAssert.Expect(LogType.Warning, new Regex("throttling.*" + MissingEntryPointMessage));

            IDisposable first = activity.Hold();
            IDisposable second = activity.Hold();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);

            first.Dispose();
            second.Dispose();

            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.EndedTokens, Is.Empty);
        }

        /// <summary>
        /// Verifies a failing End reaches the caller and is never repeated for the same token, neither by
        /// a second dispose nor by closing the registry.
        /// </summary>
        [Test]
        public void Dispose_WhenEndThrows_DoesNotEndTheSameTokenAgain()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable hold = activity.Hold();
            api.ThrowOnNextEnd = true;

            Assert.Throws<InvalidOperationException>(() => hold.Dispose());

            hold.Dispose();
            activity.ReleaseAllAndClose();

            Assert.That(api.EndedTokens, Is.EqualTo(new[] { new IntPtr(1) }));
        }

        /// <summary>
        /// Verifies a failing End while closing is logged instead of thrown, the other token is still ended,
        /// and disposing the holds afterwards ends no token a second time.
        /// </summary>
        [Test]
        public void ReleaseAllAndClose_WhenEndThrows_StillEndsTheOthers_AndEndsNoTokenTwice()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            IDisposable first = activity.Hold();
            IDisposable second = activity.Hold();
            api.ThrowOnNextEnd = true;
            LogAssert.Expect(LogType.Exception, new Regex(RecordingProcessActivityApi.EndFailureMessage));

            Assert.DoesNotThrow(() => activity.ReleaseAllAndClose());
            Assert.That(api.EndedTokens, Is.EquivalentTo(new[] { new IntPtr(1), new IntPtr(2) }));

            first.Dispose();
            second.Dispose();

            Assert.That(api.EndedTokens.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that when the platform starts no activity, the hold is still a usable handle and
        /// disposing it ends nothing.
        /// </summary>
        [Test]
        public void Hold_WhenThePlatformReturnsNoToken_ReturnsAHandleThatEndsNothing()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            api.ReturnsNoToken = true;
            EditorExecutionActivity activity = new EditorExecutionActivity(api);

            IDisposable hold = activity.Hold();

            Assert.That(hold, Is.Not.Null);

            hold.Dispose();

            Assert.That(api.EndedTokens, Is.Empty);
        }

        /// <summary>
        /// Verifies the API used off macOS never starts an activity.
        /// </summary>
        [Test]
        public void InertProcessActivityApi_Begin_ReturnsNoToken()
        {
            InertProcessActivityApi api = new InertProcessActivityApi();

            Assert.That(api.Begin("test reason"), Is.EqualTo(IntPtr.Zero));
        }
    }
}
