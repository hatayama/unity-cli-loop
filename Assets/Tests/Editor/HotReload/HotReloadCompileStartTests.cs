using System;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the compile start the snapshot capture checks sources from: which time
    /// it answers for a DLL, and how the time is recorded across a compile's domain reload.
    /// </summary>
    public sealed class HotReloadCompileStartTests
    {
        private string _savedRecord;

        [SetUp]
        public void SetUp()
        {
            _savedRecord = SessionState.GetString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_savedRecord == null)
            {
                SessionState.EraseString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey);
                return;
            }

            SessionState.SetString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey, _savedRecord);
        }

        /// <summary>
        /// Verifies that a compile start recorded before the DLL was written is where the check starts.
        /// </summary>
        [Test]
        public void SuspectWritesFrom_WhenTheRecordedStartIsBeforeTheDllWrite_ReturnsTheStart()
        {
            Assert.That(HotReloadCompileStart.At(100).SuspectWritesFrom(200), Is.EqualTo(100));
        }

        /// <summary>
        /// Verifies that a compile start recorded after the DLL was written, as a later compile that
        /// did not rewrite the DLL leaves it, never moves the check past the DLL write.
        /// </summary>
        [Test]
        public void SuspectWritesFrom_WhenTheRecordedStartIsAfterTheDllWrite_ReturnsTheDllWrite()
        {
            Assert.That(HotReloadCompileStart.At(300).SuspectWritesFrom(200), Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that without a recorded start the check starts at the DLL write.
        /// </summary>
        [Test]
        public void SuspectWritesFrom_WhenUnknown_ReturnsTheDllWrite()
        {
            Assert.That(HotReloadCompileStart.Unknown.SuspectWritesFrom(200), Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that a recorded start reads back.
        /// </summary>
        [Test]
        public void Read_AfterRecord_ReturnsThatStart()
        {
            HotReloadCompileStartRecord.Record(123);

            Assert.That(HotReloadCompileStartRecord.Read().SuspectWritesFrom(long.MaxValue), Is.EqualTo(123));
        }

        /// <summary>
        /// Verifies that a stored value that is not a number reads as no recorded start.
        /// </summary>
        [Test]
        public void Read_WhenTheStoredValueIsNotANumber_ReturnsUnknown()
        {
            SessionState.SetString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey, "x");

            Assert.That(HotReloadCompileStartRecord.Read().SuspectWritesFrom(200), Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that Initialize subscribes the recorder to compile start.
        /// </summary>
        [Test]
        public void Initialize_SubscribesTheRecorderToCompilationStarted()
        {
            HotReloadCompileStartRecord.Initialize();

            Assert.That(
                StaticEventHasHandler(typeof(CompilationPipeline), IsTheRecorder),
                Is.True,
                "HotReloadCompileStartRecord.OnCompilationStarted must be subscribed on CompilationPipeline.");
        }

        // Why the declaring type as well as the name: HotReloadCompilationAssemblies subscribes a
        // handler of the same name to the same event.
        private static bool IsTheRecorder(Delegate listener)
        {
            MethodInfo listenerMethod = listener.Method;
            return listenerMethod.DeclaringType == typeof(HotReloadCompileStartRecord)
                && listenerMethod.Name == nameof(HotReloadCompileStartRecord.OnCompilationStarted);
        }

        // Why scan the fields: CompilationPipeline keeps its event handlers in static Delegate fields
        // that no public member lists.
        private static bool StaticEventHasHandler(Type eventOwner, Func<Delegate, bool> isMatch)
        {
            FieldInfo[] fields = eventOwner.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                Delegate current = fields[index].GetValue(null) as Delegate;
                if (current != null && InvocationListContains(current, isMatch))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InvocationListContains(Delegate current, Func<Delegate, bool> isMatch)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int listenerIndex = 0; listenerIndex < listeners.Length; listenerIndex++)
            {
                if (isMatch(listeners[listenerIndex]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
