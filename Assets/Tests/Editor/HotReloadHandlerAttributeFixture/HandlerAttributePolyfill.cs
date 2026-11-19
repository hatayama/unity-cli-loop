using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// The project's own declaration of the handler attribute, internal and in a file of its own,
    /// as a project on a base class library without it declares it. This assembly references no
    /// other assembly that declares the attribute, so this is the only declaration it sees.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class InterpolatedStringHandlerAttribute : Attribute
    {
    }
}
