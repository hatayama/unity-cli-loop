using System.Runtime.CompilerServices;

/// <summary>
/// Compiled caller type declared in the global namespace. Tests edit a copy of this body so it
/// calls a member added in the sibling global-namespace host file within the same reload.
/// </summary>
internal sealed class HotReloadGlobalNamespaceCaller
{
    // Why NoInlining: end-to-end tests read the patched body back through a direct call, which
    // an inlined copy at the call site would not observe.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Call(HotReloadGlobalNamespaceHost host)
    {
        return host.Value();
    }
}
