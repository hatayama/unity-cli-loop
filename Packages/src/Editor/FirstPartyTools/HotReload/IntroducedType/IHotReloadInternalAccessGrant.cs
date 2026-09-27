using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Grants generated artifact methods access to their original assembly's internal members.
    /// </summary>
    internal interface IHotReloadInternalAccessGrant
    {
        bool IsAvailable { get; }
        string UnavailableReason { get; }
        HotReloadInternalAccessGrantResult Grant(Assembly assembly);
    }
}
