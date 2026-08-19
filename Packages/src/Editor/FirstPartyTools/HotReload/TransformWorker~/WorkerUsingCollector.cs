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

internal static class WorkerUsingCollector
{
    internal static List<UsingDirectiveSyntax> CollectUsingsForType(
        CompilationUnitSyntax root,
        TypeDeclarationSyntax typeDeclaration,
        List<UsingDirectiveSyntax> assemblyGlobalUsings)
    {
        List<UsingDirectiveSyntax> usings = new List<UsingDirectiveSyntax>();
        foreach (UsingDirectiveSyntax usingDirective in root.Usings)
        {
            usings.Add(usingDirective.WithoutTrivia());
        }

        for (SyntaxNode node = typeDeclaration.Parent; node != null; node = node.Parent)
        {
            if (node is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (UsingDirectiveSyntax usingDirective in namespaceDeclaration.Usings)
                {
                    usings.Add(usingDirective.WithoutTrivia());
                }
            }
        }

        foreach (UsingDirectiveSyntax assemblyUsing in assemblyGlobalUsings)
        {
            if (!ShouldSkipAssemblyUsing(usings, assemblyUsing))
            {
                usings.Add(assemblyUsing);
            }
        }

        return usings;
    }

    // Why skip same alias name regardless of target: C# lets a namespace-scoped alias shadow a
    // global one. Flattening both into the shim's single namespace is CS1537.
    internal static bool ShouldSkipAssemblyUsing(
        List<UsingDirectiveSyntax> existingUsings,
        UsingDirectiveSyntax assemblyUsing)
    {
        if (ContainsEquivalentUsing(existingUsings, assemblyUsing))
        {
            return true;
        }

        if (assemblyUsing.Alias == null)
        {
            return false;
        }

        string aliasName = assemblyUsing.Alias.Name.ToString();
        foreach (UsingDirectiveSyntax existing in existingUsings)
        {
            if (existing.Alias != null && existing.Alias.Name.ToString() == aliasName)
            {
                return true;
            }
        }

        return false;
    }

    // Why skip SourcePath: the edited file's usings already come from the in-memory tree.
    // Reading the on-disk copy would pick up the pre-edit source.
    internal static List<UsingDirectiveSyntax> CollectAssemblyGlobalUsings(
        WorkerInput input,
        CSharpParseOptions parseOptions)
    {
        List<UsingDirectiveSyntax> collected = new List<UsingDirectiveSyntax>();
        foreach (string assemblySourcePath in input.AssemblySourcePaths)
        {
            if (string.IsNullOrEmpty(assemblySourcePath)
                || PathsReferToSameSourceFile(assemblySourcePath, input.SourcePath)
                || !File.Exists(assemblySourcePath))
            {
                continue;
            }

            string text = File.ReadAllText(
                assemblySourcePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!FileContainsGlobalUsingLine(text))
            {
                continue;
            }

            AppendGlobalUsingsFromParsedText(collected, text, parseOptions, assemblySourcePath);
        }

        return collected;
    }

    internal static void AppendGlobalUsingsFromParsedText(
        List<UsingDirectiveSyntax> collected,
        string text,
        CSharpParseOptions parseOptions,
        string assemblySourcePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            SourceText.From(text, Encoding.UTF8),
            parseOptions,
            path: assemblySourcePath);
        CompilationUnitSyntax unit = tree.GetCompilationUnitRoot();
        foreach (UsingDirectiveSyntax usingDirective in unit.Usings)
        {
            if (!usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            {
                continue;
            }

            UsingDirectiveSyntax asOrdinary = usingDirective
                .WithGlobalKeyword(default)
                .WithoutTrivia();
            if (!ContainsEquivalentUsing(collected, asOrdinary))
            {
                collected.Add(asOrdinary);
            }
        }
    }

    internal static bool FileContainsGlobalUsingLine(string text)
    {
        using StringReader reader = new StringReader(text);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (LineStartsWithGlobalUsing(line))
            {
                return true;
            }
        }

        return false;
    }

    // Why same-line tokens (not ParseText): the prefilter must stay cheaper than parsing every
    // assembly file. Extra whitespace between global and using is allowed; a comment or line
    // break between those tokens is out of scope for this filter.
    internal static bool LineStartsWithGlobalUsing(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("global", StringComparison.Ordinal) || trimmed.Length <= 6)
        {
            return false;
        }

        char afterGlobal = trimmed[6];
        if (afterGlobal != ' ' && afterGlobal != '\t')
        {
            return false;
        }

        return trimmed.Substring(6).TrimStart().StartsWith("using", StringComparison.Ordinal);
    }

    internal static bool PathsReferToSameSourceFile(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        string normalizedLeft = Path.GetFullPath(left);
        string normalizedRight = Path.GetFullPath(right);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    internal static bool ContainsEquivalentUsing(
        List<UsingDirectiveSyntax> usings,
        UsingDirectiveSyntax candidate)
    {
        foreach (UsingDirectiveSyntax existing in usings)
        {
            if (UsingDirectivesMatch(existing, candidate))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool UsingDirectivesMatch(UsingDirectiveSyntax left, UsingDirectiveSyntax right)
    {
        bool leftStatic = left.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
        bool rightStatic = right.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
        if (leftStatic != rightStatic)
        {
            return false;
        }

        string leftAlias = left.Alias == null ? string.Empty : left.Alias.Name.ToString();
        string rightAlias = right.Alias == null ? string.Empty : right.Alias.Name.ToString();
        if (leftAlias != rightAlias)
        {
            return false;
        }

        string leftName = left.Name == null ? string.Empty : left.Name.ToString();
        string rightName = right.Name == null ? string.Empty : right.Name.ToString();
        return leftName == rightName;
    }
}
