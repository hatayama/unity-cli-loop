using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The two accounts under which an ordinary method of an edited file needs no patch at all: the
// baseline says its body is the one the assembly was compiled from, or a retained artifact serves
// its type and the fingerprint comparison did not name it among the changed bodies. Both produce
// the same row, so they are decided in one place rather than each emitting its own shape.
internal static class UnchangedOrdinaryMethodRecorder
{
    internal static bool TryRecordUnchangedOrdinaryMethod(
        bool isAddedMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        string[] parameterTypeFullNames,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        if (isAddedMethod || !hasBaseline)
        {
            return false;
        }

        // Why plainDecl: compare unannotated nodes; annotated methodDeclaration breaks
        // AreEquivalent for long-return / unchecked / switch shapes (see plainRoot).
        if (snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            unchangedMethods.Add(BuildUnchangedMethod(methodSymbol, typeState, parameterTypeFullNames));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records a method of a type a retained artifact serves as unchanged when this edit did not
    /// change its body, so the reload patches only the bodies it did change. Returns false for
    /// every type no artifact serves, which is every type until an introduced type is edited.
    /// </summary>
    internal static bool TryRecordRetainedUnchangedOrdinaryMethod(
        bool isAddedMethod,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        string[] parameterTypeFullNames,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        // Why not the baseline comparison the next step makes: a retained type has no snapshot of
        // its own, because the reload that introduced it wrote the artifact rather than a
        // baseline. The fingerprint comparison against that artifact is the only account of which
        // bodies changed, and it was already made before the declaration was kept.
        if (isAddedMethod
            || typeState.RetainedChangedMethodKeys == null
            || typeState.RetainedChangedMethodKeys.Contains(syntaxMethodKey))
        {
            return false;
        }

        unchangedMethods.Add(BuildUnchangedMethod(methodSymbol, typeState, parameterTypeFullNames));
        return true;
    }

    private static WorkerUnchangedMethod BuildUnchangedMethod(
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        string[] parameterTypeFullNames)
    {
        return new WorkerUnchangedMethod
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            MethodName = methodSymbol.Name,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = methodSymbol.Arity,
            HomeAssemblyName = typeState.HomeAssemblyName
        };
    }
}
