using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Decides when an added property must be left to a full compile rather than patched, and where an
// auto-property's value is kept when it can be patched. Kept apart from classifying the property
// itself so each reads on its own.
internal static class AddedPropertySkipEvaluator
{
    // Why before every other rule: a compiled field or event of the same name still owns the
    // member, so emitting accessors would leave compiled code on the old storage while edited
    // code reads a side table that never sees the compiled value.
    internal static WorkerReason EvaluateCompiledMemberKindChangeReason(
        INamedTypeSymbol compiledType,
        string propertyName)
    {
        foreach (ISymbol member in compiledType.GetMembers(propertyName))
        {
            if (member is IFieldSymbol || member is IEventSymbol)
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AddedPropertyCompiledMemberKindChanged,
                    propertyName);
            }
        }

        return null;
    }

    internal static bool HasCompiledProperty(INamedTypeSymbol compiledType, string propertyName)
    {
        foreach (ISymbol member in compiledType.GetMembers(propertyName))
        {
            if (member is IPropertySymbol)
            {
                return true;
            }
        }

        return false;
    }


    // Why the shared store rules: an auto-property's backing value lives in the same
    // side table as an added field, so it must be rejected on exactly the same grounds.
    internal static WorkerReason EvaluateAutoPropertyStoreSkipReason(
        PropertyDeclarationSyntax declaration,
        IPropertySymbol symbol,
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        IntroducedTypeArtifactMap artifactMap)
    {
        AddedFieldStoreAvailability availability = AddedFieldClassifier.EvaluateStoreAvailability(
            hostType,
            semanticModel,
            home,
            symbol.Type,
            declaration.Initializer?.Value,
            artifactMap,
            out ITypeSymbol _);
        switch (availability)
        {
            case AddedFieldStoreAvailability.Available:
                return null;
            case AddedFieldStoreAvailability.StructHost:
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyStructHost);
            case AddedFieldStoreAvailability.InitializerNotEmittable:
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyInitializerNotEmittable);
            default:
                // The declaration check already rejects unresolved and non-visible value types, so
                // anything left names a type the shim assembly cannot see.
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyValueTypeNotExternallyVisible);
        }
    }

    // Why register here and not at rewrite time: the accessor shims themselves read and write
    // the store, so an emitted auto-property always rewrites, unlike an added field that a body
    // may never touch.
    internal static void RegisterAutoPropertyStore(
        AddedPropertyBinding binding,
        AddedFieldCatalog addedFieldCatalog)
    {
        binding.Initializer = binding.Declaration.Initializer?.Value;

        // Why not AddAddedSyntaxKey: the field syntax-key set drives field drift stripping,
        // while the property declaration is already registered as an added property key.
        addedFieldCatalog.Register(new AddedFieldBinding
        {
            SourceProjectRelativePath = binding.SourceProjectRelativePath,
            FieldKey = binding.PropertyKey,
            SyntaxKey = binding.SyntaxKey,
            FieldName = binding.Name,
            FieldType = binding.ValueType,
            IsStatic = binding.IsStatic,
            IsConst = false,
            Initializer = binding.Initializer
        });
        addedFieldCatalog.MarkStoreRewrite(binding.PropertyKey);
    }

    internal static WorkerReason EvaluateDeclarationSkipReason(
        IPropertySymbol symbol,
        PropertyDeclarationSyntax declaration,
        INamedTypeSymbol hostType)
    {
        // Why generic hosts first: the CLR gives every closed instantiation its own statics, so a
        // single accessor identity and a single store entry would let Host<int> and Host<string>
        // read and write the same value.
        if (hostType.IsGenericType)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyGenericHostType);
        }

        if (hostType.TypeKind == TypeKind.Struct)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyStructHost);
        }

        if (hostType.TypeKind == TypeKind.Interface
            || symbol.IsVirtual
            || symbol.IsOverride
            || symbol.IsAbstract)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyVirtualOrAbstract);
        }

        if (declaration.ExplicitInterfaceSpecifier != null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyExplicitInterface);
        }

        if (HasInitAccessor(declaration))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyInitAccessor);
        }

        if (symbol.ReturnsByRef || symbol.ReturnsByRefReadonly)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyRefOutIn);
        }

        // Why unresolved types before visibility: TypeKind.Error is not externally visible, so the
        // shim-visibility reason would hide a missing using directive or a typo in the declaration.
        if (AddedFieldClassifier.TryFindUnresolvedType(symbol.Type, out ITypeSymbol unresolvedType))
        {
            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeUnresolved,
                unresolvedType.ToDisplayString());
        }

        if (!AccessibilityRules.IsExternallyVisibleType(symbol.Type))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyValueTypeNotExternallyVisible);
        }

        return null;
    }

    private static bool HasInitAccessor(PropertyDeclarationSyntax declaration)
    {
        if (declaration.AccessorList == null)
        {
            return false;
        }

        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
            {
                return true;
            }
        }

        return false;
    }
}
