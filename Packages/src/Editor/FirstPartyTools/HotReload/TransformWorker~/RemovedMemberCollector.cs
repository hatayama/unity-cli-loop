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

internal static class RemovedMemberCollector
{
    internal static void CollectRemovedMembersIfBaseline(
        BaselineSnapshotState baseline,
        CompilationUnitSyntax plainRoot,
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures)
    {
        if (!baseline.HasBaseline)
        {
            return;
        }

        CollectRemovedMethods(
            baseline.SnapshotMethodMap,
            baseline.PlainCurrentMethodMap,
            addedMethodCatalog,
            removedMembers);
        CollectRemovedMethodSignaturesForDeletedNames(
            typeEmitStates,
            semanticModel,
            targetTypesAssemblySymbol,
            removedMembers,
            removedMethodSignatures);
        Dictionary<string, VariableDeclaratorSyntax> snapshotFieldMap =
            WorkerSyntaxIndex.BuildSyntaxFieldMapOrNull(baseline.SnapshotRoot);
        Dictionary<string, VariableDeclaratorSyntax> currentFieldMap =
            WorkerSyntaxIndex.BuildSyntaxFieldMapOrNull(plainRoot);
        if (snapshotFieldMap != null && currentFieldMap != null)
        {
            CollectRemovedFields(
                snapshotFieldMap,
                currentFieldMap,
                addedFieldCatalog,
                removedMembers);
        }
    }

    internal static void CollectRemovedMethods(
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerRemovedMember> removedMembers)
    {
        HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerRemovedMember existing in removedMembers)
        {
            if (existing.Kind == RemovedMemberKinds.Method)
            {
                seenNames.Add(existing.Name);
            }
        }

        foreach (KeyValuePair<string, MethodDeclarationSyntax> pair in snapshotMethodMap)
        {
            if (plainCurrentMethodMap.ContainsKey(pair.Key))
            {
                continue;
            }

            addedMethodCatalog.AddRemovedSyntaxKey(pair.Key);
            string name = pair.Value.Identifier.Text;
            if (!seenNames.Add(name))
            {
                continue;
            }

            removedMembers.Add(new WorkerRemovedMember
            {
                Kind = RemovedMemberKinds.Method,
                Name = name
            });
        }
    }

    internal static void CollectRemovedMethodSignaturesForDeletedNames(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures)
    {
        HashSet<string> removedMethodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerRemovedMember removed in removedMembers)
        {
            if (removed.Kind == RemovedMemberKinds.Method)
            {
                removedMethodNames.Add(removed.Name);
            }
        }

        if (removedMethodNames.Count == 0)
        {
            return;
        }

        foreach (TypeEmitState typeState in typeEmitStates)
        {
            if (typeState.TypeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                // Why skip: a compiled type includes methods declared in other files of a
                // partial. A name missing from this file is not proof the method was deleted.
                continue;
            }

            INamedTypeSymbol compiledType = CompiledMemberMatcher.FindCompiledType(typeState.TypeSymbol, targetTypesAssemblySymbol);
            if (compiledType == null)
            {
                continue;
            }

            foreach (ISymbol member in compiledType.GetMembers())
            {
                if (member is not IMethodSymbol compiledMethod
                    || compiledMethod.MethodKind != MethodKind.Ordinary
                    || !removedMethodNames.Contains(compiledMethod.Name))
                {
                    continue;
                }

                if (SourceDeclarationCoversCompiledMethod(typeState, semanticModel, compiledMethod))
                {
                    continue;
                }

                string[] parameterTypeFullNames = compiledMethod.Parameters
                    .Select(CecilTypeNames.ToParameterTypeFullName)
                    .ToArray();
                AddRemovedMethodSignature(
                    removedMethodSignatures,
                    typeState.TypeSymbol,
                    compiledMethod.Name,
                    parameterTypeFullNames,
                    compiledMethod.Arity);
            }
        }
    }

    internal static bool SourceDeclarationCoversCompiledMethod(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IMethodSymbol compiledMethod)
    {
        string[] compiledParameterTypeFullNames = compiledMethod.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol sourceMethod = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (sourceMethod == null
                || sourceMethod.MethodKind != MethodKind.Ordinary
                || sourceMethod.Name != compiledMethod.Name
                || sourceMethod.Arity != compiledMethod.Arity
                || sourceMethod.IsStatic != compiledMethod.IsStatic
                || sourceMethod.Parameters.Length != compiledParameterTypeFullNames.Length)
            {
                continue;
            }

            bool parametersMatch = true;
            for (int index = 0; index < compiledParameterTypeFullNames.Length; index++)
            {
                if (CecilTypeNames.ToParameterTypeFullName(sourceMethod.Parameters[index])
                    != compiledParameterTypeFullNames[index])
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
            {
                return true;
            }
        }

        return false;
    }

    // Why strip current always, snapshot only when equivalent: a return-type-only
    // change keeps the same syntax key (name+params). Stripping only the current tree
    // leaves the snapshot's old return type as unhandled outside-body drift. Stripping
    // both unconditionally hid attribute/accessibility diffs that still need the warning.
    internal static void RecordHandledAddedMethodSyntaxKey(
        AddedMethodCatalog addedMethodCatalog,
        string syntaxKey,
        bool replacesCompiledMethod,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap)
    {
        addedMethodCatalog.AddAddedSyntaxKey(syntaxKey);
        if (!replacesCompiledMethod || snapshotMethodMap == null || plainCurrentMethodMap == null)
        {
            return;
        }

        snapshotMethodMap.TryGetValue(syntaxKey, out MethodDeclarationSyntax snapshotDecl);
        plainCurrentMethodMap.TryGetValue(syntaxKey, out MethodDeclarationSyntax currentDecl);
        if (AreDeclarationsEquivalentIgnoringBodyAndReturnType(snapshotDecl, currentDecl))
        {
            addedMethodCatalog.AddRemovedSyntaxKey(syntaxKey);
        }
    }

    internal static bool AreDeclarationsEquivalentIgnoringBodyAndReturnType(
        MethodDeclarationSyntax snapshotDecl,
        MethodDeclarationSyntax currentDecl)
    {
        if (snapshotDecl == null || currentDecl == null)
        {
            return false;
        }

        MethodDeclarationSyntax normalizedSnapshot =
            NormalizeDeclarationIgnoringBodyAndReturnType(snapshotDecl);
        MethodDeclarationSyntax normalizedCurrent =
            NormalizeDeclarationIgnoringBodyAndReturnType(currentDecl);
        return SyntaxFactory.AreEquivalent(normalizedSnapshot, normalizedCurrent, topLevel: false);
    }

    internal static MethodDeclarationSyntax NormalizeDeclarationIgnoringBodyAndReturnType(
        MethodDeclarationSyntax method)
    {
        TypeSyntax placeholderReturn = SyntaxFactory.PredefinedType(
            SyntaxFactory.Token(SyntaxKind.VoidKeyword));
        return method
            .WithReturnType(placeholderReturn)
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .NormalizeWhitespace();
    }

    internal static void AddRemovedMethodName(List<WorkerRemovedMember> removedMembers, string name)
    {
        foreach (WorkerRemovedMember existing in removedMembers)
        {
            if (existing.Kind == RemovedMemberKinds.Method && existing.Name == name)
            {
                return;
            }
        }

        removedMembers.Add(new WorkerRemovedMember
        {
            Kind = RemovedMemberKinds.Method,
            Name = name
        });
    }

    internal static void AddRemovedMethodSignature(
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        INamedTypeSymbol sourceType,
        string methodName,
        string[] parameterTypeFullNames,
        int genericArity)
    {
        string typeMetadataName = CecilTypeNames.ToMetadataName(sourceType);
        foreach (WorkerRemovedMethodSignature existing in removedMethodSignatures)
        {
            if (existing.TypeMetadataName == typeMetadataName
                && existing.MethodName == methodName
                && existing.GenericArity == genericArity
                && ParameterTypeFullNamesEqual(existing.ParameterTypeFullNames, parameterTypeFullNames))
            {
                return;
            }
        }

        removedMethodSignatures.Add(new WorkerRemovedMethodSignature
        {
            TypeMetadataName = typeMetadataName,
            MethodName = methodName,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = genericArity
        });
    }

    internal static bool ParameterTypeFullNamesEqual(string[] left, string[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static void CollectRemovedFields(
        Dictionary<string, VariableDeclaratorSyntax> snapshotFieldMap,
        Dictionary<string, VariableDeclaratorSyntax> plainCurrentFieldMap,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerRemovedMember> removedMembers)
    {
        HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, VariableDeclaratorSyntax> pair in snapshotFieldMap)
        {
            if (plainCurrentFieldMap.ContainsKey(pair.Key))
            {
                continue;
            }

            addedFieldCatalog.AddRemovedSyntaxKey(pair.Key);
            string name = pair.Value.Identifier.Text;
            if (!seenNames.Add(name))
            {
                continue;
            }

            removedMembers.Add(new WorkerRemovedMember
            {
                Kind = RemovedMemberKinds.Field,
                Name = name
            });
        }
    }
}
