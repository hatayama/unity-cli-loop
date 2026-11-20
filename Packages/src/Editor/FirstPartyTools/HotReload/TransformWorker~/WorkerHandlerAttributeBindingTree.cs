using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Builds a binding-only declaration of InterpolatedStringHandlerAttribute for a run whose target
// assembly declares that attribute internally. A project on a base class library without the
// attribute declares its own copy, usually internal and in a file of its own. The worker reads the
// target assembly through its public surface, so when that file is not passed the attribute does
// not bind: a handler type is then an ordinary type, its interpolated strings make no handler
// calls, and the implicit-call checks never see an internal AppendFormatted that the shim compile,
// which reads the attribute from metadata, does call.
internal static class WorkerHandlerAttributeBindingTree
{
    internal const string TreePath = "UloopHotReloadInterpolatedStringHandlerAttribute.cs";

    private const string AttributeMetadataName = "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute";

    // Why internal: the declaration only has to make the compiler recognize handler types in this
    // compilation, which reads the attribute by its full name whatever its accessibility.
    private const string Declaration =
        "namespace System.Runtime.CompilerServices\n"
        + "{\n"
        + "    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]\n"
        + "    internal sealed class InterpolatedStringHandlerAttribute : System.Attribute\n"
        + "    {\n"
        + "    }\n"
        + "}\n";

    /// <summary>
    /// What: the compilation with the attribute declared when the target assembly declares it but
    /// the compilation cannot bind it; otherwise the compilation unchanged. Also gives the target
    /// assembly with every member visible, resolved from the returned compilation.
    /// </summary>
    internal static CSharpCompilation AddWhenOnlyTheTargetDeclaresIt(
        CSharpCompilation compilation,
        MetadataReference targetTypesReference,
        CSharpParseOptions parseOptions,
        out IAssemblySymbol targetAssemblyWithAllMembers)
    {
        targetAssemblyWithAllMembers = WorkerCompiledAssemblySymbols.ResolveWithAllMembers(compilation, targetTypesReference);

        if (targetAssemblyWithAllMembers == null
            || HasAccessibleDeclaration(compilation)
            || targetAssemblyWithAllMembers.GetTypeByMetadataName(AttributeMetadataName) == null)
        {
            return compilation;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(Declaration, parseOptions, TreePath, Encoding.UTF8);
        CSharpCompilation withAttribute = compilation.AddSyntaxTrees(tree);

        // Why resolved again: the caller's type home must come from the compilation its semantic
        // models bind with, and the one resolved above belongs to the compilation without the tree.
        targetAssemblyWithAllMembers = WorkerCompiledAssemblySymbols.ResolveWithAllMembers(withAttribute, targetTypesReference);
        return withAttribute;
    }

    // Why an accessible declaration and not any: GetTypeByMetadataName also returns an internal
    // copy of another assembly, which the compiler cannot bind the attribute to. A passed file or
    // a public declaration in a reference already binds, and a second one would make it ambiguous.
    private static bool HasAccessibleDeclaration(CSharpCompilation compilation)
    {
        foreach (INamedTypeSymbol declared in compilation.GetTypesByMetadataName(AttributeMetadataName))
        {
            if (compilation.IsSymbolAccessibleWithin(declared, compilation.Assembly))
            {
                return true;
            }
        }

        return false;
    }
}
