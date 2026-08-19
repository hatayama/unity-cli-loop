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

internal static class CompiledMemberMatcher
{
    internal static INamedTypeSymbol FindCompiledType(
        INamedTypeSymbol sourceType,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        if (sourceType == null || targetTypesAssemblySymbol == null)
        {
            return null;
        }

        return targetTypesAssemblySymbol.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(sourceType));
    }

    internal static CompiledMethodMatch MatchCompiledOrdinaryMethod(
        INamedTypeSymbol compiledType,
        IMethodSymbol sourceMethod)
    {
        string[] sourceParameterTypeFullNames = sourceMethod.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        foreach (ISymbol member in compiledType.GetMembers(sourceMethod.Name))
        {
            // Why compare Arity: Caller(int) and Caller<T>(int) must not share a compiled match.
            if (member is not IMethodSymbol compiledMethod
                || compiledMethod.MethodKind != MethodKind.Ordinary
                || compiledMethod.Arity != sourceMethod.Arity
                || compiledMethod.IsStatic != sourceMethod.IsStatic
                || compiledMethod.Parameters.Length != sourceParameterTypeFullNames.Length)
            {
                continue;
            }

            bool parametersMatch = true;
            for (int index = 0; index < sourceParameterTypeFullNames.Length; index++)
            {
                if (CecilTypeNames.ToParameterTypeFullName(compiledMethod.Parameters[index])
                    != sourceParameterTypeFullNames[index])
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (!parametersMatch)
            {
                continue;
            }

            if (ReturnTypesMatch(compiledMethod, sourceMethod))
            {
                return CompiledMethodMatch.Matched;
            }

            return CompiledMethodMatch.ReturnTypeChanged;
        }

        return CompiledMethodMatch.NotFound;
    }

    internal static bool ReturnTypesMatch(IMethodSymbol compiledMethod, IMethodSymbol sourceMethod)
    {
        if (CecilTypeNames.ToCecilFullName(compiledMethod.ReturnType)
            != CecilTypeNames.ToCecilFullName(sourceMethod.ReturnType))
        {
            return false;
        }

        // Why compare byref flags separately: ToCecilFullName sees only ITypeSymbol, so
        // int F() and ref int F() both become System.Int32. Missing this would transplant
        // the new body onto the old non-byref signature.
        return compiledMethod.ReturnsByRef == sourceMethod.ReturnsByRef
            && compiledMethod.ReturnsByRefReadonly == sourceMethod.ReturnsByRefReadonly;
    }

    /// <summary>
    /// What: matches a source field to a compiled member by name, then reports type,
    /// static/const, or property/event kind drift so callers can skip instead of
    /// rewriting storage.
    /// </summary>
    internal static CompiledFieldMatch MatchCompiledField(
        INamedTypeSymbol compiledType,
        IFieldSymbol sourceField)
    {
        foreach (ISymbol member in compiledType.GetMembers(sourceField.Name))
        {
            if (member is not IFieldSymbol compiledField)
            {
                // Why: a compiled property or event still owns this name. Treating the
                // source field as added would duplicate storage in the side table.
                if (member.Kind == SymbolKind.Property || member.Kind == SymbolKind.Event)
                {
                    return CompiledFieldMatch.MemberKindChanged;
                }

                continue;
            }

            // Why return here: C# field names are unique in a type, so a modifier
            // mismatch is the compiled field, not a miss that should become a store.
            if (compiledField.IsStatic != sourceField.IsStatic
                || compiledField.IsConst != sourceField.IsConst)
            {
                return CompiledFieldMatch.FieldModifiersChanged;
            }

            if (CecilTypeNames.ToCecilFullName(compiledField.Type)
                == CecilTypeNames.ToCecilFullName(sourceField.Type))
            {
                return CompiledFieldMatch.Matched;
            }

            return CompiledFieldMatch.FieldTypeChanged;
        }

        return CompiledFieldMatch.NotFound;
    }

    internal static bool IsCompiledFieldDeclarationChange(CompiledFieldMatch fieldMatch)
    {
        return fieldMatch == CompiledFieldMatch.FieldTypeChanged
            || fieldMatch == CompiledFieldMatch.FieldModifiersChanged
            || fieldMatch == CompiledFieldMatch.MemberKindChanged;
    }

    internal static string TryFormatCompiledFieldDeclarationChangeReason(
        CompiledFieldMatch fieldMatch,
        string fieldName)
    {
        if (fieldMatch == CompiledFieldMatch.FieldTypeChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.FieldTypeChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.FieldModifiersChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.FieldModifiersChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.MemberKindChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.MemberKindChanged,
                fieldName);
        }

        return null;
    }
}
