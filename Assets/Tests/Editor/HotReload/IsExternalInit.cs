namespace System.Runtime.CompilerServices
{
    // Unity's .NET profile does not provide this marker type, but init accessors require it. The
    // package declares its own copy as internal, so this test assembly cannot see that one.
    internal static class IsExternalInit
    {
    }
}
