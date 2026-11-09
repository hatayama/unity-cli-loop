using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies finished EditMode test runs are released and everything else is left alone.
    /// </summary>
    public class RunTestsFinishedRunReleaserTests
    {
        private readonly List<ScriptableObject> _createdObjects = new();
        private List<AssemblyReloadEvents.AssemblyReloadCallback> _unsubscribed;
        private RunTestsFinishedRunReleaser _releaser;

        [SetUp]
        public void SetUp()
        {
            FakeEditModeTestCallbacks.RestoringTestContext = null;
            _unsubscribed = new List<AssemblyReloadEvents.AssemblyReloadCallback>();
            _releaser = new RunTestsFinishedRunReleaser(
                typeof(FakeEditModeTestCallbacks).GetProperty(nameof(FakeEditModeTestCallbacks.RestoringTestContext)),
                _unsubscribed.Add);
        }

        [TearDown]
        public void TearDown()
        {
            FakeEditModeTestCallbacks.RestoringTestContext = null;
            foreach (ScriptableObject createdObject in _createdObjects)
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }
        }

        /// <summary>
        /// Verifies a finished run leaves both events, because a run still on either one stays reachable.
        /// </summary>
        [Test]
        public void Release_RemovesFinishedRunFromBothEvents()
        {
            FakeEditModeRunner runner = CreateFinishedRunner();

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext, Is.Null);
            Assert.That(_unsubscribed, Has.Count.EqualTo(1));
            Assert.That(_unsubscribed[0].Target, Is.SameAs(runner));
            Assert.That(_unsubscribed[0].Method.Name, Is.EqualTo("OnBeforeAssemblyReload"));
        }

        /// <summary>
        /// Verifies a run still in progress keeps both subscriptions, because it needs them to finish.
        /// </summary>
        [Test]
        public void Release_LeavesRunInProgress()
        {
            FakeEditModeRunner runner = CreateRunner();

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext.Target, Is.SameAs(runner));
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies only the finished runs leave when finished runs and a run in progress are subscribed together.
        /// </summary>
        [Test]
        public void Release_WithBothKinds_KeepsOnlyTheRunInProgress()
        {
            CreateFinishedRunner();
            FakeEditModeRunner runInProgress = CreateRunner();
            CreateFinishedRunner();

            _releaser.Release();

            Delegate[] remaining = FakeEditModeTestCallbacks.RestoringTestContext.GetInvocationList();
            Assert.That(remaining, Has.Length.EqualTo(1));
            Assert.That(remaining[0].Target, Is.SameAs(runInProgress));
            Assert.That(_unsubscribed, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies a handler whose target is not a Unity object is left alone, since it cannot be a finished run.
        /// </summary>
        [Test]
        public void Release_LeavesHandlersOfOtherOwners()
        {
            PlainHandlerOwner owner = new();
            FakeEditModeTestCallbacks.RestoringTestContext += owner.OnRestoringTest;

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext.Target, Is.SameAs(owner));
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies a static handler is left alone. It has no target to ask about, and reading one would throw
        /// while a finished run is reporting its result.
        /// </summary>
        [Test]
        public void Release_LeavesStaticHandlers()
        {
            FakeEditModeTestCallbacks.RestoringTestContext += OnRestoringTestStatic;

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext, Is.Not.Null);
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies a runner whose reload handler gains an overload is still released. A lookup by name alone
        /// throws on that, and the throw would cost a finished run its result.
        /// </summary>
        [Test]
        public void Release_RemovesFinishedRunWhoseReloadHandlerHasAnOverload()
        {
            FakeOverloadedEditModeRunner runner = ScriptableObject.CreateInstance<FakeOverloadedEditModeRunner>();
            FakeEditModeTestCallbacks.RestoringTestContext += runner.OnRestoringTest;
            UnityEngine.Object.DestroyImmediate(runner);

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext, Is.Null);
            Assert.That(_unsubscribed, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies a destroyed object with no reload handler stays subscribed, because removing one of the two
        /// subscriptions would release nothing and the object is not the runner this was written for.
        /// </summary>
        [Test]
        public void Release_LeavesDestroyedObjectWithoutReloadHandler()
        {
            DestroyedObjectWithoutReloadHandler destroyedObject =
                ScriptableObject.CreateInstance<DestroyedObjectWithoutReloadHandler>();
            FakeEditModeTestCallbacks.RestoringTestContext += destroyedObject.OnRestoringTest;
            UnityEngine.Object.DestroyImmediate(destroyedObject);

            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext.Target, Is.SameAs(destroyedObject));
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies having no subscribers is not an error, as after a Domain Reload.
        /// </summary>
        [Test]
        public void Release_WithNoHandlers_ChangesNothing()
        {
            _releaser.Release();

            Assert.That(FakeEditModeTestCallbacks.RestoringTestContext, Is.Null);
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies a Test Framework version without the event only keeps the memory, and does not break the
        /// test run that is reporting its result.
        /// </summary>
        [Test]
        public void Release_WhenTheEventIsMissing_DoesNothing()
        {
            RunTestsFinishedRunReleaser releaser = new(null, _unsubscribed.Add);

            Assert.DoesNotThrow(releaser.Release);
            Assert.That(_unsubscribed, Is.Empty);
        }

        /// <summary>
        /// Verifies a property that cannot be written is not treated as the event, because the handlers could
        /// not be removed from it.
        /// </summary>
        [Test]
        public void IsUsableEventProperty_RefusesPropertyThatCannotBeWritten()
        {
            PropertyInfo readOnlyProperty = typeof(FakeReadOnlyCallbacks).GetProperty(
                nameof(FakeReadOnlyCallbacks.RestoringTestContext));

            Assert.That(RunTestsFinishedRunReleaser.IsUsableEventProperty(readOnlyProperty), Is.False);
        }

        /// <summary>
        /// Verifies the installed Test Framework still has both members this relies on by name, so an upgrade that
        /// renames them fails here instead of silently bringing the memory growth back.
        /// </summary>
        [Test]
        public void InstalledTestFramework_HasTheMembersReleaseReliesOn()
        {
            PropertyInfo property = RunTestsFinishedRunReleaser.FindRestoringTestContextProperty();
            Type runnerType = Type.GetType(
                "UnityEditor.TestTools.TestRunner.EditModeRunner, UnityEditor.TestRunner",
                throwOnError: false);

            Assert.That(property, Is.Not.Null);
            Assert.That(RunTestsFinishedRunReleaser.IsUsableEventProperty(property), Is.True);
            Assert.That(runnerType, Is.Not.Null);
            Assert.That(RunTestsFinishedRunReleaser.FindBeforeAssemblyReloadMethod(runnerType), Is.Not.Null);
        }

        private static void OnRestoringTestStatic()
        {
        }

        private FakeEditModeRunner CreateRunner()
        {
            FakeEditModeRunner runner = ScriptableObject.CreateInstance<FakeEditModeRunner>();
            _createdObjects.Add(runner);
            FakeEditModeTestCallbacks.RestoringTestContext += runner.OnRestoringTest;
            return runner;
        }

        private FakeEditModeRunner CreateFinishedRunner()
        {
            FakeEditModeRunner runner = CreateRunner();
            UnityEngine.Object.DestroyImmediate(runner);
            return runner;
        }
    }

    /// <summary>
    /// Stand-in for the Test Framework's internal class that holds the static event.
    /// </summary>
    internal static class FakeEditModeTestCallbacks
    {
        public static Action RestoringTestContext { get; set; }
    }

    /// <summary>
    /// Stand-in for a Test Framework version whose event cannot be assigned.
    /// </summary>
    internal static class FakeReadOnlyCallbacks
    {
        public static Action RestoringTestContext { get; }
    }

    /// <summary>
    /// Stand-in for the Test Framework's runner, which a finished run destroys while staying subscribed.
    /// </summary>
    internal sealed class FakeEditModeRunner : ScriptableObject
    {
        public void OnRestoringTest()
        {
        }

        private void OnBeforeAssemblyReload()
        {
        }
    }

    /// <summary>
    /// Stand-in for a Test Framework version whose runner adds an overload of the reload handler.
    /// </summary>
    internal sealed class FakeOverloadedEditModeRunner : ScriptableObject
    {
        public void OnRestoringTest()
        {
        }

        private void OnBeforeAssemblyReload()
        {
        }

        private void OnBeforeAssemblyReload(bool force)
        {
        }
    }

    /// <summary>
    /// A destroyed Unity object that has no reload handler, so it is not the runner.
    /// </summary>
    internal sealed class DestroyedObjectWithoutReloadHandler : ScriptableObject
    {
        public void OnRestoringTest()
        {
        }
    }

    /// <summary>
    /// A subscriber that is not a Unity object. It has the reload handler, so only that keeps it subscribed.
    /// </summary>
    internal sealed class PlainHandlerOwner
    {
        public void OnRestoringTest()
        {
        }

        private void OnBeforeAssemblyReload()
        {
        }
    }
}
