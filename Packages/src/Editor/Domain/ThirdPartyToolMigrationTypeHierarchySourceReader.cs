using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDeclarationNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// One class declaration (or one part of a partial class) read from a source: the names it declares and the
    /// context needed to resolve the base name it writes.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTypeHierarchyClassPart
    {
        public ThirdPartyToolMigrationTypeHierarchyClassPart(
            string qualifiedName,
            bool isPartial,
            string writtenBaseName,
            string namespaceName,
            IReadOnlyList<string> enclosingTypeNames,
            IReadOnlyList<string> usingNamespaces,
            IReadOnlyCollection<string> scopeNames,
            IReadOnlyCollection<string> memberNames,
            IReadOnlyCollection<string> nonPrivateMemberNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(qualifiedName), "qualifiedName must not be null or empty");
            Debug.Assert(writtenBaseName != null, "writtenBaseName must not be null");
            Debug.Assert(namespaceName != null, "namespaceName must not be null");
            Debug.Assert(enclosingTypeNames != null, "enclosingTypeNames must not be null");
            Debug.Assert(usingNamespaces != null, "usingNamespaces must not be null");
            Debug.Assert(scopeNames != null, "scopeNames must not be null");
            Debug.Assert(memberNames != null, "memberNames must not be null");
            Debug.Assert(nonPrivateMemberNames != null, "nonPrivateMemberNames must not be null");

            QualifiedName = qualifiedName;
            IsPartial = isPartial;
            WrittenBaseName = writtenBaseName;
            NamespaceName = namespaceName;
            EnclosingTypeNames = enclosingTypeNames;
            UsingNamespaces = usingNamespaces;
            ScopeNames = scopeNames;
            MemberNames = memberNames;
            NonPrivateMemberNames = nonPrivateMemberNames;
        }

        public string QualifiedName { get; }
        public bool IsPartial { get; }

        // The first base list entry as written, or empty when the declaration has no base list.
        public string WrittenBaseName { get; }
        public string NamespaceName { get; }

        // Qualified names of the enclosing types, from the innermost to the outermost.
        public IReadOnlyList<string> EnclosingTypeNames { get; }
        public IReadOnlyList<string> UsingNamespaces { get; }

        // Names declared anywhere in the class body outside nested type bodies, including locals and parameters.
        public IReadOnlyCollection<string> ScopeNames { get; }

        // Names declared directly in the class body.
        public IReadOnlyCollection<string> MemberNames { get; }
        public IReadOnlyCollection<string> NonPrivateMemberNames { get; }
    }

    /// <summary>
    /// Reads the class declarations of one C# source for the type hierarchy index.
    /// </summary>
    public static class ThirdPartyToolMigrationTypeHierarchySourceReader
    {
        private static readonly Regex AccessModifierRegex =
            new(@"\b(?:public|protected|internal)\b", RegexOptions.Compiled);

        private static readonly Regex PartialModifierRegex = new(@"\bpartial\b", RegexOptions.Compiled);

        public static List<ThirdPartyToolMigrationTypeHierarchyClassPart> ReadClassParts(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            List<TypeBody> typeBodies = ReadTypeBodies(source, codeTextMask);
            List<ThirdPartyToolMigrationTypeHierarchyClassPart> parts = new();
            foreach (TypeBody typeBody in typeBodies)
            {
                if (typeBody.IsClass)
                {
                    parts.Add(ReadClassPart(source, codeTextMask, typeBody, typeBodies));
                }
            }

            return parts;
        }

        private static List<TypeBody> ReadTypeBodies(string source, CodeTextMask codeTextMask)
        {
            List<TypeBody> typeBodies = new();
            foreach (Match match in TypeDeclarationNameRegex.Matches(source))
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                int openBraceIndex = FindTypeBodyOpenBraceIndex(source, codeTextMask, match.Index + match.Length);
                if (openBraceIndex < 0)
                {
                    continue;
                }

                int closeBraceIndex = FindBlockClosingBraceIndex(source, codeTextMask, openBraceIndex);
                if (closeBraceIndex < 0)
                {
                    continue;
                }

                typeBodies.Add(new TypeBody(match, openBraceIndex, closeBraceIndex));
            }

            return typeBodies;
        }

        private static ThirdPartyToolMigrationTypeHierarchyClassPart ReadClassPart(
            string source,
            CodeTextMask codeTextMask,
            TypeBody typeBody,
            List<TypeBody> typeBodies)
        {
            // The qualified name must come from ReadContainingTypeName, the same function that names the declaring
            // type of a removed signature and the type that contains a call; any other spelling never matches them.
            int bodyStartIndex = typeBody.OpenBraceIndex + 1;
            string qualifiedName = ReadContainingTypeName(source, codeTextMask, bodyStartIndex);
            int enclosingTypeCount = ReadContainingTypeDeclarations(source, codeTextMask, bodyStartIndex).Count - 1;
            int declarationIndex = typeBody.Declaration.Index;
            string header = ReadCodeOnlyText(
                source,
                codeTextMask,
                declarationIndex + typeBody.Declaration.Length,
                typeBody.OpenBraceIndex);
            DeclaredNames declaredNames = ReadDeclaredNames(source, codeTextMask, typeBody, typeBodies);

            return new ThirdPartyToolMigrationTypeHierarchyClassPart(
                qualifiedName,
                PartialModifierRegex.IsMatch(ReadModifierText(source, codeTextMask, declarationIndex)),
                ReadFirstBaseListTypeName(header),
                ReadNamespaceName(source, codeTextMask, declarationIndex),
                ReadEnclosingTypeNames(qualifiedName, enclosingTypeCount),
                ReadUsingNamespaces(source, codeTextMask, declarationIndex),
                declaredNames.ScopeNames,
                declaredNames.MemberNames,
                declaredNames.NonPrivateMemberNames);
        }

        // Each enclosing type's qualified name is the class's qualified name with trailing segments dropped.
        private static List<string> ReadEnclosingTypeNames(string qualifiedName, int enclosingTypeCount)
        {
            List<string> enclosingTypeNames = new();
            string current = qualifiedName;
            for (int count = 0; count < enclosingTypeCount; count++)
            {
                int lastDotIndex = current.LastIndexOf('.');
                if (lastDotIndex < 0)
                {
                    break;
                }

                current = current.Substring(0, lastDotIndex);
                enclosingTypeNames.Add(current);
            }

            return enclosingTypeNames;
        }

        private static List<string> ReadUsingNamespaces(string source, CodeTextMask codeTextMask, int declarationIndex)
        {
            List<string> usingNamespaces = new();
            foreach (string importedNamespace in ReadImportedNamespaceNames(source, codeTextMask, declarationIndex))
            {
                usingNamespaces.Add(NormalizeTypeNameForComparison(importedNamespace));
            }

            return usingNamespaces;
        }

        // The modifiers of a declaration run back to the previous statement end, block brace, or attribute bracket.
        // An array rank specifier in the return type ("int[] Run", "Task<string[]> Run") is skipped, not a boundary.
        private static string ReadModifierText(string source, CodeTextMask codeTextMask, int declarationIndex)
        {
            int startIndex = declarationIndex - 1;
            while (startIndex >= 0)
            {
                if (codeTextMask.IsCodeAt(startIndex) && IsModifierBoundary(source[startIndex]))
                {
                    int rankOpenIndex = ReadRankSpecifierOpenIndex(source, codeTextMask, startIndex);
                    if (rankOpenIndex < 0)
                    {
                        break;
                    }

                    startIndex = rankOpenIndex;
                }

                startIndex--;
            }

            return ReadCodeOnlyText(source, codeTextMask, startIndex + 1, declarationIndex);
        }

        private static bool IsModifierBoundary(char character)
        {
            return character == ';' || character == '{' || character == '}' || character == ']';
        }

        // Returns the '[' of a rank specifier ("[]", "[,]") closed at the index, or -1 for any other bracket such as
        // an attribute. A rank specifier holds only commas and spaces and follows a type: a name, '>', ']', '?' or ')'.
        private static int ReadRankSpecifierOpenIndex(string source, CodeTextMask codeTextMask, int closeIndex)
        {
            if (source[closeIndex] != ']')
            {
                return -1;
            }

            int index = closeIndex - 1;
            while (index >= 0 && codeTextMask.IsCodeAt(index) && (source[index] == ',' || char.IsWhiteSpace(source[index])))
            {
                index--;
            }

            if (index < 0 || !codeTextMask.IsCodeAt(index) || source[index] != '[')
            {
                return -1;
            }

            int ownerIndex = ReadPreviousCodeIndex(source, codeTextMask, index - 1);
            return ownerIndex >= 0 && IsNullableTypeSuffixOwner(source[ownerIndex]) ? index : -1;
        }

        // Scans the class body once, front to back, skipping nested type bodies, so building the index stays linear.
        private static DeclaredNames ReadDeclaredNames(
            string source,
            CodeTextMask codeTextMask,
            TypeBody typeBody,
            List<TypeBody> typeBodies)
        {
            Dictionary<int, int> nestedBodyCloseIndexByOpenIndex = new();
            foreach (TypeBody candidate in typeBodies)
            {
                if (candidate.OpenBraceIndex > typeBody.OpenBraceIndex &&
                    candidate.CloseBraceIndex < typeBody.CloseBraceIndex)
                {
                    nestedBodyCloseIndexByOpenIndex[candidate.OpenBraceIndex] = candidate.CloseBraceIndex;
                }
            }

            DeclaredNames declaredNames = new();
            BodyScanState state = new();
            for (int index = typeBody.OpenBraceIndex + 1; index < typeBody.CloseBraceIndex; index++)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                if (nestedBodyCloseIndexByOpenIndex.TryGetValue(index, out int nestedCloseIndex))
                {
                    index = nestedCloseIndex;
                    continue;
                }

                if (state.TryTrackBracket(source[index]))
                {
                    continue;
                }

                if (!IsIdentifierStartAt(source, codeTextMask, index))
                {
                    continue;
                }

                int identifierEndIndex = ReadIdentifierEndIndex(source, index);
                AddDeclaredName(source, codeTextMask, index, identifierEndIndex, state, declaredNames);
                index = identifierEndIndex - 1;
            }

            return declaredNames;
        }

        private static void AddDeclaredName(
            string source,
            CodeTextMask codeTextMask,
            int identifierStartIndex,
            int identifierEndIndex,
            BodyScanState state,
            DeclaredNames declaredNames)
        {
            if (!IsDeclarationName(source, codeTextMask, identifierStartIndex, state.ParenDepth))
            {
                return;
            }

            string name = source.Substring(identifierStartIndex, identifierEndIndex - identifierStartIndex);
            declaredNames.ScopeNames.Add(name);

            // Parameters sit at brace depth 1 too, but inside the parameter list; they are not members.
            if (state.BraceDepth != 1 || state.ParenDepth != 0)
            {
                return;
            }

            declaredNames.MemberNames.Add(name);
            if (AccessModifierRegex.IsMatch(ReadModifierText(source, codeTextMask, identifierStartIndex)))
            {
                declaredNames.NonPrivateMemberNames.Add(name);
            }
        }

        private static bool IsIdentifierStartAt(string source, CodeTextMask codeTextMask, int index)
        {
            if (!IsIdentifierStartCharacter(source[index]))
            {
                return false;
            }

            return index == 0 || !codeTextMask.IsCodeAt(index - 1) || !IsIdentifierCharacter(source[index - 1]);
        }

        private readonly struct TypeBody
        {
            public TypeBody(Match declaration, int openBraceIndex, int closeBraceIndex)
            {
                Declaration = declaration;
                OpenBraceIndex = openBraceIndex;
                CloseBraceIndex = closeBraceIndex;
                // Only classes and record classes have a base class; structs and interfaces list interfaces only.
                IsClass = IsClassDeclarationKeyword(declaration.Value);
            }

            public Match Declaration { get; }
            public int OpenBraceIndex { get; }
            public int CloseBraceIndex { get; }
            public bool IsClass { get; }
        }

        // Tracks brace and bracket depth while scanning a class body. The bracket depth restarts inside every brace
        // block so a lambda body inside an argument list reads its own statements at depth 0.
        private sealed class BodyScanState
        {
            private readonly Stack<int> _savedParenDepths = new();

            public int BraceDepth { get; private set; } = 1;
            public int ParenDepth { get; private set; }

            public bool TryTrackBracket(char character)
            {
                switch (character)
                {
                    case '{':
                        _savedParenDepths.Push(ParenDepth);
                        ParenDepth = 0;
                        BraceDepth++;
                        return true;
                    case '}':
                        BraceDepth--;
                        ParenDepth = _savedParenDepths.Count > 0 ? _savedParenDepths.Pop() : 0;
                        return true;
                    case '(':
                    case '[':
                        ParenDepth++;
                        return true;
                    case ')':
                    case ']':
                        ParenDepth = Math.Max(0, ParenDepth - 1);
                        return true;
                    default:
                        return false;
                }
            }
        }

        private sealed class DeclaredNames
        {
            public HashSet<string> ScopeNames { get; } = new(StringComparer.Ordinal);
            public HashSet<string> MemberNames { get; } = new(StringComparer.Ordinal);
            public HashSet<string> NonPrivateMemberNames { get; } = new(StringComparer.Ordinal);
        }
    }
}
