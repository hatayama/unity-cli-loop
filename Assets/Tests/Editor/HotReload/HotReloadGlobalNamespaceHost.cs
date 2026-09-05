using System.Runtime.CompilerServices;

/// <summary>
/// Compiled host type declared in the global namespace. Tests add a member to a copy of this
/// source and expect the sibling caller's edited body to bind against it, which only works when
/// the shim emitter and the body rewriter agree on the namespace a global-namespace shim type
/// is synthesized into.
/// </summary>
internal sealed class HotReloadGlobalNamespaceHost
{
    // Why NoInlining: end-to-end tests edit this body and read the new value back through a
    // direct call, which an inlined copy at the call site would not observe.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Value()
    {
        return 1;
    }
}
