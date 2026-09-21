using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class AddedFieldClassifier
{
    /// <summary>
    /// What: classifies source fields missing from the compiled type as added, and records
    /// store/const/unavailable bindings used by skip evaluation and body rewrite.
    /// </summary>
    internal static void ClassifyAddedFields(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        WorkerTypeHome home,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (FieldDeclarationSyntax fieldDeclaration in typeState.TypeDeclaration.Members
            .OfType<FieldDeclarationSyntax>())
        {
            foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
            {
                IFieldSymbol fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol == null)
                {
                    continue;
                }

                CompiledFieldMatch fieldMatch = CompiledMemberMatcher.MatchCompiledField(compiledType, fieldSymbol);
                if (fieldMatch == CompiledFieldMatch.Matched)
                {
                    continue;
                }

                ClassifyOneAddedField(
                    typeState,
                    semanticModel,
                    home,
                    fieldDeclaration,
                    variable,
                    fieldSymbol,
                    addedFieldCatalog,
                    fieldMatch);
            }
        }
    }

    internal static void ClassifyOneAddedField(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        FieldDeclarationSyntax fieldDeclaration,
        VariableDeclaratorSyntax variable,
        IFieldSymbol fieldSymbol,
        AddedFieldCatalog addedFieldCatalog,
        CompiledFieldMatch fieldMatch)
    {
        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeState.TypeMetadataNameFromSyntax, fieldSymbol.Name);
        string fieldKey = FormatAddedFieldStoreKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            fieldSymbol.Name);
        addedFieldCatalog.MarkClassifiedAdded(fieldKey);
        addedFieldCatalog.AddAddedSyntaxKey(syntaxKey);

        AddedFieldBinding binding = new AddedFieldBinding
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            FieldKey = fieldKey,
            SyntaxKey = syntaxKey,
            FieldName = fieldSymbol.Name,
            FieldType = fieldSymbol.Type,
            IsStatic = fieldSymbol.IsStatic,
            IsConst = fieldSymbol.IsConst,
            HasSerializationAttribute = AddedFieldSkipEvaluator.FieldHasSerializationAttribute(fieldDeclaration),
            ConstantValue = fieldSymbol.HasConstantValue ? fieldSymbol.ConstantValue : null,
            Initializer = variable.Initializer != null ? variable.Initializer.Value : null
        };

        WorkerReason declarationChangeReason = CompiledMemberMatcher.TryBuildCompiledFieldDeclarationChangeReason(
            fieldMatch,
            fieldSymbol.Name);
        if (declarationChangeReason != null)
        {
            // Why register as unavailable: the reason set here is what stops the field from
            // being rewritten to the side table, which would hide the declaration change and
            // leave compiled callers on the old field.
            binding.UnavailableReason = declarationChangeReason;
            addedFieldCatalog.Register(binding);
            return;
        }

        binding.UnavailableReason = EvaluateAddedFieldAvailability(
            typeState.TypeSymbol,
            semanticModel,
            home,
            fieldSymbol,
            binding,
            typeState.SourceUnit);

        if (binding.UnavailableReason != null)
        {
            addedFieldCatalog.Register(binding);
            return;
        }

        if (fieldSymbol.IsConst)
        {
            addedFieldCatalog.Register(binding);
            return;
        }

        addedFieldCatalog.Register(binding);
    }

    internal static WorkerReason EvaluateAddedFieldAvailability(
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        IFieldSymbol fieldSymbol,
        AddedFieldBinding binding,
        WorkerSourceUnit sourceUnit)
    {
        if (fieldSymbol.IsConst)
        {
            if (ConstantLiteralFactory.TryCreateConstantLiteral(binding.ConstantValue, fieldSymbol.Type) == null)
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                    fieldSymbol.Name);
            }

            return null;
        }

        // Why after const: added consts on struct hosts still fold to literals; the store
        // identity problem only applies to instance/static storage.
        AddedFieldStoreAvailability availability = EvaluateStoreAvailability(
            hostType,
            semanticModel,
            home,
            fieldSymbol.Type,
            binding.Initializer,
            sourceUnit,
            out ITypeSymbol unresolvedStoreType);
        return DescribeStoreAvailability(availability, unresolvedStoreType);
    }

    /// <summary>
    /// Reports whether a value can live in the added-field store, and on what grounds it cannot,
    /// for any member backed by it. Added auto-properties classify against this so their backing
    /// store follows the same rules as fields while wording the outcome as a property.
    /// </summary>
    // unresolvedType names the type the compilation could not resolve, and is set only for
    // ValueTypeUnresolved, whose reason has to repeat that name.
    internal static AddedFieldStoreAvailability EvaluateStoreAvailability(
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        ITypeSymbol valueType,
        ExpressionSyntax initializer,
        WorkerSourceUnit sourceUnit,
        out ITypeSymbol unresolvedType)
    {
        unresolvedType = null;
        if (hostType.TypeKind == TypeKind.Struct)
        {
            return AddedFieldStoreAvailability.StructHost;
        }

        // Why unresolved types before visibility: TypeKind.Error is not externally
        // visible, so the shim-visibility reason would hide a missing using or typo.
        if (TryFindUnresolvedType(valueType, out unresolvedType))
        {
            return AddedFieldStoreAvailability.ValueTypeUnresolved;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(valueType))
        {
            return AddedFieldStoreAvailability.ValueTypeNotExternallyVisible;
        }

        if (initializer != null
            && AddedFieldSkipEvaluator.InitializerCannotEmitInShimLambda(
                initializer,
                semanticModel,
                hostType,
                home,
                sourceUnit))
        {
            return AddedFieldStoreAvailability.InitializerNotEmittable;
        }

        return AddedFieldStoreAvailability.Available;
    }

    /// <summary>Words a store outcome as the skip reason an added field reports.</summary>
    private static WorkerReason DescribeStoreAvailability(
        AddedFieldStoreAvailability availability,
        ITypeSymbol unresolvedType)
    {
        switch (availability)
        {
            case AddedFieldStoreAvailability.Available:
                return null;
            case AddedFieldStoreAvailability.StructHost:
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldStructHost);
            case AddedFieldStoreAvailability.ValueTypeUnresolved:
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AddedFieldFieldTypeUnresolved,
                    unresolvedType.ToDisplayString());
            case AddedFieldStoreAvailability.ValueTypeNotExternallyVisible:
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldFieldTypeNotExternallyVisible);
            default:
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedFieldInitializerNotLiteralOrExternalStatic);
        }
    }

    // Why recurse array elements and type arguments: List<Missing> and Missing[]
    // would otherwise keep the shim-visibility reason even though the inner type is unresolved.
    /// <summary>
    /// Finds the first type in a value type, its element type, or its type arguments that the
    /// compilation could not resolve. Added properties reuse it to report the same cause.
    /// </summary>
    internal static bool TryFindUnresolvedType(ITypeSymbol typeSymbol, out ITypeSymbol unresolvedType)
    {
        unresolvedType = null;
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol.TypeKind == TypeKind.Error)
        {
            unresolvedType = typeSymbol;
            return true;
        }

        if (typeSymbol is ITypeParameterSymbol)
        {
            return false;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return TryFindUnresolvedType(arrayType.ElementType, out unresolvedType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (TryFindUnresolvedType(typeArgument, out unresolvedType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static string FormatAddedFieldStoreKey(string typeMetadataName, string fieldName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + fieldName;
    }

    internal static bool IsSameFileAddedMember(
        ISymbol symbol,
        WorkerTypeHome home,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
    {
        SyntaxTree currentTree = semanticModel?.SyntaxTree;
        if (symbol.ContainingType == null || currentTree == null)
        {
            return false;
        }

        bool declaredInCurrentTree = false;
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == currentTree)
            {
                declaredInCurrentTree = true;
                break;
            }
        }

        if (!declaredInCurrentTree)
        {
            return false;
        }

        // Why the artifact counts here: a type an earlier reload introduced runs from that
        // artifact, and every member the artifact holds is one the shim assembly can reference.
        // Only a type neither the patch target nor an active artifact holds is introduced by
        // this edit, and only then is every member of it added in the same file.
        INamedTypeSymbol compiledType = home.FindCompiledType(symbol.ContainingType)
            ?? RetainedBodyEditHome.FindRetainedType(sourceUnit, semanticModel, symbol.ContainingType);
        if (compiledType == null)
        {
            return true;
        }

        if (symbol is IFieldSymbol fieldSymbol)
        {
            // Why map any non-Matched result to added: FieldTypeChanged,
            // FieldModifiersChanged, and MemberKindChanged still name compiled
            // storage, so treating them as a direct shim reference would bind it.
            return CompiledMemberMatcher.MatchCompiledField(compiledType, fieldSymbol) != CompiledFieldMatch.Matched;
        }

        if (symbol is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
        {
            CompiledMethodMatch match = CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
            // Why map ReturnTypeChanged to added: the compiled method still has the old
            // signature, so treating it as a direct shim reference would bind the old body.
            return match != CompiledMethodMatch.Matched;
        }

        foreach (ISymbol member in compiledType.GetMembers(symbol.Name))
        {
            if (member.Kind == symbol.Kind)
            {
                return false;
            }
        }

        return true;
    }
}
