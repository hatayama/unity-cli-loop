using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves missing using directives by retrying compilation with candidate namespaces.
    /// Parses CS0246/CS0103 from CompilerMessage and looks up types via AssemblyTypeIndex.
    /// </summary>
    internal sealed class AutoUsingResolver
    {
        private const int MaxRetries = 3;

        public async Task<AutoUsingResult> ResolveAsync(
            string sourcePath,
            string dllPath,
            string originalSource,
            List<string> currentReferences,
            System.Func<string, string, List<string>, CancellationToken, Task<CompilerMessage[]>> buildFunc,
            CancellationToken ct)
        {
            Debug.Assert(originalSource != null, "originalSource must not be null");

            string currentSource = originalSource;
            Dictionary<string, List<string>> ambiguousCandidates = new();
            HashSet<string> addedNamespaces = new(System.StringComparer.Ordinal);
            List<string> mutableReferences = currentReferences != null
                ? new List<string>(currentReferences)
                : new List<string>();
            HashSet<string> addedAssemblyReferences = new(System.StringComparer.OrdinalIgnoreCase);
            double referenceResolutionMilliseconds = 0;

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                File.WriteAllText(sourcePath, currentSource);
                // AssemblyBuilder retries must stay on Unity's main thread because the compiler API is not thread-safe.
                CompilerMessage[] messages = await buildFunc(sourcePath, dllPath, mutableReferences, ct);

                List<string> unresolvedTypes = ExtractUnresolvedTypes(messages);
                if (unresolvedTypes.Count == 0)
                {
                    return new AutoUsingResult(
                        currentSource,
                        messages,
                        ambiguousCandidates,
                        addedNamespaces,
                        addedAssemblyReferences.ToList(),
                        referenceResolutionMilliseconds);
                }

                HashSet<string> namespacesToAdd = new(System.StringComparer.Ordinal);
                List<string> assemblyReferencesToAdd = new();
                AssemblyTypeIndex index = AssemblyTypeIndex.Instance;
                Stopwatch resolutionStopwatch = Stopwatch.StartNew();

                foreach (string typeName in unresolvedTypes)
                {
                    if (string.IsNullOrEmpty(typeName)) continue;

                    List<string> candidates = index.FindNamespacesForType(typeName);

                    if (candidates.Count == 1 && addedNamespaces.Add(candidates[0]))
                    {
                        namespacesToAdd.Add(candidates[0]);
                    }
                    else if (candidates.Count > 1)
                    {
                        ambiguousCandidates[typeName] = candidates;
                    }

                    List<string> assemblyCandidates = index.FindAssemblyLocationsForIdentifier(typeName);
                    if (assemblyCandidates.Count == 1)
                    {
                        AddAssemblyReferenceIfMissing(assemblyReferencesToAdd, mutableReferences, assemblyCandidates[0]);
                    }
                }

                resolutionStopwatch.Stop();
                referenceResolutionMilliseconds += resolutionStopwatch.Elapsed.TotalMilliseconds;

                if (namespacesToAdd.Count == 0 && assemblyReferencesToAdd.Count == 0)
                {
                    break;
                }

                if (namespacesToAdd.Count > 0)
                {
                    currentSource = InsertUsingDirectives(currentSource, namespacesToAdd);
                }

                foreach (string assemblyReference in assemblyReferencesToAdd)
                {
                    addedAssemblyReferences.Add(assemblyReference);
                    mutableReferences.Add(assemblyReference);
                }
            }

            // Final compilation with all added usings
            File.WriteAllText(sourcePath, currentSource);
            CompilerMessage[] finalMessages = await buildFunc(sourcePath, dllPath, mutableReferences, ct);
            return new AutoUsingResult(
                currentSource,
                finalMessages,
                ambiguousCandidates,
                addedNamespaces,
                addedAssemblyReferences.ToList(),
                referenceResolutionMilliseconds);
        }

        private static List<string> ExtractUnresolvedTypes(CompilerMessage[] messages)
        {
            List<string> types = new();
            foreach (CompilerMessage msg in messages)
            {
                if (msg.type != CompilerMessageType.Error) continue;

                // CS0246: The type or namespace name 'X' could not be found
                // CS0103: The name 'X' does not exist in the current context
                if (!msg.message.Contains("CS0246")
                    && !msg.message.Contains("CS0103")
                    && !msg.message.Contains("CS1069"))
                {
                    continue;
                }

                string typeName = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(msg.message);
                if (!string.IsNullOrEmpty(typeName))
                {
                    types.Add(typeName);
                }
            }
            return types;
        }

        internal static string InsertUsingDirectives(string source, IEnumerable<string> namespaces)
        {
            StringBuilder sb = new();
            foreach (string ns in namespaces)
            {
                sb.AppendLine($"using {ns};");
            }
            sb.Append(source);
            return sb.ToString();
        }

        internal static void AddAssemblyReferenceIfMissing(
            List<string> assemblyReferencesToAdd,
            List<string> currentReferences,
            string assemblyReference)
        {
            if (string.IsNullOrEmpty(assemblyReference))
            {
                return;
            }

            if (ContainsAssemblyReference(currentReferences, assemblyReference))
            {
                return;
            }

            if (ContainsAssemblyReference(assemblyReferencesToAdd, assemblyReference))
            {
                return;
            }

            assemblyReferencesToAdd.Add(assemblyReference);
        }

        internal static bool ContainsAssemblyReference(List<string> references, string assemblyReference)
        {
            string assemblyIdentity = GetAssemblyIdentityKey(assemblyReference);
            foreach (string existingReference in references)
            {
                if (string.Equals(
                    GetAssemblyIdentityKey(existingReference),
                    assemblyIdentity,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetAssemblyIdentityKey(string assemblyReference)
        {
            if (string.IsNullOrEmpty(assemblyReference))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileNameWithoutExtension(assemblyReference);
            if (string.IsNullOrEmpty(fileName))
            {
                return assemblyReference;
            }

            return fileName;
        }
    }

    /// <summary>
    /// Carries the result data produced by Auto Using behavior.
    /// </summary>
    internal sealed class AutoUsingResult
    {
        public string UpdatedSource { get; }
        public CompilerMessage[] Messages { get; }
        public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

        public IReadOnlyCollection<string> AddedNamespaces { get; }

        public IReadOnlyCollection<string> AddedAssemblyReferences { get; }

        public double ReferenceResolutionMilliseconds { get; }

        public AutoUsingResult(
            string updatedSource,
            CompilerMessage[] messages,
            Dictionary<string, List<string>> ambiguousTypeCandidates,
            IReadOnlyCollection<string> addedNamespaces,
            IReadOnlyCollection<string> addedAssemblyReferences,
            double referenceResolutionMilliseconds)
        {
            UpdatedSource = updatedSource;
            Messages = messages;
            AmbiguousTypeCandidates = ambiguousTypeCandidates;
            AddedNamespaces = addedNamespaces;
            AddedAssemblyReferences = addedAssemblyReferences;
            ReferenceResolutionMilliseconds = referenceResolutionMilliseconds;
        }
    }
}
