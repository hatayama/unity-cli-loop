using System;
using Microsoft.CodeAnalysis;

// Tells whether an event a body subscribes to is one this edit adds. A subscription stays on the
// event's add/remove accessors in the shim, and the shim is compiled against the assembly that
// serves the event's declaring type, so an event that assembly lacks fails the shim compile
// (CS1061) instead of skipping the method.
internal sealed class AddedEventLookup
{
    private readonly WorkerTypeHome _home;
    private readonly WorkerSourceUnit _sourceUnit;
    private readonly SemanticModel _semanticModel;

    internal AddedEventLookup(WorkerTypeHome home, WorkerSourceUnit sourceUnit, SemanticModel semanticModel)
    {
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _sourceUnit = sourceUnit ?? throw new ArgumentNullException(nameof(sourceUnit));
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    internal bool IsAddedInThisEdit(IEventSymbol eventSymbol)
    {
        // An event read from metadata belongs to an assembly the edit does not compile, so it is
        // there already.
        if (eventSymbol.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        INamedTypeSymbol compiledType = FindCompiledDeclaringType(eventSymbol.ContainingType.OriginalDefinition);
        // Why no compiled type means not added: the declaring type is itself new to the Editor,
        // and the introduced-type paths already decide what a body using it may do.
        if (compiledType == null)
        {
            return false;
        }

        foreach (ISymbol member in compiledType.GetMembers(eventSymbol.Name))
        {
            if (member is IEventSymbol)
            {
                return false;
            }
        }

        return true;
    }

    // Why not the subscribing type's compiled counterpart and its assembly: when a retained
    // artifact serves the subscribing type, that assembly is the artifact, which never holds a
    // type the patch target compiled. The declaring type is looked up where it is served instead:
    // the patch target first, then any artifact this run keeps.
    private INamedTypeSymbol FindCompiledDeclaringType(INamedTypeSymbol declaringType)
    {
        return _home.FindCompiledType(declaringType)
            ?? RetainedBodyEditHome.FindRunRetainedType(_sourceUnit, _semanticModel, declaringType);
    }
}
