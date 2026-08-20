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

    internal static Dictionary<string, MethodDeclarationSyntax> BuildSyntaxMethodMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MethodDeclarationSyntax> map = new Dictionary<string, MethodDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MethodDeclarationSyntax methodDeclaration in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                string key = BuildSyntaxMethodKey(typeMetadataName, methodDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = methodDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, VariableDeclaratorSyntax> BuildSyntaxFieldMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, VariableDeclaratorSyntax> map =
            new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (FieldDeclarationSyntax fieldDeclaration in typeDeclaration.Members
                .OfType<FieldDeclarationSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
                {
                    string key = BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                    if (map.ContainsKey(key))
                    {
                        return null;
                    }

                    map[key] = variable;
                }
            }
        }

        return map;
    }

    internal static Dictionary<string, PropertyDeclarationSyntax> BuildSyntaxPropertyMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, PropertyDeclarationSyntax> map = new Dictionary<string, PropertyDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (PropertyDeclarationSyntax propertyDeclaration in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                string key = BuildSyntaxPropertyKey(typeMetadataName, propertyDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = propertyDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, IndexerDeclarationSyntax> BuildSyntaxIndexerMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, IndexerDeclarationSyntax> map = new Dictionary<string, IndexerDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (IndexerDeclarationSyntax indexerDeclaration in typeDeclaration.Members.OfType<IndexerDeclarationSyntax>())
            {
                string key = BuildSyntaxIndexerKey(typeMetadataName, indexerDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = indexerDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, ConstructorDeclarationSyntax> BuildSyntaxConstructorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, ConstructorDeclarationSyntax> map =
            new Dictionary<string, ConstructorDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>())
            {
                string key = BuildSyntaxConstructorKey(typeMetadataName, constructorDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = constructorDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, MemberDeclarationSyntax> BuildSyntaxOperatorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MemberDeclarationSyntax> map =
            new Dictionary<string, MemberDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                string key = TryBuildSyntaxOperatorMemberKey(typeMetadataName, member);
                if (key == null)
                {
                    continue;
                }

                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = member;
            }
        }

        return map;
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

    internal static Dictionary<string, EventDeclarationSyntax> BuildSyntaxEventMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, EventDeclarationSyntax> map =
            new Dictionary<string, EventDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (EventDeclarationSyntax eventDeclaration in typeDeclaration.Members
                .OfType<EventDeclarationSyntax>())
            {
                string key = BuildSyntaxEventKey(typeMetadataName, eventDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = eventDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, VariableDeclaratorSyntax> BuildSyntaxEventFieldMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, VariableDeclaratorSyntax> map =
            new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (EventFieldDeclarationSyntax eventFieldDeclaration in typeDeclaration.Members
                .OfType<EventFieldDeclarationSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in eventFieldDeclaration.Declaration.Variables)
                {
                    // Why field key format: kind-change identity is type metadata + "::" + name,
                    // the same shape BuildSyntaxFieldKey already uses.
                    string key = BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                    if (map.ContainsKey(key))
                    {
                        return null;
                    }

                    map[key] = variable;
                }
            }
        }

        return map;
    }
}
