using System;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how a generated shim method is classified: a Unity message a proxy forwards, a Unity
    /// message that needs a compile, or an ordinary added method.
    /// </summary>
    public class HotReloadUnityMessageDetectorTests
    {
        /// <summary>
        /// What: a void message on the forwarded list, taking the worker's receiver parameter, is
        /// forwarded and reports the receiver type.
        /// </summary>
        [Test]
        public void Classify_ForwardedMessageOnMonoBehaviour_IsForwarded()
        {
            MethodInfo shim = ShimOf(typeof(ForwardedShims), "Update");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.Forwarded));
            Assert.That(targetType, Is.EqualTo(typeof(DetectorFixture)));
        }

        /// <summary>
        /// What: a lifecycle message the proxy cannot mirror is still recognized as a Unity message,
        /// so the caller can say it needs a compile.
        /// </summary>
        [Test]
        public void Classify_LifecycleMessage_IsNotForwardedButKnown()
        {
            MethodInfo shim = ShimOf(typeof(ForwardedShims), "Awake");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotForwarded));
            Assert.That(targetType, Is.EqualTo(typeof(DetectorFixture)));
        }

        /// <summary>
        /// What: a static user method that happens to take its own MonoBehaviour as the first
        /// argument is not a shim for an instance method, so it is not a Unity message.
        /// </summary>
        [Test]
        public void Classify_FirstParameterNotTheReceiver_IsNotAUnityMessage()
        {
            MethodInfo shim = ShimOf(typeof(MisnamedReceiverShims), "Update");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotUnityMessage));
            Assert.That(targetType, Is.Null);
        }

        /// <summary>
        /// What: an ordinary added instance method keeps being an ordinary added method.
        /// </summary>
        [Test]
        public void Classify_OrdinaryMethodName_IsNotAUnityMessage()
        {
            MethodInfo shim = ShimOf(typeof(ForwardedShims), "Helper");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotUnityMessage));
            Assert.That(targetType, Is.Null);
        }

        /// <summary>
        /// What: a shim for a static method carries no receiver parameter, so it is not forwarded.
        /// </summary>
        [Test]
        public void Classify_NoParameters_IsNotAUnityMessage()
        {
            MethodInfo shim = ShimOf(typeof(ForwardedShims), "LateUpdate");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotUnityMessage));
            Assert.That(targetType, Is.Null);
        }

        /// <summary>
        /// What: a receiver that does not derive from MonoBehaviour never receives Unity messages.
        /// </summary>
        [Test]
        public void Classify_ReceiverIsNotAMonoBehaviour_IsNotAUnityMessage()
        {
            MethodInfo shim = ShimOf(typeof(PlainReceiverShims), "Update");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotUnityMessage));
            Assert.That(targetType, Is.Null);
        }

        /// <summary>
        /// What: a message that returns a value cannot be forwarded through a void proxy method,
        /// but it is still a Unity message the next compile will dispatch.
        /// </summary>
        [Test]
        public void Classify_MessageReturningAValue_IsNotForwarded()
        {
            MethodInfo shim = ShimOf(typeof(ReturningShims), "Update");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotForwarded));
            Assert.That(targetType, Is.EqualTo(typeof(DetectorFixture)));
        }

        /// <summary>
        /// What: a message whose argument is passed by reference cannot travel through the proxy's
        /// object array, so it is reported as a Unity message that is not forwarded.
        /// </summary>
        [Test]
        public void Classify_MessageWithAByRefParameter_IsNotForwarded()
        {
            MethodInfo shim = ShimOf(typeof(ByRefShims), "OnTriggerEnter");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotForwarded));
            Assert.That(targetType, Is.EqualTo(typeof(DetectorFixture)));
        }

        /// <summary>
        /// What: a shim named the way the worker actually names it — the member plus the marker and
        /// a number — is classified by the member's name, so the feature works on a real run.
        /// </summary>
        [Test]
        public void Classify_ShimNamedAsTheWorkerNamesIt_IsForwarded()
        {
            MethodInfo shim = ShimOf(typeof(WorkerNamedShims), "Update__shim0");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.Forwarded));
            Assert.That(targetType, Is.EqualTo(typeof(DetectorFixture)));
        }

        /// <summary>
        /// What: an added method whose own name ends in the marker followed by a number keeps that
        /// name once the shim's own suffix is off, so it is still not a Unity message.
        /// </summary>
        [Test]
        public void Classify_MemberNameThatItselfEndsInTheMarker_IsNotAUnityMessage()
        {
            MethodInfo shim = ShimOf(typeof(WorkerNamedShims), "Update__shim0__shim1");

            HotReloadUnityMessageDetector.Classification classification =
                HotReloadUnityMessageDetector.Classify(shim, out Type targetType);

            Assert.That(classification, Is.EqualTo(HotReloadUnityMessageDetector.Classification.NotUnityMessage));
            Assert.That(targetType, Is.Null);
        }

        /// <summary>
        /// What: the marker with no number after it is not a suffix the worker produced, so the name
        /// is left as it is.
        /// </summary>
        [Test]
        public void ResolveMemberName_MarkerWithoutANumber_KeepsTheName()
        {
            MethodInfo shim = ShimOf(typeof(WorkerNamedShims), "Update__shim");

            Assert.That(
                HotReloadUnityMessageDetector.ResolveMemberName(shim),
                Is.EqualTo("Update__shim"));
        }

        /// <summary>
        /// What: a name that is nothing but the marker and a number has no member name in front of
        /// it, so it is left as it is rather than resolving to an empty name.
        /// </summary>
        [Test]
        public void ResolveMemberName_NameIsOnlyTheMarker_KeepsTheName()
        {
            MethodInfo shim = ShimOf(typeof(WorkerNamedShims), "__shim0");

            Assert.That(
                HotReloadUnityMessageDetector.ResolveMemberName(shim),
                Is.EqualTo("__shim0"));
        }

        private static MethodInfo ShimOf(Type host, string methodName)
        {
            MethodInfo shim = host.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
            return shim;
        }

        private sealed class DetectorFixture : MonoBehaviour
        {
        }

        private sealed class PlainReceiver
        {
        }

        /// <summary>Models the shims the worker emits for instance methods on a MonoBehaviour.</summary>
        private static class ForwardedShims
        {
            public static void Update(DetectorFixture __uloopInstance)
            {
            }

            public static void Awake(DetectorFixture __uloopInstance)
            {
            }

            public static void Helper(DetectorFixture __uloopInstance)
            {
            }

            public static void LateUpdate()
            {
            }
        }

        /// <summary>
        /// Models the names the worker actually gives its shims: the member's name, the marker, and
        /// the number that keeps one run's shims apart.
        /// </summary>
        private static class WorkerNamedShims
        {
            public static void Update__shim0(DetectorFixture __uloopInstance)
            {
            }

            public static void Update__shim0__shim1(DetectorFixture __uloopInstance)
            {
            }

            public static void Update__shim(DetectorFixture __uloopInstance)
            {
            }

            public static void __shim0(DetectorFixture __uloopInstance)
            {
            }
        }

        /// <summary>Models a user's own static method whose first argument is the MonoBehaviour.</summary>
        private static class MisnamedReceiverShims
        {
            public static void Update(DetectorFixture value)
            {
            }
        }

        private static class PlainReceiverShims
        {
            public static void Update(PlainReceiver __uloopInstance)
            {
            }
        }

        private static class ReturningShims
        {
            public static int Update(DetectorFixture __uloopInstance)
            {
                return 0;
            }
        }

        /// <summary>Models an added method a user gave a Unity message name and a by-ref argument.</summary>
        private static class ByRefShims
        {
            public static void OnTriggerEnter(DetectorFixture __uloopInstance, ref Collider other)
            {
                other = null;
            }
        }
    }
}
