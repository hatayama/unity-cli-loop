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

internal static class TestAttributeNames
{
    public const string AddedTestMethodWarningFormat =
        "Added test method '{0}' on {1} is not visible to the Unity Test Runner until 'uloop compile'; "
        + "the Test Runner discovers tests by reflection on the compiled assembly, so "
        + "'uloop run-tests --skip-compile' will not find or run it. "
        + "Run 'uloop compile' (or 'uloop run-tests' without --skip-compile) first.";

    private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "Test",
        "TestCase",
        "TestCaseSource",
        "Theory",
        "UnityTest",
        "SetUp",
        "TearDown",
        "OneTimeSetUp",
        "OneTimeTearDown",
        "UnitySetUp",
        "UnityTearDown"
    };

    public static bool HasTestAttribute(MethodDeclarationSyntax methodDeclaration)
    {
        Debug.Assert(methodDeclaration != null, "methodDeclaration must not be null.");
        foreach (AttributeListSyntax attributeList in methodDeclaration.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                if (Names.Contains(GetSimpleAttributeName(attribute.Name)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static void AppendAddedTestMethodWarningIfNeeded(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol methodSymbol,
        List<string> declarationDriftWarnings)
    {
        Debug.Assert(methodDeclaration != null, "methodDeclaration must not be null.");
        Debug.Assert(typeSymbol != null, "typeSymbol must not be null.");
        Debug.Assert(methodSymbol != null, "methodSymbol must not be null.");
        Debug.Assert(declarationDriftWarnings != null, "declarationDriftWarnings must not be null.");
        if (!HasTestAttribute(methodDeclaration))
        {
            return;
        }

        declarationDriftWarnings.Add(
            string.Format(
                CultureInfo.InvariantCulture,
                AddedTestMethodWarningFormat,
                methodSymbol.Name,
                typeSymbol.ToDisplayString()));
    }

    // Syntax only, not symbols: the worker compile may lack NUnit / UnityEngine.TestTools
    // references, so attribute symbols become ErrorType and a semantic check would flicker.
    private static string GetSimpleAttributeName(NameSyntax name)
    {
        NameSyntax current = name;
        while (current is QualifiedNameSyntax qualified)
        {
            current = qualified.Right;
        }

        if (current is AliasQualifiedNameSyntax aliasQualified)
        {
            current = aliasQualified.Name;
        }

        string identifier = ReadIdentifier(current);
        const string suffix = "Attribute";
        if (identifier.Length > suffix.Length && identifier.EndsWith(suffix, StringComparison.Ordinal))
        {
            return identifier.Substring(0, identifier.Length - suffix.Length);
        }

        return identifier;
    }

    private static string ReadIdentifier(NameSyntax name)
    {
        if (name is IdentifierNameSyntax identifierName)
        {
            return identifierName.Identifier.ValueText;
        }

        if (name is GenericNameSyntax genericName)
        {
            return genericName.Identifier.ValueText;
        }

        return name.ToString();
    }
}
