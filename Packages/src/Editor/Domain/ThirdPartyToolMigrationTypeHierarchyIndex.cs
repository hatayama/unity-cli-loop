using System;
using System.Collections.Generic;
using System.Diagnostics;

using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Index of the classes in a project's C# sources, their base classes, and the names they declare, used to
    /// decide whether an unqualified, this. or base. call inside a derived class reaches the class that declares
    /// a removed timing signature. Every question that the text cannot settle is answered "not reachable".
    /// </summary>
    public sealed class ThirdPartyToolMigrationTypeHierarchyIndex
    {
        public static readonly ThirdPartyToolMigrationTypeHierarchyIndex Empty =
            new(new Dictionary<string, IndexedClass>(StringComparer.Ordinal));

        private readonly Dictionary<string, IndexedClass> _classesByQualifiedName;

        private ThirdPartyToolMigrationTypeHierarchyIndex(Dictionary<string, IndexedClass> classesByQualifiedName)
        {
            _classesByQualifiedName = classesByQualifiedName;
        }

        public static ThirdPartyToolMigrationTypeHierarchyIndex Build(IReadOnlyList<string> sources)
        {
            Debug.Assert(sources != null, "sources must not be null");

            Dictionary<string, List<ThirdPartyToolMigrationTypeHierarchyClassPart>> partsByQualifiedName =
                new(StringComparer.Ordinal);
            foreach (string source in sources)
            {
                Debug.Assert(source != null, "sources must not contain null");

                foreach (ThirdPartyToolMigrationTypeHierarchyClassPart part in
                         ThirdPartyToolMigrationTypeHierarchySourceReader.ReadClassParts(source))
                {
                    if (!partsByQualifiedName.TryGetValue(part.QualifiedName, out List<ThirdPartyToolMigrationTypeHierarchyClassPart> parts))
                    {
                        parts = new List<ThirdPartyToolMigrationTypeHierarchyClassPart>();
                        partsByQualifiedName.Add(part.QualifiedName, parts);
                    }

                    parts.Add(part);
                }
            }

            Dictionary<string, IndexedClass> classesByQualifiedName = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<ThirdPartyToolMigrationTypeHierarchyClassPart>> entry in partsByQualifiedName)
            {
                classesByQualifiedName.Add(entry.Key, new IndexedClass(entry.Value));
            }

            return new ThirdPartyToolMigrationTypeHierarchyIndex(classesByQualifiedName);
        }

        /// <summary>
        /// Decides whether an unqualified or this. call of the member inside the containing class binds to the member
        /// that the declaring class declares. A name the containing class uses for anything (members, locals,
        /// parameters) could take the call instead, so it keeps the call unresolved.
        /// </summary>
        public bool IsInheritedMemberReachable(string containingTypeName, string memberName, string declaringTypeName)
        {
            Debug.Assert(containingTypeName != null, "containingTypeName must not be null");
            Debug.Assert(!string.IsNullOrEmpty(memberName), "memberName must not be null or empty");
            Debug.Assert(declaringTypeName != null, "declaringTypeName must not be null");

            if (!TryGetUnambiguous(containingTypeName, out IndexedClass containingClass))
            {
                return false;
            }

            if (containingClass.ScopeNames.Contains(memberName))
            {
                return false;
            }

            return IsBaseMemberReachable(containingTypeName, memberName, declaringTypeName);
        }

        /// <summary>
        /// Decides whether a base. call of the member inside the containing class binds to the member that the
        /// declaring class declares, walking the base classes up from the containing class.
        /// </summary>
        public bool IsBaseMemberReachable(string containingTypeName, string memberName, string declaringTypeName)
        {
            Debug.Assert(containingTypeName != null, "containingTypeName must not be null");
            Debug.Assert(!string.IsNullOrEmpty(memberName), "memberName must not be null or empty");
            Debug.Assert(declaringTypeName != null, "declaringTypeName must not be null");

            HashSet<string> visited = new(StringComparer.Ordinal) { containingTypeName };
            string current = ResolveBaseTypeName(containingTypeName);
            while (current.Length > 0)
            {
                if (!visited.Add(current))
                {
                    return false;
                }

                if (!TryGetUnambiguous(current, out IndexedClass currentClass))
                {
                    return false;
                }

                // The first class up the chain that declares the name owns the call, whatever overload it picks.
                if (currentClass.MemberNames.Contains(memberName))
                {
                    return string.Equals(current, declaringTypeName, StringComparison.Ordinal) &&
                        currentClass.NonPrivateMemberNames.Contains(memberName);
                }

                current = ResolveBaseTypeName(current);
            }

            return false;
        }

        // Partial parts may disagree on their first base list entry because interfaces can be listed in any part.
        // The base class is the single indexed class the parts resolve to; none means no base class in the index,
        // and more than one cannot be decided.
        private string ResolveBaseTypeName(string qualifiedName)
        {
            if (!TryGetUnambiguous(qualifiedName, out IndexedClass indexedClass))
            {
                return string.Empty;
            }

            HashSet<string> resolved = new(StringComparer.Ordinal);
            foreach (ThirdPartyToolMigrationTypeHierarchyClassPart part in indexedClass.Parts)
            {
                if (part.WrittenBaseName.Length == 0)
                {
                    continue;
                }

                List<string> hits = ResolvePartCandidates(part);
                if (hits.Count >= 2)
                {
                    return string.Empty;
                }

                if (hits.Count == 1)
                {
                    resolved.Add(hits[0]);
                }
            }

            if (resolved.Count != 1)
            {
                return string.Empty;
            }

            foreach (string baseTypeName in resolved)
            {
                return baseTypeName;
            }

            return string.Empty;
        }

        // Instead of replaying C#'s lookup order, every qualified name the written base name could mean in the part's
        // context is collected, and the name is resolved only when exactly one of them is an indexed class.
        private List<string> ResolvePartCandidates(ThirdPartyToolMigrationTypeHierarchyClassPart part)
        {
            string writtenBaseName = part.WrittenBaseName;
            string name = NormalizeTypeNameForComparison(writtenBaseName);
            List<string> candidates = new();
            if (writtenBaseName.StartsWith("global::", StringComparison.Ordinal))
            {
                candidates.Add(name);
            }
            else
            {
                foreach (string enclosingTypeName in part.EnclosingTypeNames)
                {
                    candidates.Add(enclosingTypeName + "." + name);
                }

                AddNamespaceCandidates(candidates, part.NamespaceName, name);
                foreach (string usingNamespace in part.UsingNamespaces)
                {
                    candidates.Add(usingNamespace + "." + name);
                }
            }

            List<string> hits = new();
            foreach (string candidate in candidates)
            {
                if (_classesByQualifiedName.ContainsKey(candidate) && !hits.Contains(candidate))
                {
                    hits.Add(candidate);
                }
            }

            return hits;
        }

        // The name may be written relative to the namespace of the declaration or any namespace that encloses it.
        private static void AddNamespaceCandidates(List<string> candidates, string namespaceName, string name)
        {
            string current = namespaceName;
            while (true)
            {
                candidates.Add(current.Length == 0 ? name : current + "." + name);
                if (current.Length == 0)
                {
                    return;
                }

                int lastDotIndex = current.LastIndexOf('.');
                current = lastDotIndex < 0 ? string.Empty : current.Substring(0, lastDotIndex);
            }
        }

        private bool TryGetUnambiguous(string qualifiedName, out IndexedClass indexedClass)
        {
            if (!_classesByQualifiedName.TryGetValue(qualifiedName, out indexedClass))
            {
                return false;
            }

            return !indexedClass.IsAmbiguous;
        }

        /// <summary>
        /// All parts of one qualified class name merged together.
        /// </summary>
        private sealed class IndexedClass
        {
            public IndexedClass(List<ThirdPartyToolMigrationTypeHierarchyClassPart> parts)
            {
                Debug.Assert(parts != null && parts.Count > 0, "parts must not be null or empty");

                Parts = parts;
                bool hasNonPartialPart = false;
                foreach (ThirdPartyToolMigrationTypeHierarchyClassPart part in parts)
                {
                    hasNonPartialPart |= !part.IsPartial;
                    ScopeNames.UnionWith(part.ScopeNames);
                    MemberNames.UnionWith(part.MemberNames);
                    NonPrivateMemberNames.UnionWith(part.NonPrivateMemberNames);
                }

                // Two declarations of one name are only legal as partial parts; anything else is either a duplicate
                // in another assembly or text this reader misread, and neither can be resolved safely.
                IsAmbiguous = parts.Count >= 2 && hasNonPartialPart;
            }

            public List<ThirdPartyToolMigrationTypeHierarchyClassPart> Parts { get; }
            public HashSet<string> ScopeNames { get; } = new(StringComparer.Ordinal);
            public HashSet<string> MemberNames { get; } = new(StringComparer.Ordinal);
            public HashSet<string> NonPrivateMemberNames { get; } = new(StringComparer.Ordinal);
            public bool IsAmbiguous { get; }
        }
    }
}
