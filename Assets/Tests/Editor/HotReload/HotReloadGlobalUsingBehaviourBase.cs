using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GlobalUsingBase
{
    /// <summary>
    /// Unity object base type reachable only through the test assembly's global using, so an
    /// introduced type that derives from it without a using directive is refused as a Unity
    /// object only when the planning compilation carries that using.
    /// </summary>
    public class HotReloadGlobalUsingBehaviourBase : MonoBehaviour
    {
    }
}
