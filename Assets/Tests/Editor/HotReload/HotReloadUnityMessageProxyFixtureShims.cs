using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    // Why the file is named after the shim class and not after the fixture: Unity binds a
    // MonoScript to the class whose name matches the file, and refuses AddComponent for a
    // MonoScript that lives under an Editor folder. A MonoBehaviour with no file of its own has
    // no such MonoScript, so a test can add it to a GameObject.

    /// <summary>
    /// Stands in for a user MonoBehaviour that a hot reload added Unity messages to, recording
    /// every forwarded call so a test can tell what reached the target.
    /// </summary>
    public sealed class HotReloadUnityMessageProxyFixture : MonoBehaviour
    {
        public int UpdateCount;
        public int TriggerCount;
        public bool StartRan;
        public bool PauseValue;
        public Collider LastCollider;
    }

    /// <summary>
    /// Models the shims the transform worker generates for the fixture's added messages: public
    /// static methods whose first parameter is the receiver.
    /// </summary>
    public static class HotReloadUnityMessageProxyFixtureShims
    {
        public static void Update(HotReloadUnityMessageProxyFixture __uloopInstance)
        {
            __uloopInstance.UpdateCount++;
        }

        public static void Start(HotReloadUnityMessageProxyFixture __uloopInstance)
        {
            __uloopInstance.StartRan = true;
        }

        public static void OnTriggerEnter(HotReloadUnityMessageProxyFixture __uloopInstance, Collider other)
        {
            __uloopInstance.TriggerCount++;
            __uloopInstance.LastCollider = other;
        }

        public static void OnApplicationPause(HotReloadUnityMessageProxyFixture __uloopInstance, bool paused)
        {
            __uloopInstance.PauseValue = paused;
        }
    }

    /// <summary>
    /// A target type this assembly keeps to itself, so a forwarder that relied on ordinary
    /// accessibility would fail to call into it.
    /// </summary>
    internal sealed class HotReloadUnityMessageInternalFixture : MonoBehaviour
    {
        public int UpdateCount;
    }

    internal static class HotReloadUnityMessageInternalFixtureShims
    {
        public static void Update(HotReloadUnityMessageInternalFixture __uloopInstance)
        {
            __uloopInstance.UpdateCount++;
        }
    }
}
