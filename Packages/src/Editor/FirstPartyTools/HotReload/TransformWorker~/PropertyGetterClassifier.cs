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

internal static class PropertyGetterClassifier
{
    internal static bool TryRecordUnchangedPropertyGetter(
        string sourceProjectRelativePath,
        bool hasBaseline,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        string typeMetadataNameFromSyntax,
        PropertyDeclarationSyntax propertyDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol getterSymbol,
        string getterRowName,
        string[] parameterTypeFullNames,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        if (!hasBaseline
            || snapshotPropertyMap == null
            || plainCurrentPropertyMap == null)
        {
            return false;
        }

        string propertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
        if (snapshotPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax snapshotProperty)
            && plainCurrentPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax plainProperty)
            && ArePropertyGettersEquivalent(snapshotProperty, plainProperty))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                MethodName = getterRowName,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = getterSymbol.Arity
            });
            return true;
        }

        return false;
    }

    // What: why a getter of a compiled property cannot be patched, or null when it can.
    // Why each case: an entry for a getter the compiled property lacks (a getter added to a
    // write-only property, or one whose property changed its type or static modifier) fails the
    // Editor's lookup or patches a method of another signature, while
    // Skipped keeps pointing the reader at 'uloop compile'. An explicit getter on a type a retained
    // artifact serves stays Skipped as its setter does: explicit accessors are patched only on
    // compiled types.
    internal static WorkerReason DescribeUnpatchableGetterOrNull(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol compiledType,
        string homeAssemblyName)
    {
        bool isExplicit = propertySymbol.ExplicitInterfaceImplementations.Length > 0;
        if (isExplicit && homeAssemblyName != null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation);
        }

        if (compiledType == null || homeAssemblyName != null)
        {
            return null;
        }

        IPropertySymbol compiledProperty = isExplicit
            ? ExplicitImplementationMatcher.FindCompiledPropertyImplementation(compiledType, propertySymbol)
            : compiledType.GetMembers(propertySymbol.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate => !candidate.IsIndexer);
        if (compiledProperty == null || HasMatchingCompiledGetter(compiledProperty, propertySymbol))
        {
            return null;
        }

        return WorkerReason.Of(isExplicit
            ? HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation
            : HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor);
    }

    // Why each check: a getter added since the last compile, and a property re-typed, made
    // (non-)static, or switched to or from returning by reference since then, all lack a compiled
    // get_X with this signature, as for the setter. RefKind is compared on its own because the
    // Cecil type name is the same for int and ref int.
    private static bool HasMatchingCompiledGetter(IPropertySymbol compiledProperty, IPropertySymbol propertySymbol)
    {
        return compiledProperty.GetMethod != null
            && compiledProperty.IsStatic == propertySymbol.IsStatic
            && compiledProperty.RefKind == propertySymbol.RefKind
            && CecilTypeNames.ToCecilFullName(compiledProperty.Type) == CecilTypeNames.ToCecilFullName(propertySymbol.Type);
    }

    // What: runs a property accessor body through the same transform decision and name/added-call
    // guards as an ordinary method, adding the skip row itself when one applies.
    internal static (bool SkipAccessor, MethodTransformDecision Decision) TrySkipPropertyAccessorByDecision(
        string sourceProjectRelativePath,
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol accessorSymbol,
        SyntaxNode accessorBodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        AddedEventLookup addedEvents,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        PartialTypeParts partialTypeParts,
        IAssemblySymbol targetAssembly,
        List<WorkerSkipped> skipped)
    {
        MethodTransformDecision decision = MethodTransformDecider.DecideMethodTransform(
            typeDeclaration,
            typeSymbol,
            methodDeclaration: null,
            accessorSymbol,
            accessorBodyNode,
            semanticModel,
            compiledType,
            addedMemberAccess: null,
            addedEvents,
            partialTypeParts,
            targetAssembly);
        if (decision.SkipReason != null)
        {
            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                Method = WorkerMethodKeys.FormatRowLabel(compiledType, accessorSymbol),
                Reason = decision.SkipReason
            });
            return (true, decision);
        }

        WorkerReason unresolvedNameSkip = UnresolvedBodyNameGuard.DescribeSkipOrNull(
            typeDeclaration,
            semanticModel,
            accessorBodyNode,
            methodDeclarationOrNull: null,
            decision,
            typeSymbol,
            targetAssembly);
        if (unresolvedNameSkip != null)
        {
            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                Method = WorkerMethodKeys.FormatRowLabel(compiledType, accessorSymbol),
                Reason = unresolvedNameSkip
            });
            return (true, MethodTransformDecision.Skip(unresolvedNameSkip));
        }

        (WorkerReason addedCallSiteSkip, string calledAddedMethodKey) = AddedCallSiteGuard.EvaluateAddedCallSiteSkipReason(
            accessorBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            typeSymbol);
        if (addedCallSiteSkip == null)
        {
            return (false, decision);
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            Method = WorkerMethodKeys.FormatRowLabel(compiledType, accessorSymbol),
            Reason = addedCallSiteSkip,
            CalledAddedMethodKey = calledAddedMethodKey,
            MethodKey = calledAddedMethodKey == null
                ? null
                : WorkerMethodKeys.BuildRowMethodKey(compiledType, accessorSymbol)
        });
        return (true, decision);
    }

    internal static (bool HasGetterBody, AccessorDeclarationSyntax GetAccessor) TryGetPropertyGetterBody(
        PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.ExpressionBody != null)
        {
            return (true, null);
        }

        if (propertyDeclaration.AccessorList == null)
        {
            return (false, null);
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }

            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                return (false, null);
            }

            return (true, accessor);
        }

        return (false, null);
    }

    // Why getter-only: whole-property AreEquivalent treats setter edits as getter changes and
    // would emit a useless Patched get_ row beside Skipped set_.
    internal static bool ArePropertyGettersEquivalent(
        PropertyDeclarationSyntax snapshotProperty,
        PropertyDeclarationSyntax currentProperty)
    {
        return SyntaxFactory.AreEquivalent(
            NormalizePropertyToGetterShape(snapshotProperty),
            NormalizePropertyToGetterShape(currentProperty),
            topLevel: false);
    }

    internal static PropertyDeclarationSyntax NormalizePropertyToGetterShape(
        PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.ExpressionBody != null)
        {
            return propertyDeclaration.WithAccessorList(null);
        }

        if (propertyDeclaration.AccessorList == null)
        {
            return propertyDeclaration;
        }

        List<AccessorDeclarationSyntax> getAccessors = new List<AccessorDeclarationSyntax>();
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                getAccessors.Add(accessor);
            }
        }

        return propertyDeclaration.WithAccessorList(
            SyntaxFactory.AccessorList(SyntaxFactory.List(getAccessors)));
    }

    internal static void SkipPropertyGetterOnUncompiledType(
        string sourceProjectRelativePath,
        PropertyDeclarationSyntax propertyDeclaration,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || propertySymbol.GetMethod == null)
        {
            return;
        }

        (bool hasGetterBody, AccessorDeclarationSyntax _) =
            TryGetPropertyGetterBody(propertyDeclaration);
        if (!hasGetterBody)
        {
            return;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            Method = WorkerMethodKeys.FormatMethodLabel(propertySymbol.GetMethod),
            Reason = WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced)
        });
    }
}
