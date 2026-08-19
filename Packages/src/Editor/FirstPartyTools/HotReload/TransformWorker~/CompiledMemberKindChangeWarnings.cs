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

internal static class CompiledMemberKindChangeWarnings
{
    internal const string CompiledPropertyKindChangeWarningFormat =
        "Compiled property '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

    internal const string CompiledEventKindChangeWarningFormat =
        "Compiled event '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

    /// <summary>
    /// What: names compiled properties and events that the edited source deleted or
    /// redeclared as another member kind, even when no method body changed.
    /// </summary>
    internal static void AppendCompiledPropertyOrEventKindChangeWarnings(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<string> warnings)
    {
        if (targetTypesAssemblySymbol == null)
        {
            return;
        }

        HashSet<string> seenTypeMetadataNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseTypeDeclarationSyntax typeDeclaration
            in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            // Why syntax PartialKeyword: the worker compilation sees only this file. A
            // compiled property or event declared in another partial file is absent from
            // the source symbol and would look permanently removed. Locations cannot be
            // used — metadata symbols have no source locations.
            if (typeDeclaration is TypeDeclarationSyntax typedDeclaration
                && typedDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                continue;
            }

            INamedTypeSymbol sourceType = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (sourceType == null)
            {
                continue;
            }

            string typeMetadataName = ConstDriftCollector.ToReflectionMetadataName(sourceType);
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

            AppendMissingCompiledPropertyOrEventWarnings(compiledType, sourceType, warnings);
        }
    }

    internal static void AppendMissingCompiledPropertyOrEventWarnings(
        INamedTypeSymbol compiledType,
        INamedTypeSymbol sourceType,
        List<string> warnings)
    {
        foreach (ISymbol compiledMember in compiledType.GetMembers())
        {
            string warning = TryFormatMissingCompiledPropertyOrEventWarning(compiledMember, sourceType);
            if (warning == null)
            {
                continue;
            }

            warnings.Add(warning);
        }
    }

    internal static string TryFormatMissingCompiledPropertyOrEventWarning(
        ISymbol compiledMember,
        INamedTypeSymbol sourceType)
    {
        // Why still check IsImplicitlyDeclared: source-compiled symbols can be implicit.
        // Metadata symbols from the PE almost always report false, so this is best-effort
        // and does not filter compiler-generated members out of the compiled assembly.
        if (compiledMember is IPropertySymbol property
            && !property.IsIndexer
            && !property.IsImplicitlyDeclared
            && !SourceDeclaresProperty(sourceType, property.Name))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CompiledPropertyKindChangeWarningFormat,
                sourceType.ToDisplayString() + "." + property.Name);
        }

        if (compiledMember is IEventSymbol compiledEvent
            && !compiledEvent.IsImplicitlyDeclared
            && !SourceDeclaresEvent(sourceType, compiledEvent.Name))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CompiledEventKindChangeWarningFormat,
                sourceType.ToDisplayString() + "." + compiledEvent.Name);
        }

        return null;
    }

    internal static bool SourceDeclaresProperty(INamedTypeSymbol sourceType, string name)
    {
        foreach (ISymbol member in sourceType.GetMembers(name))
        {
            if (member is IPropertySymbol property && !property.IsIndexer)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool SourceDeclaresEvent(INamedTypeSymbol sourceType, string name)
    {
        foreach (ISymbol member in sourceType.GetMembers(name))
        {
            if (member is IEventSymbol)
            {
                return true;
            }
        }

        return false;
    }
}
