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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class CompiledMemberMatcher
{
    internal static CompiledMethodMatch MatchCompiledOrdinaryMethod(
        INamedTypeSymbol compiledType,
        IMethodSymbol sourceMethod)
    {
        IMethodSymbol compiledMethod = FindCompiledOrdinaryMethod(compiledType, sourceMethod);
        if (compiledMethod == null)
        {
            return CompiledMethodMatch.NotFound;
        }

        return ReturnTypesMatch(compiledMethod, sourceMethod)
            ? CompiledMethodMatch.Matched
            : CompiledMethodMatch.ReturnTypeChanged;
    }

    /// <summary>
    /// The compiled ordinary method with the source method's name, arity, static-ness and parameter
    /// types, whatever its return type; null when there is none.
    /// </summary>
    internal static IMethodSymbol FindCompiledOrdinaryMethod(
        INamedTypeSymbol compiledType,
        IMethodSymbol sourceMethod)
    {
        string[] sourceParameterTypeFullNames = sourceMethod.Parameters
            .Select(CecilTypeNames.ToParameterSignatureComparisonName)
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
                if (CecilTypeNames.ToParameterSignatureComparisonName(compiledMethod.Parameters[index])
                    != sourceParameterTypeFullNames[index])
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
            {
                return compiledMethod;
            }
        }

        return null;
    }

    internal static bool ReturnTypesMatch(IMethodSymbol compiledMethod, IMethodSymbol sourceMethod)
    {
        if (CecilTypeNames.ToSignatureComparisonName(compiledMethod.ReturnType)
            != CecilTypeNames.ToSignatureComparisonName(sourceMethod.ReturnType))
        {
            return false;
        }

        // Why compare byref flags separately: ToSignatureComparisonName sees only ITypeSymbol, so
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

            if (CecilTypeNames.ToSignatureComparisonName(compiledField.Type)
                == CecilTypeNames.ToSignatureComparisonName(sourceField.Type))
            {
                return CompiledFieldMatch.Matched;
            }

            return CompiledFieldMatch.FieldTypeChanged;
        }

        return CompiledFieldMatch.NotFound;
    }

    internal static WorkerReason TryBuildCompiledFieldDeclarationChangeReason(
        CompiledFieldMatch fieldMatch,
        string fieldName)
    {
        if (fieldMatch == CompiledFieldMatch.FieldTypeChanged)
        {
            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.FieldModifiersChanged)
        {
            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AddedFieldFieldModifiersChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.MemberKindChanged)
        {
            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AddedFieldMemberKindChanged,
                fieldName);
        }

        return null;
    }
}
