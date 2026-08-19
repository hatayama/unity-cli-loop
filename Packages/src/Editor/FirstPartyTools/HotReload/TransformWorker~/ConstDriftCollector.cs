using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class ConstDriftCollector
{
    /// <summary>
    /// Detects const declarations (including enum members) in the edited source whose values
    /// differ from the compiled target assembly. C# inlines const values at compile time and
    /// shims compile against the already-compiled assembly, so such edits silently keep the old
    /// value at runtime; each drift becomes a response warning instead of a silent no-op.
    /// </summary>
    internal static List<string> CollectConstDriftWarnings(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        List<string> warnings = new List<string>();
        if (targetTypesAssemblySymbol == null)
        {
            return warnings;
        }

        HashSet<string> seenTypeMetadataNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseTypeDeclarationSyntax typeDeclaration
            in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            INamedTypeSymbol sourceType = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (sourceType == null)
            {
                continue;
            }

            // Partial declarations in one file resolve to the same merged type symbol, and
            // comparing its members once per declaration would duplicate every warning.
            string typeMetadataName = ToReflectionMetadataName(sourceType);
            if (!seenTypeMetadataNames.Add(typeMetadataName))
            {
                continue;
            }

            INamedTypeSymbol compiledType = targetTypesAssemblySymbol.GetTypeByMetadataName(
                typeMetadataName);
            if (compiledType == null)
            {
                continue;
            }

            foreach (IFieldSymbol sourceField in sourceType.GetMembers().OfType<IFieldSymbol>())
            {
                if (!sourceField.HasConstantValue)
                {
                    continue;
                }

                IFieldSymbol compiledField = null;
                foreach (ISymbol member in compiledType.GetMembers(sourceField.Name))
                {
                    if (member is IFieldSymbol candidate && candidate.HasConstantValue)
                    {
                        compiledField = candidate;
                        break;
                    }
                }

                if (compiledField == null)
                {
                    // A const missing from the compiled assembly is a new declaration, not a
                    // drift; bodies reading it already fail shim compilation with their own
                    // actionable error.
                    continue;
                }

                if (Equals(sourceField.ConstantValue, compiledField.ConstantValue))
                {
                    continue;
                }

                warnings.Add(
                    "const " + sourceType.ToDisplayString() + "." + sourceField.Name
                    + " is " + FormatConstValue(sourceField.ConstantValue)
                    + " in the edited source but " + FormatConstValue(compiledField.ConstantValue)
                    + " in the compiled assembly; edits outside method bodies never take effect "
                    + "through hot reload. Run 'uloop compile' to apply this change.");
            }
        }

        return warnings;
    }

    /// <summary>
    /// Builds the CLR reflection metadata name ('+' for nested types) that
    /// IAssemblySymbol.GetTypeByMetadataName expects. CecilTypeNames.ToMetadataName cannot be
    /// reused here because Cecil separates nested types with '/'.
    /// </summary>
    internal static string ToReflectionMetadataName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingType != null)
        {
            return ToReflectionMetadataName(typeSymbol.ContainingType) + "+" + typeSymbol.MetadataName;
        }

        if (typeSymbol.ContainingNamespace == null || typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.MetadataName;
        }

        return typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }

    /// <summary>
    /// Renders a const value for the drift warning: quoted for strings and chars, "null" for
    /// null, invariant-culture text otherwise.
    /// </summary>
    internal static string FormatConstValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string text)
        {
            return "\"" + text + "\"";
        }

        if (value is char character)
        {
            // A bare char (especially whitespace) is invisible inside the warning sentence;
            // quote it the way C# source spells it.
            return "'" + character + "'";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
