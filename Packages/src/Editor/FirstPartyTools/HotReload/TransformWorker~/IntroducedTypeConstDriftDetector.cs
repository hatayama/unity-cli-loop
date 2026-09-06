using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Finds const fields an introduced declaration reads whose edited source value no longer matches
// the value compiled into the target assembly. The artifact is compiled against that assembly, so
// the reference would silently fold the stale metadata value into the retained type.
internal static class IntroducedTypeConstDriftDetector
{
    // The identifier of the first changed const the declaration reads, or null when every const it
    // reads still holds the value the target assembly was compiled with.
    internal static string FindChangedReferencedConst(
        BaseTypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        foreach (SyntaxNode node in declaration.DescendantNodesAndSelf())
        {
            IFieldSymbol field = semanticModel.GetSymbolInfo(node).Symbol as IFieldSymbol;
            if (field == null || !field.IsConst || !field.HasConstantValue)
            {
                continue;
            }

            if (HasDriftedFromCompiledValue(field, targetAssembly))
            {
                return CecilTypeNames.ToMetadataName(field.ContainingType) + "." + field.Name;
            }
        }

        return null;
    }

    private static bool HasDriftedFromCompiledValue(IFieldSymbol field, IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol compiledType = CompiledMemberMatcher.FindCompiledType(field.ContainingType, targetAssembly);
        if (compiledType == null)
        {
            return false;
        }

        // A const that is not in the compiled type at all is an added const, which planning
        // classifies elsewhere; only a value that exists on both sides can have drifted.
        foreach (ISymbol member in compiledType.GetMembers(field.Name))
        {
            IFieldSymbol compiledField = member as IFieldSymbol;
            if (compiledField == null || !compiledField.IsConst || !compiledField.HasConstantValue)
            {
                continue;
            }

            return !ConstDriftCollector.HasSameConstantValue(field.ConstantValue, compiledField.ConstantValue);
        }

        return false;
    }
}
