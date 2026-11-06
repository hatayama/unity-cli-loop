using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        if (AddedFieldBodyScan.TryFindUnsupportedAddedFieldCompound(
                bodyNode,
                semanticModel,
                addedFieldCatalog,
                out string compoundFieldName,
                out SyntaxKind compoundKind))
        {
            // '??=' gets its own sentence because the rewrite that works is not obvious from the
            // general one: an explicit null guard around a simple assignment does apply.
            if (compoundKind == SyntaxKind.CoalesceAssignmentExpression)
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AddedFieldCoalesceAssignment,
                    compoundFieldName);
            }

            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                compoundFieldName);
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

        if (AddedFieldBodyScan.BodyDeconstructsIntoAddedField(bodyNode, semanticModel, addedFieldCatalog))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldDeconstructionTarget);
        }

        if (AddedFieldValueTypeWriteScan.BodyHasValueTypeAddedFieldMemberWrite(bodyNode, semanticModel, addedFieldCatalog))
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
    // a shim type, so even public instance members of the host are CS0103 / CS0026, and a member
    // this edit adds in the same file is on neither the compiled type nor the retained artifact
    // the shim references.
    internal static bool InitializerCannotEmitInShimLambda(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        INamedTypeSymbol hostType,
        WorkerTypeHome home,
        WorkerSourceUnit sourceUnit)
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

            // Asked before the element's own kind is decided: an element that is itself a creation
            // still reaches the collection through this Add.
            if (HasUnreachableCollectionAdd(node, semanticModel, home))
            {
                return true;
            }

            // Why object creation is decided on its own: a constructor is the one instance member
            // the lambda can call, and an inaccessible constructor leaves GetSymbolInfo without a
            // symbol, so the general check would let it through.
            if (node is BaseObjectCreationExpressionSyntax creation)
            {
                if (!IsEmittableConstruction(creation, semanticModel, home, sourceUnit))
                {
                    return true;
                }

                continue;
            }

            if (HasDisallowedInitializerSymbol(
                semanticModel.GetSymbolInfo(node).Symbol,
                hostType,
                home,
                semanticModel,
                sourceUnit))
            {
                return true;
            }
        }

        return false;
    }

    // Why two routes: the shim assembly references both the artifact a verified mapping names and
    // the assembly compiled before this edit, and those are the only places a constructor it can
    // call lives. A construction that reaches neither has nothing to run.
    private static bool IsEmittableConstruction(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        WorkerSourceUnit sourceUnit)
    {
        // Why a bound symbol that is public: this binding ignores accessibility and imports a
        // referenced assembly through its public surface. A protected constructor is on that
        // surface and binds, so it has to be refused by name. An internal or private one leaves no
        // symbol, and the candidate reported instead can be a public overload the arguments do not
        // fit, while the shim's binding sees the hidden one and would fail on the field's first read.
        IMethodSymbol constructor = semanticModel.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        INamedTypeSymbol constructedType = constructor?.ContainingType;
        if (constructedType == null
            || constructedType.ContainingAssembly == null
            || constructor.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        return IsIntroducedTypeConstruction(constructor, constructedType, semanticModel, sourceUnit)
            || IsCompiledTypeConstruction(creation, constructor, constructedType, semanticModel, home);
    }

    // The constructed type is exactly the (assembly, metadata name) pair the verified mapping
    // holds, or a declaration this same reload keeps editing on top of it.
    private static bool IsIntroducedTypeConstruction(
        IMethodSymbol constructor,
        INamedTypeSymbol constructedType,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
    {
        IntroducedTypeArtifactMap artifactMap = sourceUnit?.ArtifactMap;
        if (artifactMap == null)
        {
            return false;
        }

        if (artifactMap.FindNormalizedIdentity(
                constructedType.ContainingAssembly,
                CecilTypeNames.ToMetadataName(constructedType.OriginalDefinition)) != null)
        {
            return true;
        }

        return ConstructsRetainedSourceDeclaration(constructor, constructedType, semanticModel, sourceUnit);
    }

    private static bool IsCompiledTypeConstruction(
        BaseObjectCreationExpressionSyntax creation,
        IMethodSymbol constructor,
        INamedTypeSymbol constructedType,
        SemanticModel semanticModel,
        WorkerTypeHome home)
    {
        if (!IsVisibleAsCompiled(constructedType, home)
            || SpellsNestedTypeByBareName(creation, semanticModel))
        {
            return false;
        }

        INamedTypeSymbol compiledType = home.FindCompiledType(constructedType.OriginalDefinition);

        // A type this run has source for binds to that source, while the shim calls the compiled
        // one: a constructor the edit added is not there to be called.
        if (!constructedType.DeclaringSyntaxReferences.IsEmpty)
        {
            return compiledType != null && HasMatchingPublicConstructor(compiledType, constructor);
        }

        // Outside the edited assembly the shim sees the overloads this binding saw. Inside it, the
        // shim compile sees every member public while this binding saw the public surface, so a
        // hidden overload could win there and fail on the field's first read.
        return compiledType == null || !HasHiddenConstructor(compiledType);
    }

    private static bool HasHiddenConstructor(INamedTypeSymbol compiledType)
    {
        foreach (IMethodSymbol constructor in compiledType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    // Why the compiled declaration decides where there is one: the shim runs against it, so a
    // visibility the edit widened exists only in the source this binding read. A type argument and
    // an outer type are named by the shim as much as the type is.
    private static bool IsVisibleAsCompiled(ITypeSymbol type, WorkerTypeHome home)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsVisibleAsCompiled(arrayType.ElementType, home);
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return true;
        }

        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            if (!IsVisibleAsCompiled(typeArgument, home))
            {
                return false;
            }
        }

        if (namedType.ContainingType != null && !IsVisibleAsCompiled(namedType.ContainingType, home))
        {
            return false;
        }

        INamedTypeSymbol declaration = namedType.OriginalDefinition;
        return AccessibilityRules.IsExternallyVisibleType(home.FindCompiledType(declaration) ?? declaration);
    }

    // Why a bare nested name is refused: the initializer is emitted as the source spells it, on a
    // shim type that is not nested where the name resolves. 'new()' spells no name and takes the
    // store's fully qualified type argument, and 'Outer.Inner' resolves from the namespace.
    private static bool SpellsNestedTypeByBareName(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel)
    {
        if (creation is not ObjectCreationExpressionSyntax explicitCreation)
        {
            return false;
        }

        foreach (SimpleNameSyntax name in explicitCreation.Type.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            bool isRightOfQualifier = name.Parent is QualifiedNameSyntax qualified && qualified.Right == name;
            if (!isRightOfQualifier
                && semanticModel.GetSymbolInfo(name).Symbol is INamedTypeSymbol { ContainingType: not null })
            {
                return true;
            }
        }

        return false;
    }

    // Why the element is asked separately: a collection initializer binds its Add to each element
    // and names it nowhere the general walk can see. Why the edited assembly is ruled out: there the
    // shim compile sees every Add public, so the source's visibility or a hidden overload this
    // binding cannot see decides what runs, and it would fail only when the field is first read.
    private static bool HasUnreachableCollectionAdd(SyntaxNode node, SemanticModel semanticModel, WorkerTypeHome home)
    {
        if (node is not ExpressionSyntax element
            || !(node.Parent?.IsKind(SyntaxKind.CollectionInitializerExpression) ?? false))
        {
            return false;
        }

        ISymbol add = semanticModel.GetCollectionInitializerSymbolInfo(element).Symbol;
        return add == null
            || AccessibilityRules.IsInaccessibleFromExternalAssembly(add)
            || !add.ContainingType.DeclaringSyntaxReferences.IsEmpty
            || home.FindCompiledType(add.ContainingType.OriginalDefinition) != null;
    }

    // Why the source declaration counts as well: a type whose bodies this same reload edits keeps
    // its declaration in the binding tree, so the construction binds to the source symbol while
    // the artifact assembly the shim references is what serves the type at run time. Refusing it
    // would make the first body edit of an introduced type refuse every field initializer that
    // constructs it, for as long as the edited file differs from the compiled assembly.
    private static bool ConstructsRetainedSourceDeclaration(
        IMethodSymbol constructor,
        INamedTypeSymbol constructedType,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
    {
        INamedTypeSymbol artifactType = RetainedBodyEditHome.FindRunRetainedType(
            sourceUnit,
            semanticModel,
            constructedType.OriginalDefinition);
        if (artifactType == null)
        {
            return false;
        }

        // The constructor the lambda really runs is the artifact's, not the one bound from
        // source: a constructor this reload added to the type, or one whose accessibility the
        // source now spells differently, is not there to be called.
        return HasMatchingPublicConstructor(artifactType, constructor);
    }

    // Why the original definition: the candidates come from the open type, so a parameter spelled T
    // there is the type argument on a constructor bound through a constructed form.
    private static bool HasMatchingPublicConstructor(INamedTypeSymbol artifactType, IMethodSymbol constructor)
    {
        foreach (IMethodSymbol candidate in artifactType.InstanceConstructors)
        {
            if (candidate.DeclaredAccessibility == Accessibility.Public
                && ParameterTypesMatch(candidate, constructor.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    // Compared by display string because the two constructors come from different assemblies -
    // the artifact and the edited source - so the same type is not the same symbol on both sides.
    // The ref kind is part of the comparison because C# lets 'T(int)' and 'T(ref int)' overload
    // each other: without it an artifact holding only the by-value one would answer for a call
    // the domain cannot resolve.
    private static bool ParameterTypesMatch(IMethodSymbol artifactConstructor, IMethodSymbol constructor)
    {
        if (artifactConstructor.Parameters.Length != constructor.Parameters.Length)
        {
            return false;
        }

        for (int index = 0; index < constructor.Parameters.Length; index++)
        {
            IParameterSymbol artifactParameter = artifactConstructor.Parameters[index];
            IParameterSymbol sourceParameter = constructor.Parameters[index];
            if (artifactParameter.RefKind != sourceParameter.RefKind)
            {
                return false;
            }

            if (!string.Equals(
                    artifactParameter.Type.ToDisplayString(),
                    sourceParameter.Type.ToDisplayString(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasDisallowedInitializerSymbol(
        ISymbol symbol,
        INamedTypeSymbol hostType,
        WorkerTypeHome home,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
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

        // Why an anonymous function is let through: it is not a member of the host but a value
        // the static lambda creates, and every name inside its body is checked on its own node.
        if (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction })
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

        if (AddedFieldClassifier.IsSameFileAddedMember(symbol, home, semanticModel, sourceUnit))
        {
            return true;
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }
}
