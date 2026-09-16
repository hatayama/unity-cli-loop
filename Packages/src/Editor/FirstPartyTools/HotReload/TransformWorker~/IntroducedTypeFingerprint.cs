using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Turns one declaration into the value that decides whether a retained type still describes the
// same definition: its tokens, the defines it was parsed with, and the identity of every type it
// depends on, each recorded against the traversal position that binds it. The inputs are split
// across the header, the member order and each member's declaration and body, so a reader can
// tell which of them moved; nothing is dropped, so the parts together still separate every pair
// of definitions the single hash used to separate.
internal static class IntroducedTypeFingerprint
{
    internal static HotReloadIntroducedTypeFingerprint Compute(
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<string> defineSymbols,
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
        IReadOnlyList<MemberDeclarationSyntax> memberNodes = IntroducedTypeMemberRegions.CollectMembers(declaration);

        string headerHash = ComputeHash(BuildHeaderInput(declaration, memberNodes, typeSymbol, semanticModel, walker));
        string definesHash = ComputeHash(BuildDefinesInput(defineSymbols));

        string typeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol);
        IReadOnlyList<string> keys = CollectOrderedMemberKeys(memberNodes, typeMetadataName);

        List<HotReloadIntroducedTypeMemberFingerprint> members =
            new List<HotReloadIntroducedTypeMemberFingerprint>(memberNodes.Count);
        for (int index = 0; index < memberNodes.Count; index++)
        {
            members.Add(BuildMemberFingerprint(memberNodes[index], keys[index], semanticModel, walker));
        }

        // Sorting the members by key drops the order they were declared in, and that order is
        // observable: it decides implicit enum values and the sequence field initializers run in.
        string memberOrderHash = ComputeMemberOrderHash(keys);
        members.Sort(CompareMemberKeys);

        return new HotReloadIntroducedTypeFingerprint(headerHash, definesHash, memberOrderHash, members);
    }

    /// <summary>
    /// The keys of a declaration's members in the order they are declared in, spelled and
    /// numbered the way Compute records them. Anything that compares an order against a recorded
    /// one reads it from here, so no caller builds a member key a second way.
    /// </summary>
    internal static IReadOnlyList<string> CollectOrderedMemberKeys(
        IReadOnlyList<MemberDeclarationSyntax> memberNodes,
        string typeMetadataName)
    {
        List<string> keys = new List<string>(memberNodes.Count);
        for (int index = 0; index < memberNodes.Count; index++)
        {
            keys.Add(IntroducedTypeMemberRegions.BuildMemberKey(
                IntroducedTypeMemberRegions.StripTrivia(memberNodes[index]),
                typeMetadataName,
                index));
        }

        DisambiguateKeys(keys);
        return keys;
    }

    /// <summary>The value a fingerprint records the declared member order under.</summary>
    internal static string ComputeMemberOrderHash(IReadOnlyList<string> keys)
    {
        return ComputeHash(BuildKeyOrderInput(keys));
    }

    private static int CompareMemberKeys(
        HotReloadIntroducedTypeMemberFingerprint left,
        HotReloadIntroducedTypeMemberFingerprint right)
    {
        return string.CompareOrdinal(left.Key, right.Key);
    }

    // Everything the declaration says outside its members: attributes, modifiers, the identifier,
    // type parameters, the base list, constraints, and the types all of those bind to.
    private static string BuildHeaderInput(
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<MemberDeclarationSyntax> memberNodes,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        IntroducedTypeDependencyWalker walker)
    {
        StringBuilder input = new StringBuilder();
        AppendTokens(input, declaration.DescendantTokens().Where(token => !IsInsideMember(memberNodes, token.Span)));

        HashSet<string> declaringDependency = new HashSet<string>(StringComparer.Ordinal);
        walker.AddDependencies(typeSymbol, declaringDependency);
        foreach (string identity in declaringDependency.OrderBy(identity => identity, StringComparer.Ordinal))
        {
            AppendValue(input, "self|" + identity);
        }

        AppendNodeDependencies(
            input,
            declaration.DescendantNodesAndSelf().Where(node => !IsInsideMember(memberNodes, node.Span)),
            semanticModel,
            walker);
        return input.ToString();
    }

    private static HotReloadIntroducedTypeMemberFingerprint BuildMemberFingerprint(
        MemberDeclarationSyntax member,
        string key,
        SemanticModel semanticModel,
        IntroducedTypeDependencyWalker walker)
    {
        IReadOnlyList<SyntaxNode> bodyNodes = IntroducedTypeMemberRegions.CollectBodyNodes(member);
        IReadOnlyList<SyntaxToken> bodyTerminators = IntroducedTypeMemberRegions.CollectBodyTerminators(member);

        StringBuilder declarationInput = new StringBuilder();
        AppendTokens(
            declarationInput,
            member.DescendantTokens().Where(token =>
                !IsInsideBody(bodyNodes, token.Span) && !IsBodyTerminator(bodyTerminators, token)));
        AppendNodeDependencies(
            declarationInput,
            member.DescendantNodesAndSelf().Where(node => !IsInsideBody(bodyNodes, node.Span)),
            semanticModel,
            walker);
        string declarationHash = ComputeHash(declarationInput.ToString());

        if (bodyNodes.Count == 0)
        {
            return new HotReloadIntroducedTypeMemberFingerprint(key, declarationHash, string.Empty);
        }

        StringBuilder bodyInput = new StringBuilder();
        AppendTokens(bodyInput, bodyNodes.SelectMany(node => node.DescendantTokens()));
        AppendTokens(bodyInput, bodyTerminators);
        AppendNodeDependencies(
            bodyInput,
            bodyNodes.SelectMany(node => node.DescendantNodesAndSelf()),
            semanticModel,
            walker);
        return new HotReloadIntroducedTypeMemberFingerprint(key, declarationHash, ComputeHash(bodyInput.ToString()));
    }

    private static bool IsInsideMember(IReadOnlyList<MemberDeclarationSyntax> memberNodes, TextSpan span)
    {
        for (int index = 0; index < memberNodes.Count; index++)
        {
            if (memberNodes[index].Span.Contains(span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBodyTerminator(IReadOnlyList<SyntaxToken> bodyTerminators, SyntaxToken token)
    {
        for (int index = 0; index < bodyTerminators.Count; index++)
        {
            if (bodyTerminators[index] == token)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideBody(IReadOnlyList<SyntaxNode> bodyNodes, TextSpan span)
    {
        for (int index = 0; index < bodyNodes.Count; index++)
        {
            if (bodyNodes[index].Span.Contains(span))
            {
                return true;
            }
        }

        return false;
    }

    // Two members can spell the same key when the source declares the same signature twice, which
    // does not compile but must still fingerprint: the later ones are numbered in declaration order.
    private static void DisambiguateKeys(List<string> keys)
    {
        Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < keys.Count; index++)
        {
            string key = keys[index];
            int occurrence;
            if (!seen.TryGetValue(key, out occurrence))
            {
                seen[key] = 1;
                continue;
            }

            occurrence++;
            seen[key] = occurrence;
            keys[index] = key + "#" + occurrence.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string BuildDefinesInput(IReadOnlyList<string> defineSymbols)
    {
        StringBuilder input = new StringBuilder();
        foreach (string defineSymbol in defineSymbols.OrderBy(symbol => symbol, StringComparer.Ordinal))
        {
            AppendValue(input, defineSymbol);
        }

        return input.ToString();
    }

    private static string BuildKeyOrderInput(IReadOnlyList<string> keys)
    {
        StringBuilder input = new StringBuilder();
        for (int index = 0; index < keys.Count; index++)
        {
            AppendValue(input, keys[index]);
        }

        return input.ToString();
    }

    private static string ComputeHash(string input)
    {
        byte[] sourceBytes = Encoding.UTF8.GetBytes(input);
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

    // Records each dependency against the ordinal position of the node that binds it, counted
    // within the region being hashed. An unordered set cannot tell two aliases apart when they
    // exchange the types they bind to: the tokens are identical and the set of referenced types is
    // the same, so the fingerprint would stay equal while the definition changed. The position is a
    // traversal ordinal rather than an absolute span, so it survives trivia edits and unrelated
    // using directives.
    private static void AppendNodeDependencies(
        StringBuilder builder,
        IEnumerable<SyntaxNode> nodes,
        SemanticModel semanticModel,
        IntroducedTypeDependencyWalker walker)
    {
        int position = 0;
        foreach (SyntaxNode node in nodes)
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
                AppendValue(builder, position.ToString(CultureInfo.InvariantCulture) + "|" + identity);
            }

            position++;
        }
    }
}
