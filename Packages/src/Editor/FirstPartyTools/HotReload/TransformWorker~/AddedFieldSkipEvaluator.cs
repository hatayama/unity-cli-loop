using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Decides when an added field must be left to a full compile rather than patched: the body that
// touches it may run before the field exists, and its initializer may name things the shim cannot
// see. Kept apart from classifying the field itself so each reads on its own.
internal static class AddedFieldSkipEvaluator
{
    internal static WorkerReason EvaluateAddedFieldSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null || addedFieldCatalog == null || !addedFieldCatalog.HasClassifiedAdded)
        {
            return null;
        }

        WorkerReason unavailable = AddedFieldBodyScan.BodyReferencesUnavailableAddedField(bodyNode, semanticModel, addedFieldCatalog);
        if (unavailable != null)
        {
            return unavailable;
        }

        if (AddedFieldBodyScan.BodyPassesAddedFieldByRef(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldRefOutIn);
        }

        if (AddedFieldBodyScan.BodyHasUnsupportedAddedFieldCompound(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField);
        }

        if (AddedFieldBodyScan.BodyHasNonNumericAddedFieldIncrement(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldIncrementNotNumeric);
        }

        if (AddedFieldBodyScan.BodyHasConsumedAddedFieldWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldConsumedWrite);
        }

        if (AddedFieldBodyScan.BodyHasDoubleEvalAddedFieldReceiver(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldDoubleEvalReceiver);
        }

        if (AddedFieldBodyScan.BodyHasValueTypeAddedFieldMemberWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldValueTypeMemberWrite);
        }

        return null;
    }

    internal static bool FieldHasSerializationAttribute(FieldDeclarationSyntax fieldDeclaration)
    {
        foreach (AttributeListSyntax attributeList in fieldDeclaration.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                string name = attribute.Name.ToString();
                int lastDot = name.LastIndexOf('.');
                string simpleName = lastDot >= 0 ? name.Substring(lastDot + 1) : name;
                if (simpleName == "SerializeField"
                    || simpleName == "SerializeReference"
                    || simpleName == "FormerlySerializedAs")
                {
                    return true;
                }
            }
        }

        return false;
    }


    // Why this gate (not inaccessible-only): the initializer is spliced into a static lambda on
    // a shim type, so even public instance members of the host are CS0103 / CS0026, and
    // same-file added members do not exist on the compiled type the shim references.
    internal static bool InitializerCannotEmitInShimLambda(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        INamedTypeSymbol hostType,
        WorkerTypeHome home,
        IntroducedTypeArtifactMap artifactMap)
    {
        foreach (SyntaxNode node in initializer.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (node is ThisExpressionSyntax || node is BaseExpressionSyntax)
            {
                return true;
            }

            // Why object creation is decided on its own: the constructor of an introduced type
            // is the one instance member the lambda can call, and an inaccessible constructor
            // leaves GetSymbolInfo without a symbol, so the general check would let it through.
            if (node is ObjectCreationExpressionSyntax creation)
            {
                if (!IsIntroducedTypeConstruction(creation, semanticModel, artifactMap))
                {
                    return true;
                }

                continue;
            }

            if (HasDisallowedInitializerSymbol(
                semanticModel.GetSymbolInfo(node).Symbol,
                hostType,
                home,
                initializer.SyntaxTree))
            {
                return true;
            }
        }

        return false;
    }

    // The three conditions a construction has to meet: it names a constructor, the constructor is
    // public, and the constructed type is exactly the (assembly, metadata name) pair the verified
    // mapping holds. The shim compilation references that artifact assembly, so such a type is
    // reachable from the lambda; anything else is not.
    private static bool IsIntroducedTypeConstruction(
        ObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        IntroducedTypeArtifactMap artifactMap)
    {
        if (artifactMap == null)
        {
            return false;
        }

        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(creation);
        IMethodSymbol constructor = symbolInfo.Symbol as IMethodSymbol;
        if (constructor == null)
        {
            // A constructor the shim assembly cannot reach is reported as a candidate rather
            // than as the symbol, and it still has to be refused rather than ignored.
            foreach (ISymbol candidate in symbolInfo.CandidateSymbols)
            {
                constructor = candidate as IMethodSymbol;
                if (constructor != null)
                {
                    break;
                }
            }
        }

        if (constructor == null
            || constructor.MethodKind != MethodKind.Constructor
            || constructor.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        INamedTypeSymbol constructedType = constructor.ContainingType;
        if (constructedType == null || constructedType.ContainingAssembly == null)
        {
            return false;
        }

        return artifactMap.FindNormalizedIdentity(
            constructedType.ContainingAssembly,
            CecilTypeNames.ToMetadataName(constructedType.OriginalDefinition)) != null;
    }

    internal static bool HasDisallowedInitializerSymbol(
        ISymbol symbol,
        INamedTypeSymbol hostType,
        WorkerTypeHome home,
        SyntaxTree currentTree)
    {
        if (symbol == null
            || symbol is INamespaceSymbol
            || symbol is ITypeSymbol
            || symbol is ILabelSymbol
            || symbol is IRangeVariableSymbol)
        {
            return false;
        }

        if (symbol is not IFieldSymbol
            && symbol is not IPropertySymbol
            && symbol is not IMethodSymbol
            && symbol is not IEventSymbol)
        {
            return false;
        }

        if (!symbol.IsStatic)
        {
            return true;
        }

        if (hostType != null
            && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, hostType))
        {
            return true;
        }

        if (AddedFieldClassifier.IsSameFileAddedMember(symbol, home, currentTree))
        {
            return true;
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }
}
