using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// How the worker binds a referenced assembly when it has to see every member of it, not only the
// public surface the default metadata import exposes. A classification that reads a compiled type
// through the public surface alone reports its private members as added by this edit, so the
// widened import belongs to every lookup of a type the run patches rather than declares.
internal static class WorkerCompiledAssemblySymbols
{
    /// <summary>
    /// The assembly symbol a reference binds to with private and internal members visible, or
    /// null when the run could not read the reference.
    /// </summary>
    internal static IAssemblySymbol ResolveWithAllMembers(
        CSharpCompilation compilation,
        MetadataReference reference)
    {
        // Why a throwaway compilation: widening the main one would also widen what every
        // classification query can bind to, so the wider import stays confined to this lookup.
        if (reference == null)
        {
            return null;
        }

        CSharpCompilation allMembersCompilation = compilation.WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithMetadataImportOptions(MetadataImportOptions.All));
        return allMembersCompilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
    }
}
