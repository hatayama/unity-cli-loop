using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            ? parameter.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword)
            || parameter.Modifiers.Any(SyntaxKind.OutKeyword)
            || parameter.Modifiers.Any(SyntaxKind.InKeyword))
        {
            typeText += "&";
        }

        return typeText;
    }

    // What: syntax-only type metadata name for baseline signature keys (not shim naming).

    internal static string BuildTypeMetadataNameFromSyntax(TypeDeclarationSyntax typeDeclaration)
    {
        List<string> nestedNames = new List<string>();
        TypeDeclarationSyntax current = typeDeclaration;
        while (current != null)
        {
            string simpleName = current.Identifier.Text;
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
        string simpleName = enumDeclaration.Identifier.Text;
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
            // Why NormalizeWhitespace: trivia in nested namespace names must not invent distinct keys.
            if (current is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                parts.Add(namespaceDeclaration.Name.NormalizeWhitespace().ToString());
            }
            else if (current is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
            {
                parts.Add(fileScopedNamespace.Name.NormalizeWhitespace().ToString());
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

    internal static string BuildSyntaxPropertyKey(
        string typeMetadataName,
        PropertyDeclarationSyntax propertyDeclaration)
    {
        string name = propertyDeclaration.Identifier.Text;
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
