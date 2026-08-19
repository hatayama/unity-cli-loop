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

internal static class PropertyGetterClassifier
{
    internal static bool TryRecordUnchangedPropertyGetter(
        bool hasBaseline,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        string typeMetadataNameFromSyntax,
        PropertyDeclarationSyntax propertyDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol getterSymbol,
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
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                MethodName = getterSymbol.Name,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = getterSymbol.Arity
            });
            return true;
        }

        return false;
    }

    internal static bool TrySkipAddedProperty(
        bool hasBaseline,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        string typeMetadataNameFromSyntax,
        PropertyDeclarationSyntax propertyDeclaration,
        IMethodSymbol getterSymbol,
        List<WorkerSkipped> skipped,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (!hasBaseline
            || snapshotPropertyMap == null
            || plainCurrentPropertyMap == null)
        {
            return false;
        }

        string addedPropertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
        if (snapshotPropertyMap.ContainsKey(addedPropertyKey))
        {
            return false;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = TransformWorkerProgram.FormatMethodLabel(getterSymbol),
            Reason = AddedMethodSkipReasons.AddedProperty
        });
        addedMethodCatalog.AddAddedPropertySyntaxKey(addedPropertyKey);
        return true;
    }

    internal static (bool SkipGetter, MethodTransformDecision Decision) TrySkipPropertyGetterByDecision(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol getterSymbol,
        SyntaxNode getterBodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        MethodTransformDecision decision = MethodTransformDecider.DecideMethodTransform(
            typeDeclaration,
            typeSymbol,
            methodDeclaration: null,
            getterSymbol,
            getterBodyNode,
            semanticModel);
        if (decision.SkipReason != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = TransformWorkerProgram.FormatMethodLabel(getterSymbol),
                Reason = decision.SkipReason
            });
            return (true, decision);
        }

        (string addedCallSiteSkip, string calledAddedMethodKey) = TransformWorkerProgram.EvaluateAddedCallSiteSkipReason(
            getterBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog);
        if (addedCallSiteSkip == null)
        {
            return (false, decision);
        }

        skipped.Add(new WorkerSkipped
        {
            Method = TransformWorkerProgram.FormatMethodLabel(getterSymbol),
            Reason = addedCallSiteSkip,
            CalledAddedMethodKey = calledAddedMethodKey,
            MethodKey = calledAddedMethodKey == null
                ? null
                : TransformWorkerProgram.BuildMethodKeyFromSymbol(getterSymbol)
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
            Method = TransformWorkerProgram.FormatMethodLabel(propertySymbol.GetMethod),
            Reason = AddedMethodSkipReasons.NewTypeOutOfScope
        });
    }
}
