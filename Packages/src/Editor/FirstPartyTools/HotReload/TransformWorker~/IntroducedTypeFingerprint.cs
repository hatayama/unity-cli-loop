using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Turns one declaration into the value that decides whether a retained type still describes the
// same definition: its tokens, the defines it was parsed with, and the identity of every type it
// depends on, each recorded against the traversal position that binds it.
internal static class IntroducedTypeFingerprint
{
    internal static string Compute(
        CompilationUnitSyntax root,
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<string> defineSymbols,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap)
    {
        StringBuilder input = new StringBuilder();
        AppendTokens(input, declaration.DescendantTokens());

        foreach (string defineSymbol in defineSymbols.OrderBy(symbol => symbol, StringComparer.Ordinal))
        {
            AppendValue(input, defineSymbol);
        }

        foreach (string dependencyIdentity in CollectDependencyIdentities(
            declaration,
            typeSymbol,
            semanticModel,
            targetAssembly,
            targetAssemblyName,
            targetAssemblyMvid,
            artifactMap))
        {
            AppendValue(input, dependencyIdentity);
        }

        byte[] sourceBytes = Encoding.UTF8.GetBytes(input.ToString());
        using SHA256 hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(sourceBytes);
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        for (int index = 0; index < bytes.Length; index++)
        {
            builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendTokens(StringBuilder builder, IEnumerable<SyntaxToken> tokens)
    {
        foreach (SyntaxToken token in tokens)
        {
            builder.Append(token.RawKind.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            AppendValue(builder, token.Text);
        }
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        string safeValue = value ?? string.Empty;
        builder.Append(safeValue.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(safeValue);
        builder.Append('\n');
    }

    // Records each dependency against the ordinal position of the declaration node that binds it.
    // An unordered set cannot tell two aliases apart when they exchange the types they bind to:
    // the tokens are identical and the set of referenced types is the same, so the fingerprint
    // would stay equal while the definition changed. The position is a traversal ordinal rather
    // than an absolute span, so it survives trivia edits and unrelated using directives.
    private static IReadOnlyList<string> CollectDependencyIdentities(
        BaseTypeDeclarationSyntax declaration,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap)
    {
        IntroducedTypeDependencyWalker walker = new IntroducedTypeDependencyWalker(
            semanticModel.Compilation.Assembly,
            targetAssembly,
            targetAssemblyName,
            targetAssemblyMvid,
            artifactMap);
        List<string> positionedDependencies = new List<string>();
        HashSet<string> declaringDependency = new HashSet<string>(StringComparer.Ordinal);
        walker.AddDependencies(typeSymbol, declaringDependency);
        foreach (string identity in declaringDependency.OrderBy(identity => identity, StringComparer.Ordinal))
        {
            positionedDependencies.Add("self|" + identity);
        }

        int position = 0;
        foreach (SyntaxNode node in declaration.DescendantNodesAndSelf())
        {
            HashSet<string> nodeDependencies = new HashSet<string>(StringComparer.Ordinal);
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node);
            walker.AddDependencies(symbolInfo.Symbol, nodeDependencies);
            foreach (ISymbol candidate in symbolInfo.CandidateSymbols)
            {
                walker.AddDependencies(candidate, nodeDependencies);
            }

            TypeInfo typeInfo = semanticModel.GetTypeInfo(node);
            walker.AddDependencies(typeInfo.Type, nodeDependencies);
            walker.AddDependencies(typeInfo.ConvertedType, nodeDependencies);
            foreach (string identity in nodeDependencies.OrderBy(identity => identity, StringComparer.Ordinal))
            {
                positionedDependencies.Add(position.ToString(CultureInfo.InvariantCulture) + "|" + identity);
            }

            position++;
        }

        return positionedDependencies;
    }
}
