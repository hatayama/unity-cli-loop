using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Finds const fields an introduced declaration reads that the artifact cannot be compiled against:
// the edited source value no longer matches the value compiled into the target assembly, or the
// edited value cannot be read at all. The artifact is compiled against that assembly, so in either
// case the reference would silently fold the stale metadata value into the retained type.
internal static class IntroducedTypeConstDriftDetector
{
    // True when the declaration reads a const the artifact cannot be compiled against: one whose
    // edited value no longer matches the compiled one, or one whose edited value cannot be read at
    // all. Reports which const it was and why.
    internal static bool TryFindUnusableReferencedConst(
        BaseTypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        out string identifier,
        out string reason)
    {
        foreach (SyntaxNode node in declaration.DescendantNodesAndSelf())
        {
            // nameof yields the identifier, never the value, so a const named inside one is not
            // folded into the artifact and its value cannot go stale there.
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            IFieldSymbol field = semanticModel.GetSymbolInfo(node).Symbol as IFieldSymbol;
            if (field == null || !field.IsConst)
            {
                continue;
            }

            // A const whose initializer does not compile carries no value to compare, and the
            // artifact compile does not see that file at all: it would succeed against the value
            // still in the target assembly and fold a stale constant into the retained type.
            if (!field.HasConstantValue)
            {
                identifier = CecilTypeNames.ToMetadataName(field.ContainingType) + "." + field.Name;
                reason = "Const value cannot be verified";
                return true;
            }

            if (HasDriftedFromCompiledValue(field, targetAssembly))
            {
                identifier = CecilTypeNames.ToMetadataName(field.ContainingType) + "." + field.Name;
                reason = "Changed const requires a compile";
                return true;
            }
        }

        identifier = null;
        reason = null;
        return false;
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
