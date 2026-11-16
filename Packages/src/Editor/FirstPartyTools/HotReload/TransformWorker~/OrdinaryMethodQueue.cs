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

internal static class OrdinaryMethodQueue
{
    internal static void QueueOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        ShimNameAllocator shimNames)
    {
        IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        // Why the row name: the Editor builds excluded keys from the compiled method's name, which
        // for an explicit implementation can differ from the source spelling (global::).
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            ExplicitImplementationMatcher.RowMethodName(compiledType, methodSymbol),
            parameterTypeFullNames,
            methodSymbol.Arity);
        (bool isAddedMethod, bool replacesCompiledMethod) = ClassifyOrdinaryMethodAddedState(
            methodDeclaration,
            compiledType,
            methodSymbol);
        bool bodylessNotCompared = IsBodylessCompiledMethodWithoutBaseline(
            isAddedMethod,
            hasBaseline,
            methodDeclaration,
            compiledType,
            methodSymbol);
        if (TrySkipExcludedOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            methodDeclaration,
            typeState,
            input,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog))
        {
            return;
        }

        string syntaxMethodKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(
            typeState.TypeMetadataNameFromSyntax,
            methodDeclaration);
        if (TrySkipInterfaceOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            hasBaseline,
            syntaxMethodKey,
            bodylessNotCompared,
            methodSymbol,
            typeState,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog,
            skipped))
        {
            return;
        }

        if (UnchangedOrdinaryMethodRecorder.TryRecordRetainedUnchangedOrdinaryMethod(
            isAddedMethod,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            parameterTypeFullNames,
            unchangedMethods))
        {
            return;
        }

        if (UnchangedOrdinaryMethodRecorder.TryRecordUnchangedOrdinaryMethod(
            isAddedMethod,
            hasBaseline,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            parameterTypeFullNames,
            snapshotMethodMap,
            plainCurrentMethodMap,
            unchangedMethods))
        {
            return;
        }

        MethodTransformDecision decision = DecideOrdinaryMethodTransform(
            isAddedMethod,
            methodDeclaration,
            methodSymbol,
            typeState,
            semanticModel);
        if (decision.SkipReason != null)
        {
            // Why every skip reason is replaced, hard skips included: a member with no body has
            // nothing to patch, so a reason that blames the host type or asks for a compile misleads.
            WorkerReason skipReason = bodylessNotCompared
                ? WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBodylessNotCompared)
                : decision.SkipReason;
            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
                Method = WorkerMethodKeys.FormatRowLabel(typeState.CompiledType, methodSymbol),
                Reason = skipReason
            });
            if (isAddedMethod)
            {
                // Why strip skipped added declarations: otherwise drift warns about
                // fields/initializers for a method the skip reason already explained.
                RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    syntaxMethodKey,
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
            }

            return;
        }

        QueueDecidedOrdinaryMethod(
            methodDeclaration,
            methodSymbol,
            decision,
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            syntaxMethodKey,
            parameterTypeFullNames,
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            addedMethodCatalog,
            snapshotMethodMap,
            plainCurrentMethodMap,
            declarationDriftWarnings,
            removedMembers,
            removedMethodSignatures,
            shimNames);
    }

    internal static (bool IsAddedMethod, bool ReplacesCompiledMethod) ClassifyOrdinaryMethodAddedState(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol compiledType,
        IMethodSymbol methodSymbol)
    {
        // Why match by the implemented member: the metadata name follows the specifier's
        // spelling, so a name lookup would call a respelled implementation added. A changed
        // signature is a different member, so an explicit implementation never replaces one.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return (ExplicitImplementationMatcher.FindCompiledImplementation(compiledType, methodSymbol) == null, false);
        }

        CompiledMethodMatch compiledMatch = CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
        return (
            compiledMatch != CompiledMethodMatch.Matched,
            compiledMatch == CompiledMethodMatch.ReturnTypeChanged);
    }

    // Why the compiled name for an explicit implementation: the Editor finds the method by the name
    // the compiled assembly holds, and the source name changes with the specifier's spelling.
    private static string ResolveEntryMethodName(
        bool isExplicitImplementation,
        bool isAddedMethod,
        TypeEmitState typeState,
        IMethodSymbol methodSymbol)
    {
        if (!isExplicitImplementation || isAddedMethod)
        {
            return methodSymbol.Name;
        }

        IMethodSymbol compiledMethod = ExplicitImplementationMatcher.FindCompiledImplementation(typeState.CompiledType, methodSymbol);
        if (compiledMethod == null)
        {
            throw new InvalidOperationException(
                "An explicit implementation classified as compiled has no compiled counterpart: "
                + methodSymbol.ToDisplayString());
        }

        return compiledMethod.MetadataName;
    }

    internal static bool TrySkipExcludedOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        WorkerInput input,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (isAddedMethod)
        {
            addedMethodCatalog.MarkClassifiedAdded(methodKey);
            if (input.ExcludedAddedMethodKeys.Contains(methodKey))
            {
                RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    WorkerSyntaxIndex.BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration),
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
                return true;
            }

            return false;
        }

        return input.ExcludedMethodKeys.Contains(methodKey);
    }

    internal static bool TrySkipInterfaceOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        bool bodylessNotCompared,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped)
    {
        if (typeState.TypeSymbol.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (!isAddedMethod && hasBaseline
            && snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            // Why not unchangedMethods: RevertUnchangedPatches Resolve/ReadAssembly is
            // wasted for members Harmony will never patch. Stay inert.
            return true;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            Method = WorkerMethodKeys.FormatRowLabel(typeState.CompiledType, methodSymbol),
            Reason = bodylessNotCompared
                ? WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBodylessNotCompared)
                : WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodInterfaceMember)
        });
        if (isAddedMethod)
        {
            RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
        }

        return true;
    }

    /// <summary>
    /// True when a compiled method without a body could not be compared with the compiled source:
    /// there is nothing to patch, and an edit to its default values, attributes or parameter names
    /// cannot be told apart from no edit.
    /// </summary>
    /// <remarks>
    /// With a baseline the change is known, so the existing reasons stay. An added method needs a
    /// compile whatever its body. An explicit interface implementation is matched by the interface
    /// member it implements rather than by name. The compiled method must lack a body as well:
    /// the signature match ignores abstract and extern, so an edit that removes a body (virtual to
    /// abstract, a method made extern, a default interface method made bodyless) leaves the
    /// compiled body running until a compile.
    /// </remarks>
    internal static bool IsBodylessCompiledMethodWithoutBaseline(
        bool isAddedMethod,
        bool hasBaseline,
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol compiledType,
        IMethodSymbol methodSymbol)
    {
        if (hasBaseline || isAddedMethod)
        {
            return false;
        }

        if (methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null)
        {
            return false;
        }

        IMethodSymbol compiledMethod = FindCompiledCounterpart(methodDeclaration, compiledType, methodSymbol);
        if (compiledMethod == null)
        {
            throw new InvalidOperationException(
                "A method classified as compiled has no compiled counterpart: " + methodSymbol.ToDisplayString());
        }

        return compiledMethod.IsAbstract || compiledMethod.IsExtern;
    }

    /// <summary>The compiled method a source method classified as compiled replaces; null when there is none.</summary>
    internal static IMethodSymbol FindCompiledCounterpart(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol compiledType,
        IMethodSymbol methodSymbol)
    {
        return methodDeclaration.ExplicitInterfaceSpecifier != null
            ? ExplicitImplementationMatcher.FindCompiledImplementation(compiledType, methodSymbol)
            : CompiledMemberMatcher.FindCompiledOrdinaryMethod(compiledType, methodSymbol);
    }

    internal static MethodTransformDecision DecideOrdinaryMethodTransform(
        bool isAddedMethod,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        SemanticModel semanticModel)
    {
        SyntaxNode methodBodyNode =
            (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        WorkerReason addedSkip = isAddedMethod
            ? MethodTransformDecider.EvaluateAddedMethodSkipReason(methodSymbol, methodDeclaration)
            : null;
        WorkerReason earlySkip = addedSkip
            ?? BaseCallRules.EvaluateHostSkipReason(
                isAddedMethod,
                typeState.HomeAssemblyName != null,
                methodBodyNode,
                semanticModel);
        MethodTransformDecision decision = earlySkip != null
            ? MethodTransformDecision.Skip(earlySkip)
            : MethodTransformDecider.DecideMethodTransform(
                typeState.TypeDeclaration,
                typeState.TypeSymbol,
                methodDeclaration,
                methodSymbol,
                methodBodyNode,
                semanticModel,
                typeState.CompiledType,
                typeState.AddedMemberAccess,
                typeState.AddedEvents,
                typeState.SourceUnit.PartialTypeParts);
        if (isAddedMethod && decision.SkipReason == null)
        {
            decision = MethodTransformDecider.DecideAddedMethodAccessors(
                methodSymbol,
                typeState.TypeSymbol,
                methodBodyNode,
                semanticModel,
                decision,
                typeState.AddedMemberAccess,
                typeState.SourceUnit,
                typeState.TargetAssembly);
        }

        if (!isAddedMethod && decision.SkipReason == null && typeState.SourceUnit.Input.ReappliedSibling)
        {
            WorkerReason siblingSkip = ReappliedSiblingBodyGuard.DescribeSkipOrNull(
                semanticModel,
                methodBodyNode,
                typeState.TargetAssembly,
                methodDeclaration,
                decision,
                typeState.TypeSymbol);
            if (siblingSkip != null)
            {
                decision = MethodTransformDecision.Skip(siblingSkip);
            }
        }

        // Why existing methods only: an added method's unbound body is already skipped by the
        // added-member binding check, with its own reason.
        if (!isAddedMethod && decision.SkipReason == null)
        {
            WorkerReason unresolvedNameSkip = UnresolvedBodyNameGuard.DescribeSkipOrNull(
                typeState.TypeDeclaration,
                semanticModel,
                methodBodyNode,
                methodDeclaration,
                decision,
                typeState.TypeSymbol,
                typeState.TargetAssembly);
            if (unresolvedNameSkip != null)
            {
                decision = MethodTransformDecision.Skip(unresolvedNameSkip);
            }
        }

        return decision;
    }

    internal static void QueueDecidedOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        MethodTransformDecision decision,
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        string syntaxMethodKey,
        string[] parameterTypeFullNames,
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        ShimNameAllocator shimNames)
    {
        ShimTypeBuilder shimType = OrdinaryMethodShimTypes.EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimNames);
        // Why the interface member's name: an explicit implementation's own name is dotted
        // (Ns.IFoo.Run), which is not a legal shim identifier.
        bool isExplicitImplementation = methodDeclaration.ExplicitInterfaceSpecifier != null;
        string shimMethodName = shimNames.NextShimMethodName(
            isExplicitImplementation ? ExplicitImplementationMatcher.ImplementedMemberName(methodSymbol) : methodSymbol.Name);

        FileLinePositionSpan originalSpan = methodDeclaration.GetLocation().GetLineSpan();
        QueuedShimMethod queued = new QueuedShimMethod
        {
            MethodDeclaration = methodDeclaration,
            MethodSymbol = methodSymbol,
            Decision = decision,
            ShimMethodName = shimMethodName,
            EntryMethodName = ResolveEntryMethodName(isExplicitImplementation, isAddedMethod, typeState, methodSymbol),
            ShimType = shimType,
            SourceStartLine = originalSpan.StartLinePosition.Line + 1,
            SourceEndLine = originalSpan.EndLinePosition.Line + 1,
            ParameterTypeFullNames = parameterTypeFullNames,
            MethodKey = methodKey,
            IsAddedMethod = isAddedMethod,
            ReplacesCompiledMethod = replacesCompiledMethod
        };
        typeState.QueuedMethods.Add(queued);

        if (replacesCompiledMethod)
        {
            RemovedMemberCollector.AddRemovedMethodName(removedMembers, methodSymbol.Name);
            RemovedMemberCollector.AddRemovedMethodSignature(
                removedMethodSignatures,
                typeState.TypeSymbol,
                methodSymbol.Name,
                parameterTypeFullNames,
                methodSymbol.Arity);
        }

        if (isAddedMethod)
        {
            addedMethodCatalog.Register(
                new AddedMethodBinding
                {
                    MethodKey = methodKey,
                    ShimTypeName = shimType.ShimTypeName,
                    ShimMethodName = shimMethodName,
                    NamespaceName = shimType.NamespaceName,
                    IsStatic = methodSymbol.IsStatic,
                    ParameterCount = methodSymbol.Parameters.Length
                });
            RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
            TestAttributeNames.AppendAddedTestMethodWarningIfNeeded(
                methodDeclaration,
                typeState.TypeSymbol,
                methodSymbol,
                declarationDriftWarnings);
        }
    }
}
