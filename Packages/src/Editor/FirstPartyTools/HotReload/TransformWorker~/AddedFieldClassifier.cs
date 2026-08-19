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
        IAssemblySymbol targetTypesAssemblySymbol,
        AddedFieldCatalog addedFieldCatalog,
        List<string> declarationDriftWarnings)
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
                    targetTypesAssemblySymbol,
                    fieldDeclaration,
                    variable,
                    fieldSymbol,
                    addedFieldCatalog,
                    declarationDriftWarnings,
                    fieldMatch);
            }
        }
    }

    internal static void ClassifyOneAddedField(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        FieldDeclarationSyntax fieldDeclaration,
        VariableDeclaratorSyntax variable,
        IFieldSymbol fieldSymbol,
        AddedFieldCatalog addedFieldCatalog,
        List<string> declarationDriftWarnings,
        CompiledFieldMatch fieldMatch)
    {
        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeState.TypeMetadataNameFromSyntax, fieldSymbol.Name);
        string fieldKey = FormatAddedFieldStoreKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            fieldSymbol.Name);
        addedFieldCatalog.MarkClassifiedAdded(fieldKey);
        addedFieldCatalog.AddAddedSyntaxKey(syntaxKey);

        if (!CompiledMemberMatcher.IsCompiledFieldDeclarationChange(fieldMatch)
            && FieldHasSerializationAttribute(fieldDeclaration))
        {
            declarationDriftWarnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    AddedFieldSkipReasons.SerializeWarningFormat,
                    fieldSymbol.Name));
        }

        AddedFieldBinding binding = new AddedFieldBinding
        {
            FieldKey = fieldKey,
            SyntaxKey = syntaxKey,
            FieldName = fieldSymbol.Name,
            FieldType = fieldSymbol.Type,
            IsStatic = fieldSymbol.IsStatic,
            IsConst = fieldSymbol.IsConst,
            ConstantValue = fieldSymbol.HasConstantValue ? fieldSymbol.ConstantValue : null,
            Initializer = variable.Initializer != null ? variable.Initializer.Value : null
        };

        string declarationChangeReason = CompiledMemberMatcher.TryFormatCompiledFieldDeclarationChangeReason(
            fieldMatch,
            fieldSymbol.Name);
        if (declarationChangeReason != null)
        {
            // Why not RegisterStore: rewriting to the side table would hide the
            // declaration change and leave compiled callers on the old field.
            binding.UnavailableReason = declarationChangeReason;
            addedFieldCatalog.RegisterUnavailable(binding);
            return;
        }

        binding.UnavailableReason = EvaluateAddedFieldAvailability(
            typeState.TypeSymbol,
            semanticModel,
            targetTypesAssemblySymbol,
            fieldSymbol,
            binding);

        if (binding.UnavailableReason != null)
        {
            addedFieldCatalog.RegisterUnavailable(binding);
            return;
        }

        if (fieldSymbol.IsConst)
        {
            addedFieldCatalog.RegisterConst(binding);
            return;
        }

        addedFieldCatalog.RegisterStore(binding);
    }

    internal static string EvaluateAddedFieldAvailability(
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        IFieldSymbol fieldSymbol,
        AddedFieldBinding binding)
    {
        if (fieldSymbol.IsConst)
        {
            if (ConstantLiteralFactory.TryCreateConstantLiteral(binding.ConstantValue, fieldSymbol.Type) == null)
            {
                return AddedFieldSkipReasons.UnavailableAddedField;
            }

            return null;
        }

        // Why after const: added consts on struct hosts still fold to literals; the store
        // identity problem only applies to instance/static storage.
        if (hostType.TypeKind == TypeKind.Struct)
        {
            return AddedFieldSkipReasons.StructHost;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(fieldSymbol.Type))
        {
            return AddedFieldSkipReasons.FieldTypeNotExternallyVisible;
        }

        if (binding.Initializer != null
            && InitializerCannotEmitInShimLambda(
                binding.Initializer,
                semanticModel,
                hostType,
                targetTypesAssemblySymbol))
        {
            return AddedFieldSkipReasons.InitializerNotLiteralOrExternalStatic;
        }

        return null;
    }

    internal static string EvaluateAddedFieldSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null || addedFieldCatalog == null || !addedFieldCatalog.HasClassifiedAdded)
        {
            return null;
        }

        string unavailable = AddedFieldBodyScan.BodyReferencesUnavailableAddedField(bodyNode, semanticModel, addedFieldCatalog);
        if (unavailable != null)
        {
            return unavailable;
        }

        if (AddedFieldBodyScan.BodyPassesAddedFieldByRef(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.RefOutIn;
        }

        if (AddedFieldBodyScan.BodyHasUnsupportedAddedFieldCompound(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.UnavailableAddedField;
        }

        if (AddedFieldBodyScan.BodyHasNonNumericAddedFieldIncrement(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.IncrementNotNumeric;
        }

        if (AddedFieldBodyScan.BodyHasConsumedAddedFieldWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.ConsumedWrite;
        }

        if (AddedFieldBodyScan.BodyHasDoubleEvalAddedFieldReceiver(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.DoubleEvalReceiver;
        }

        if (AddedFieldBodyScan.BodyHasValueTypeAddedFieldMemberWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.ValueTypeMemberWrite;
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

    internal static string FormatAddedFieldStoreKey(string typeMetadataName, string fieldName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + fieldName;
    }

    internal static bool IsSameFileAddedMember(
        ISymbol symbol,
        IAssemblySymbol targetTypesAssemblySymbol,
        SyntaxTree currentTree)
    {
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

        INamedTypeSymbol compiledType = CompiledMemberMatcher.FindCompiledType(symbol.ContainingType, targetTypesAssemblySymbol);
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

    // Why this gate (not inaccessible-only): the initializer is spliced into a static lambda on
    // a shim type, so even public instance members of the host are CS0103 / CS0026, and
    // same-file added members do not exist on the compiled type the shim references.
    internal static bool InitializerCannotEmitInShimLambda(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        INamedTypeSymbol hostType,
        IAssemblySymbol targetTypesAssemblySymbol)
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

            if (HasDisallowedInitializerSymbol(
                semanticModel.GetSymbolInfo(node).Symbol,
                hostType,
                targetTypesAssemblySymbol,
                initializer.SyntaxTree))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasDisallowedInitializerSymbol(
        ISymbol symbol,
        INamedTypeSymbol hostType,
        IAssemblySymbol targetTypesAssemblySymbol,
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

        if (IsSameFileAddedMember(symbol, targetTypesAssemblySymbol, currentTree))
        {
            return true;
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }
}
