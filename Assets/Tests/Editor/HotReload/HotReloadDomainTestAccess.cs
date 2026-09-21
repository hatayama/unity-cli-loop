using System.Collections.Generic;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Seeds and reads the hot-reload domain the way the apply pipeline does, so a test can arrange
    /// generation state without reaching inside the aggregate.
    /// </summary>
    /// <remarks>
    /// Why the pipeline's own order: a patch may only exist for a method whose shim its generation
    /// registered, so a test that patched without registering would be arranging a state production
    /// can never reach.
    /// </remarks>
    internal sealed class HotReloadDomainTestAccess
    {
        // Any non-empty byte array satisfies a shim generation that no test loads bytes from.
        private static readonly byte[] PlaceholderAssemblyBytes = { 0x4D, 0x5A };

        internal HotReloadDomain Domain => HotReloadCompositionRoot.Services.Domain;

        /// <summary>Applies a patch after registering its shim, as the file entry applier does.</summary>
        internal HotReloadPatchResult ApplyPatch(
            MethodBase method,
            MethodInfo shimMethod,
            HotReloadPatchShape patchShape,
            string projectRelativePath)
        {
            HotReloadFileGeneration generation = GetOrBeginShimGeneration(projectRelativePath);
            if (method != null && shimMethod != null)
            {
                generation.RegisterShimMethod(
                    method,
                    new HotReloadShimMethodEntry(
                        shimMethod,
                        patchShape == HotReloadPatchShape.Delegation,
                        sourceStartLine: 0,
                        sourceEndLine: 0));
            }

            return HotReloadCompositionRoot.Services.Patcher.Apply(method, shimMethod, patchShape, projectRelativePath);
        }

        internal HotReloadFileGeneration GetOrBeginShimGeneration(string projectRelativePath)
        {
            HotReloadFileGeneration generation = Domain.FindGeneration(projectRelativePath);
            if (generation != null && generation.HasShimGeneration)
            {
                return generation;
            }

            return Domain.BeginGeneration(
                projectRelativePath,
                PlaceholderAssemblyBytes,
                pdbBytes: null,
                loadedAssembly: typeof(HotReloadDomainTestAccess).Assembly);
        }

        internal HotReloadFileGeneration GetOrBeginAddedMemberGeneration(string projectRelativePath)
        {
            HotReloadFileGeneration generation = Domain.FindGeneration(projectRelativePath);
            if (generation != null && generation.HasAddedMemberGeneration)
            {
                return generation;
            }

            return Domain.BeginAddedMemberOnlyGeneration(projectRelativePath);
        }

        internal void RegisterAddedMember(
            string projectRelativePath,
            string methodKey,
            MethodInfo shimMethod,
            string filePath)
        {
            Domain.BeginAddedMemberOnlyGeneration(projectRelativePath)
                .RegisterAddedMethod(methodKey, shimMethod, filePath, "Added", "Fixture");
        }

        internal void ReplaceAddedFields(
            string projectRelativePath,
            IReadOnlyList<string> addedFieldFullNames,
            IReadOnlyList<HotReloadAddedFieldDeclaration> addedFieldDeclarations = null)
        {
            // Null initializers: these tests pin which fields a type holds, not what a previous
            // reload initialized them with. Declarations default to none for the same reason: a
            // test that needs them passes its own rows. No field is marked serialized: the
            // warning that reads that mark is covered end to end instead.
            GetOrBeginAddedMemberGeneration(projectRelativePath).ReplaceAddedFields(
                addedFieldFullNames,
                null,
                addedFieldDeclarations,
                null);
        }

        /// <summary>
        /// Replaces the added fields of one file with serialized ones, as a run whose worker
        /// marked every declaration with a serialization attribute would.
        /// </summary>
        internal void ReplaceSerializedAddedFields(
            string projectRelativePath,
            IReadOnlyList<HotReloadSerializedAddedField> serializedFields)
        {
            List<string> fullNames = new List<string>();
            foreach (HotReloadSerializedAddedField field in serializedFields)
            {
                fullNames.Add(field.DeclaringTypeName.ToReflectionName().Value + "." + field.FieldName);
            }

            GetOrBeginAddedMemberGeneration(projectRelativePath).ReplaceAddedFields(
                fullNames,
                null,
                null,
                serializedFields);
        }

        internal void RecordSupersededSignature(
            string projectRelativePath,
            string oldMethodKey,
            string replacementDisplayName)
        {
            GetOrBeginAddedMemberGeneration(projectRelativePath)
                .RecordSupersededSignature(oldMethodKey, replacementDisplayName);
        }

        internal bool HasShimGeneration(string requestedPath)
        {
            HotReloadFileGeneration generation = Domain.FindGenerationForRequestedPath(requestedPath);
            return generation != null && generation.HasShimGeneration;
        }

        internal bool HasAddedMemberGeneration(string projectRelativePath)
        {
            HotReloadFileGeneration generation = Domain.FindGeneration(projectRelativePath);
            return generation != null && generation.HasAddedMemberGeneration;
        }

    }
}
