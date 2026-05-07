using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Scans wrapped source for potential type identifiers and pre-injects using directives
    /// before the first compilation, avoiding AutoUsingResolver retry loops (~590ms/retry saved).
    /// Only applies to script mode (wrapped source from WrapperTemplate); raw mode is excluded.
    /// </summary>
    internal static class PreUsingResolver
    {
        private static readonly HashSet<string> ExcludedIdentifiers = new(System.StringComparer.Ordinal)
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte",
            "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum",
            "event", "explicit", "extern", "false", "finally", "fixed", "float",
            "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object",
            "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "record", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while",
            "yield",
            "String", "Int32", "Int64", "Boolean", "Single", "Double", "Byte",
            "Object", "Void", "Decimal", "Char", "UInt32", "UInt64", "Int16", "UInt16",
            "SByte", "IntPtr", "UIntPtr",
            "Task", "Dictionary", "CancellationToken", "ExecuteAsync", "Execute"
        };

        public static PreUsingResult Resolve(string wrappedSource, AssemblyTypeIndex index)
        {
            Debug.Assert(wrappedSource != null, "wrappedSource must not be null");
            Debug.Assert(index != null, "index must not be null");

            HashSet<string> existingNamespaces = ExtractExistingNamespaces(wrappedSource);
            string userCodeSection = ExtractUserCodeSection(wrappedSource);
            HashSet<string> candidateTypes = ExtractTypeIdentifiers(userCodeSection);
            HashSet<string> qualifiedTypeIdentifiers = ExtractQualifiedTypeIdentifiers(userCodeSection);

            HashSet<string> namespacesToAdd = new(System.StringComparer.Ordinal);
            List<string> assemblyReferencesToAdd = new();
            foreach (string typeName in candidateTypes)
            {
                List<string> namespaces = index.FindNamespacesForType(typeName);
                if (namespaces.Count == 1 && !existingNamespaces.Contains(namespaces[0]))
                {
                    namespacesToAdd.Add(namespaces[0]);
                }

                List<string> assemblyReferences = index.FindAssemblyLocationsForType(typeName);
                if (assemblyReferences.Count == 1)
                {
                    AddAssemblyReferenceIfMissing(assemblyReferencesToAdd, assemblyReferences[0]);
                }
            }

            foreach (string typeName in qualifiedTypeIdentifiers)
            {
                List<string> assemblyReferences = index.FindAssemblyLocationsForIdentifier(typeName);
                if (assemblyReferences.Count == 1)
                {
                    AddAssemblyReferenceIfMissing(assemblyReferencesToAdd, assemblyReferences[0]);
                }
            }

            if (namespacesToAdd.Count == 0)
            {
                return new PreUsingResult(
                    wrappedSource,
                    System.Array.Empty<string>(),
                    assemblyReferencesToAdd);
            }

            string updatedSource = AutoUsingResolver.InsertUsingDirectives(wrappedSource, namespacesToAdd);
            return new PreUsingResult(updatedSource, namespacesToAdd, assemblyReferencesToAdd);
        }

        private static HashSet<string> ExtractExistingNamespaces(string source)
        {
            HashSet<string> namespaces = new(System.StringComparer.Ordinal);
            int pos = 0;
            int length = source.Length;

            while (pos < length)
            {
                pos = SourceShaper.SkipWhitespace(source, pos);
                if (pos >= length) break;

                if (source[pos] == '#')
                {
                    pos = SkipToEndOfLine(source, pos);
                    continue;
                }

                // Skip comments that appear before using directives
                int skipped = SourceShaper.AdvanceOneTokenPublic(source, pos);
                if (skipped > pos + 1 && !char.IsLetterOrDigit(source[pos]) && source[pos] != '_')
                {
                    pos = skipped;
                    continue;
                }

                // Handle "global using Ns;" — advance past "global" to reach "using"
                int usingPos = pos;
                if (SourceShaper.StartsWithKeyword(source, pos, "global"))
                {
                    usingPos = SourceShaper.SkipWhitespace(source, pos + 6);
                }

                if (!SourceShaper.StartsWithKeyword(source, usingPos, "using"))
                {
                    break;
                }

                int afterUsing = SourceShaper.SkipWhitespace(source, usingPos + 5);
                if (SourceShaper.StartsWithKeyword(source, afterUsing, "static") ||
                    SourceShaper.StartsWithKeyword(source, afterUsing, "var") ||
                    (afterUsing < length && source[afterUsing] == '('))
                {
                    pos = SkipToSemicolon(source, pos);
                    continue;
                }

                int semiPos = source.IndexOf(';', afterUsing);
                if (semiPos > afterUsing)
                {
                    string ns = source.Substring(afterUsing, semiPos - afterUsing).Trim();
                    int eqIdx = ns.IndexOf('=');
                    if (eqIdx >= 0)
                    {
                        ns = ns.Substring(eqIdx + 1).Trim();
                    }
                    if (ns.Length > 0)
                    {
                        namespaces.Add(ns);
                    }
                }
                pos = semiPos >= 0 ? semiPos + 1 : length;
            }

            return namespaces;
        }

        internal static HashSet<string> ExtractTypeIdentifiers(string source)
        {
            HashSet<string> identifiers = new(System.StringComparer.Ordinal);
            int pos = 0;
            int length = source.Length;
            bool prevWasDot = false;

            while (pos < length)
            {
                char c = source[pos];

                if (char.IsWhiteSpace(c))
                {
                    pos++;
                    continue;
                }

                int advanced = SourceShaper.AdvanceOneTokenPublic(source, pos);
                if (advanced > pos + 1 && !char.IsLetterOrDigit(source[pos]) && source[pos] != '_')
                {
                    prevWasDot = false;
                    pos = advanced;
                    continue;
                }

                if (c == '.')
                {
                    prevWasDot = true;
                    pos++;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = pos;
                    while (pos < length && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_'))
                    {
                        pos++;
                    }

                    if (!prevWasDot && char.IsUpper(c))
                    {
                        string identifier = source.Substring(start, pos - start);

                        // Member initializers (Name = ...), named arguments (Name: ...),
                        // and labels (Name:) are not type candidates; skip them.
                        // Exclude == so comparisons are not mistakenly filtered.
                        int next = SourceShaper.SkipWhitespace(source, pos);
                        bool looksLikeMemberOrLabel =
                            next < length &&
                            (source[next] == ':' ||
                             (source[next] == '=' && (next + 1 >= length || source[next + 1] != '=')));

                        if (!looksLikeMemberOrLabel && !ExcludedIdentifiers.Contains(identifier))
                        {
                            identifiers.Add(identifier);
                        }
                    }

                    prevWasDot = false;
                    continue;
                }

                prevWasDot = false;
                pos = advanced;
            }

            return identifiers;
        }

        internal static HashSet<string> ExtractQualifiedTypeIdentifiers(string source)
        {
            HashSet<string> identifiers = new(System.StringComparer.Ordinal);
            int pos = 0;
            int length = source.Length;
            bool sawDot = false;
            List<string> qualifiedChainParts = new();

            while (pos < length)
            {
                char c = source[pos];

                if (char.IsWhiteSpace(c))
                {
                    pos++;
                    continue;
                }

                int advanced = SourceShaper.AdvanceOneTokenPublic(source, pos);
                if (advanced > pos + 1 && !char.IsLetterOrDigit(source[pos]) && source[pos] != '_')
                {
                    sawDot = false;
                    qualifiedChainParts.Clear();
                    pos = advanced;
                    continue;
                }

                if (c == '.')
                {
                    sawDot = true;
                    pos++;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = pos;
                    while (pos < length && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_'))
                    {
                        pos++;
                    }

                    string identifier = source.Substring(start, pos - start);
                    if (!sawDot)
                    {
                        qualifiedChainParts.Clear();
                        qualifiedChainParts.Add(identifier);
                    }
                    else
                    {
                        qualifiedChainParts.Add(identifier);
                        if (ShouldResolveQualifiedTypeAssembly(qualifiedChainParts))
                        {
                            identifiers.Add(string.Join(".", qualifiedChainParts));
                        }
                    }

                    sawDot = false;
                    continue;
                }

                sawDot = false;
                qualifiedChainParts.Clear();
                pos = advanced;
            }

            return identifiers;
        }

        private static bool ShouldResolveQualifiedTypeAssembly(IReadOnlyList<string> qualifiedChainParts)
        {
            return qualifiedChainParts != null && qualifiedChainParts.Count >= 2;
        }

        // WrapperTemplate marks user code with #line directives;
        // scanning only this region avoids false positives from boilerplate identifiers
        private static string ExtractUserCodeSection(string wrappedSource)
        {
            string startMarker = WrapperTemplate.UserCodeStartMarker;
            string endMarker = WrapperTemplate.UserCodeEndMarker;

            int startIdx = wrappedSource.IndexOf(startMarker, System.StringComparison.Ordinal);
            // Wrapped source from WrapperTemplate should always contain the marker
            Debug.Assert(startIdx >= 0, "UserCodeStartMarker not found in wrappedSource");
            if (startIdx < 0)
            {
                return wrappedSource;
            }

            int codeStart = wrappedSource.IndexOf('\n', startIdx);
            Debug.Assert(codeStart >= 0, "newline after UserCodeStartMarker not found");
            if (codeStart < 0)
            {
                return wrappedSource;
            }
            codeStart++;

            int endIdx = wrappedSource.IndexOf(endMarker, codeStart, System.StringComparison.Ordinal);
            Debug.Assert(endIdx >= 0, "UserCodeEndMarker not found in wrappedSource");
            if (endIdx < 0)
            {
                return wrappedSource.Substring(codeStart);
            }

            return wrappedSource.Substring(codeStart, endIdx - codeStart);
        }

        private static int SkipToEndOfLine(string s, int pos)
        {
            while (pos < s.Length && s[pos] != '\n') pos++;
            if (pos < s.Length) pos++;
            return pos;
        }

        private static int SkipToSemicolon(string s, int pos)
        {
            int semi = s.IndexOf(';', pos);
            return semi >= 0 ? semi + 1 : s.Length;
        }

        private static void AddAssemblyReferenceIfMissing(
            List<string> assemblyReferencesToAdd,
            string assemblyReference)
        {
            if (string.IsNullOrEmpty(assemblyReference))
            {
                return;
            }

            foreach (string existingReference in assemblyReferencesToAdd)
            {
                if (string.Equals(existingReference, assemblyReference, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            assemblyReferencesToAdd.Add(assemblyReference);
        }
    }

    /// <summary>
    /// Carries the result data produced by Pre Using behavior.
    /// </summary>
    internal sealed class PreUsingResult
    {
        public string UpdatedSource { get; }

        public IReadOnlyCollection<string> AddedNamespaces { get; }

        public IReadOnlyCollection<string> AddedAssemblyReferences { get; }

        public PreUsingResult(
            string updatedSource,
            IReadOnlyCollection<string> addedNamespaces,
            IReadOnlyCollection<string> addedAssemblyReferences)
        {
            UpdatedSource = updatedSource;
            AddedNamespaces = addedNamespaces;
            AddedAssemblyReferences = addedAssemblyReferences;
        }
    }
}
