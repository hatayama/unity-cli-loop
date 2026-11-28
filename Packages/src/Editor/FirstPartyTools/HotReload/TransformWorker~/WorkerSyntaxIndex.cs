using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class WorkerSyntaxIndex
{
    // Syntax-based method key for same-file snapshot vs current comparison. Do not mix with
    // WorkerMethodKeys.BuildMethodKey (Cecil/metadata names used by the orchestrator exclusion path).
    // Used only for in-memory baseline maps — safe to evolve without wire compatibility concerns.
    internal static string BuildSyntaxMethodKey(string typeMetadataName, MethodDeclarationSyntax methodDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (methodDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in methodDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        // Why arity suffix: void F(int) and void F<T>(int) must not share a key (was silent
        // baseline-disable). Arity 0 keeps the bare name so existing non-generic keys stay stable.
        string methodName = methodDeclaration.Identifier.Text;
        if (methodDeclaration.TypeParameterList != null
            && methodDeclaration.TypeParameterList.Parameters.Count > 0)
        {
            methodName += "`"
                + methodDeclaration.TypeParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture);
        }

        // Why explicit-interface qualifier: IA.Run() and IB.Run() must not share a key (same as
        // BuildSyntaxPropertyKey). Property keys already include ExplicitInterfaceSpecifier.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            methodName = methodDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + methodName;
        }

        return typeMetadataName + "::" + methodName + "("
            + string.Join(",", parameterKeys) + ")";
    }

    // Keep in sync with HotReloadAddedFieldStore.FormatFieldKey / FieldKeySeparator.

    internal static string BuildSyntaxFieldKey(string typeMetadataName, string fieldName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + fieldName;
    }

    internal static string BuildSyntaxParameterTypeKey(ParameterSyntax parameter)
    {
        // Why NormalizeWhitespace: trivia / spacing differences must not invent distinct keys.
        string typeText = parameter.Type != null
            ? SpellTypeParametersByPosition(parameter.Type.NormalizeWhitespace(), parameter)
            : string.Empty;
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword)
            || parameter.Modifiers.Any(SyntaxKind.OutKeyword)
            || parameter.Modifiers.Any(SyntaxKind.InKeyword))
        {
            typeText += "&";
        }

        return typeText;
    }

    /// <summary>
    /// The text of <paramref name="normalizedType"/> with every name of an in-scope type parameter
    /// spelled by its position: <c>!!n</c> for the n-th type parameter of the declaring method, and
    /// <c>!d.n</c> for the n-th type parameter of the containing type declaration at depth d,
    /// counted from the outermost type (0). A type with no such name keeps its text byte for byte.
    /// </summary>
    // Why by position: renaming a type parameter (Echo<T>(T) to Echo<U>(U)) leaves the member the
    // same, so its snapshot and current keys must meet; a swap of positions must not.
    private static string SpellTypeParametersByPosition(TypeSyntax normalizedType, ParameterSyntax parameter)
    {
        Dictionary<string, string> positions = CollectTypeParameterPositions(parameter);
        if (positions.Count == 0)
        {
            return normalizedType.ToString();
        }

        StringBuilder builder = new StringBuilder();
        foreach (SyntaxToken token in normalizedType.DescendantTokens())
        {
            if (IsTypeParameterReference(token) && positions.TryGetValue(token.ValueText, out string position))
            {
                builder.Append(token.LeadingTrivia.ToFullString());
                builder.Append(position);
                builder.Append(token.TrailingTrivia.ToFullString());
                continue;
            }

            builder.Append(token.ToFullString());
        }

        return builder.ToString().Trim();
    }

    // Why the method's own list first: a method type parameter hides a containing type's one of
    // the same name, so the first position recorded for a name wins.
    private static Dictionary<string, string> CollectTypeParameterPositions(ParameterSyntax parameter)
    {
        Dictionary<string, string> positions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameter.Parent?.Parent is MethodDeclarationSyntax method && method.TypeParameterList != null)
        {
            AddPositions(positions, method.TypeParameterList, "!!");
        }

        List<TypeDeclarationSyntax> containingTypes = new List<TypeDeclarationSyntax>();
        for (TypeDeclarationSyntax current = parameter.FirstAncestorOrSelf<TypeDeclarationSyntax>();
             current != null;
             current = current.Parent as TypeDeclarationSyntax)
        {
            containingTypes.Add(current);
        }

        // Innermost first, so an inner type's parameter hides an outer one of the same name;
        // the depth is still counted from the outermost type.
        for (int index = 0; index < containingTypes.Count; index++)
        {
            TypeParameterListSyntax typeParameters = containingTypes[index].TypeParameterList;
            if (typeParameters != null)
            {
                int depth = containingTypes.Count - 1 - index;
                AddPositions(positions, typeParameters, "!" + depth.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        return positions;
    }

    private static void AddPositions(Dictionary<string, string> positions, TypeParameterListSyntax typeParameters, string prefix)
    {
        for (int index = 0; index < typeParameters.Parameters.Count; index++)
        {
            string name = typeParameters.Parameters[index].Identifier.ValueText;
            if (!positions.ContainsKey(name))
            {
                positions.Add(name, prefix + index.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    // Why only a bare or leftmost name: the right side of A.T names a member type called T, and
    // both sides of T::X are an alias and a name in it, none of them the type parameter. Type
    // arguments inside either are separate names and still checked on their own.
    private static bool IsTypeParameterReference(SyntaxToken token)
    {
        if (!(token.Parent is IdentifierNameSyntax identifierName))
        {
            return false;
        }

        if (identifierName.Parent is QualifiedNameSyntax qualified && qualified.Right == identifierName)
        {
            return false;
        }

        return !(identifierName.Parent is AliasQualifiedNameSyntax);
    }

    // What: syntax-only type metadata name for baseline signature keys (not shim naming).

    internal static string BuildTypeMetadataNameFromSyntax(TypeDeclarationSyntax typeDeclaration)
    {
        List<string> nestedNames = new List<string>();
        TypeDeclarationSyntax current = typeDeclaration;
        while (current != null)
        {
            // Why ValueText: the key stands for a metadata name, and kind-change keys built from
            // symbols meet these keys; Text keeps the @ of a keyword-named type such as @class.
            string simpleName = current.Identifier.ValueText;
            if (current.TypeParameterList != null && current.TypeParameterList.Parameters.Count > 0)
            {
                simpleName += "`" + current.TypeParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture);
            }

            nestedNames.Add(simpleName);
            current = current.Parent as TypeDeclarationSyntax;
        }

        nestedNames.Reverse();
        string typeMetadataName = string.Join("+", nestedNames);

        string namespaceName = GetContainingNamespaceName(typeDeclaration);
        if (string.IsNullOrEmpty(namespaceName))
        {
            return typeMetadataName;
        }

        return namespaceName + "." + typeMetadataName;
    }

    // What: the same syntax-only metadata name for an enum, which is a type declaration the
    // rewriters key by but not a TypeDeclarationSyntax, so it cannot go through the method above.
    // An enum takes no type parameters, so there is no arity suffix to build.

    internal static string BuildEnumMetadataNameFromSyntax(EnumDeclarationSyntax enumDeclaration)
    {
        string simpleName = enumDeclaration.Identifier.ValueText;
        if (enumDeclaration.Parent is TypeDeclarationSyntax containingType)
        {
            return BuildTypeMetadataNameFromSyntax(containingType) + "+" + simpleName;
        }

        string namespaceName = GetContainingNamespaceName(enumDeclaration);
        if (string.IsNullOrEmpty(namespaceName))
        {
            return simpleName;
        }

        return namespaceName + "." + simpleName;
    }

    // What: dotted namespace path including all ancestor namespaces (not only the innermost).

    internal static string GetContainingNamespaceName(SyntaxNode node)
    {
        List<string> parts = new List<string>();
        SyntaxNode current = node.Parent;
        while (current != null)
        {
            if (current is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                parts.Add(ToUnescapedName(namespaceDeclaration.Name));
            }

            current = current.Parent;
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    // Why segment ValueTexts rather than the name text: the text keeps trivia and the @ of a
    // keyword-named segment such as @event, which no metadata name carries, so a key built from a
    // symbol would never meet it.
    private static string ToUnescapedName(NameSyntax name)
    {
        return string.Join(
            ".",
            name.DescendantNodesAndSelf()
                .OfType<SimpleNameSyntax>()
                .Select(simpleName => simpleName.Identifier.ValueText));
    }

    internal static string BuildSyntaxPropertyKey(
        string typeMetadataName,
        PropertyDeclarationSyntax propertyDeclaration)
    {
        // Why ValueText: kind-change keys name the compiled property by its symbol name, which
        // drops the @ of a keyword-named property such as @checked.
        string name = propertyDeclaration.Identifier.ValueText;
        if (propertyDeclaration.ExplicitInterfaceSpecifier != null)
        {
            // Why NormalizeWhitespace: keep property keys symmetric with BuildSyntaxMethodKey so
            // trivia in the interface name cannot invent a distinct baseline key.
            name = propertyDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return typeMetadataName + "::" + name;
    }

    internal static string BuildSyntaxIndexerKey(
        string typeMetadataName,
        IndexerDeclarationSyntax indexerDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (indexerDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in indexerDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        return typeMetadataName + "::this(" + string.Join(",", parameterKeys) + ")";
    }

    // Why the type name alone is unique: a finalizer takes no parameters and a type can
    // declare at most one.
    internal static string BuildSyntaxDestructorKey(string typeMetadataName)
    {
        return typeMetadataName + "::Finalize()";
    }

    internal static string BuildSyntaxConstructorKey(
        string typeMetadataName,
        ConstructorDeclarationSyntax constructorDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (constructorDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in constructorDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        string name = constructorDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            ? ".cctor"
            : ".ctor";
        return typeMetadataName + "::" + name + "(" + string.Join(",", parameterKeys) + ")";
    }

    internal static string BuildSyntaxOperatorKey(
        string typeMetadataName,
        OperatorDeclarationSyntax operatorDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (operatorDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in operatorDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        return typeMetadataName + "::" + operatorDeclaration.OperatorToken.ValueText
            + "(" + string.Join(",", parameterKeys) + ")";
    }

    internal static string BuildSyntaxConversionOperatorKey(
        string typeMetadataName,
        ConversionOperatorDeclarationSyntax conversionDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (conversionDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in conversionDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        string targetType = conversionDeclaration.Type != null
            ? conversionDeclaration.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        return typeMetadataName + "::" + conversionDeclaration.ImplicitOrExplicitKeyword.ValueText
            + "->" + targetType + "(" + string.Join(",", parameterKeys) + ")";
    }

    internal static string BuildSyntaxEventKey(
        string typeMetadataName,
        EventDeclarationSyntax eventDeclaration)
    {
        string name = eventDeclaration.Identifier.Text;
        if (eventDeclaration.ExplicitInterfaceSpecifier != null)
        {
            name = eventDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return typeMetadataName + "::" + name;
    }

    internal static string TryBuildSyntaxOperatorMemberKey(
        string typeMetadataName,
        MemberDeclarationSyntax member)
    {
        if (member is OperatorDeclarationSyntax operatorDeclaration)
        {
            return BuildSyntaxOperatorKey(typeMetadataName, operatorDeclaration);
        }

        if (member is ConversionOperatorDeclarationSyntax conversionDeclaration)
        {
            return BuildSyntaxConversionOperatorKey(typeMetadataName, conversionDeclaration);
        }

        return null;
    }
}
