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
    internal const string NewConstWarningFormat =
        "const {0} exists only in the edited source, not in the compiled assembly. Method bodies patched in this same run already have its value folded in, so this run needs no compile; only bodies in files outside this reload that reference it fail shim compilation. Run 'uloop compile' when one of those files has to see it.";

    // Why a separate wording: patched bodies fold an added const, but an added enum member is
    // bound as a member access on the compiled enum, so it fails shim compilation even in this
    // reload's files and the "needs no compile" sentence would be false.
    internal const string NewEnumMemberWarningFormat =
        "enum member {0} exists only in the edited source, not in the compiled assembly. Hot reload does not fold an added enum member into patched bodies, so every body that names it fails shim compilation (CS0117), including bodies in this reload's files. Write the underlying value as a cast instead ('({1}){2}'; ToString() then prints the number, not the name), or run 'uloop compile' to add the member.";

    // Why only for a file in this reload: the enum file then builds the enum from source, so an
    // added member whose compiled neighbours still name the compiled enum cannot bind and is
    // skipped. A changed file outside the reload is already left out, and the cast alone lets
    // such members through, so the advice would send the user the wrong way.
    // Why not "only while": only added members that pass the enum to, or take it from, compiled
    // code or an introduced type are skipped; other added members still hot reload.
    internal const string EnumFileInReloadWarningFormat =
        " With the file that declares {0} in this reload, an added member that passes {0} to or takes it from compiled code or a type hot reload introduced is skipped; to keep such a member hot reloading, leave that file out of --files until you compile.";

    internal const string ChangedConstWarningFormat =
        "const {0} is {1} in the edited source but {2} in the compiled assembly; edits outside method bodies never take effect through hot reload - a method body patched in the same run still compiles against the compiled assembly and keeps the old value, so nothing runs with {1} yet. This warning repeats on every reload while the two values differ. Run 'uloop compile' to apply this change.";

    /// <summary>
    /// Detects const declarations (including enum members) in the edited source whose values
    /// differ from the compiled target assembly, and consts that exist only in the edited source.
    /// C# inlines const values at compile time and shims compile against the already-compiled
    /// assembly, so value edits silently keep the old value at runtime; new consts fold into
    /// the bodies patched by this reload but fail shim compilation in files outside it.
    /// isFileInReload tells whether the scanned file is passed or carried into this reload,
    /// which decides whether an added enum member warns about keeping the file out of --files.
    /// </summary>
    internal static List<string> CollectConstDriftWarnings(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        bool isFileInReload)
    {
        List<string> warnings = new List<string>();
        if (home.AssemblySymbol == null)
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

            INamedTypeSymbol compiledType = home.FindCompiledTypeByMetadataName(
                typeMetadataName);
            if (compiledType == null)
            {
                continue;
            }

            // Why once per enum: several added members of one enum each get a warning, and the
            // file advice is about the enum's file, so repeating it on every line adds nothing.
            bool needsEnumFileInReloadAdvice = isFileInReload && sourceType.TypeKind == TypeKind.Enum;
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

                string constDisplayName = sourceType.ToDisplayString() + "." + sourceField.Name;
                if (compiledField == null)
                {
                    string newConstWarning = FormatNewConstWarning(sourceType, sourceField, constDisplayName);
                    if (needsEnumFileInReloadAdvice)
                    {
                        newConstWarning += string.Format(
                            CultureInfo.InvariantCulture,
                            EnumFileInReloadWarningFormat,
                            sourceType.ToDisplayString());
                        needsEnumFileInReloadAdvice = false;
                    }

                    warnings.Add(newConstWarning);
                    continue;
                }

                if (HasSameConstantValue(sourceField.ConstantValue, compiledField.ConstantValue))
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        ChangedConstWarningFormat,
                        constDisplayName,
                        FormatConstValue(sourceField.ConstantValue),
                        FormatConstValue(compiledField.ConstantValue)));
            }
        }

        return warnings;
    }

    /// <summary>
    /// Builds the warning for a const that exists only in the edited source, choosing the enum
    /// member wording when the declaring type is an enum.
    /// </summary>
    private static string FormatNewConstWarning(
        INamedTypeSymbol sourceType,
        IFieldSymbol sourceField,
        string constDisplayName)
    {
        if (sourceType.TypeKind != TypeKind.Enum)
        {
            return string.Format(CultureInfo.InvariantCulture, NewConstWarningFormat, constDisplayName);
        }

        // Why the parentheses: '(E)-1' parses as a subtraction when E is not a keyword type.
        string underlyingValue = FormatConstValue(sourceField.ConstantValue);
        if (underlyingValue.StartsWith("-", StringComparison.Ordinal))
        {
            underlyingValue = "(" + underlyingValue + ")";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            NewEnumMemberWarningFormat,
            constDisplayName,
            sourceType.ToDisplayString(),
            underlyingValue);
    }

    /// <summary>
    /// Compares an edited-source const value with the value compiled into the target assembly.
    /// </summary>
    // Why one helper: the transform warns about a drifted const and planning refuses an
    // introduced type that reads one. Two comparison rules would let a value count as changed in
    // one path and unchanged in the other.
    internal static bool HasSameConstantValue(object sourceValue, object compiledValue)
    {
        return Equals(sourceValue, compiledValue);
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
