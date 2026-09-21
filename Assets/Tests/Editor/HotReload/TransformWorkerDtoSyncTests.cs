using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Guards the transform worker's wire shape: the worker's own payload types and the Editor's
    /// data transfer objects are written twice, once on each side of the process boundary, so a
    /// field added to one side and forgotten on the other has to fail here rather than silently
    /// serialize to nothing at run time.
    /// </summary>
    public sealed class TransformWorkerDtoSyncTests
    {
        // The Editor type on the left is serialized by the worker type named on the right. The
        // worker types live in the global namespace of worker.dll, which Unity never compiles.
        private static readonly Dictionary<Type, string> WorkerTypeNamesByEditorType =
            new Dictionary<Type, string>
            {
                { typeof(TransformWorkerInputDto), "WorkerInput" },
                { typeof(TransformWorkerIntroducedTypeArtifactDto), "WorkerIntroducedTypeArtifact" },
                {
                    typeof(TransformWorkerIntroducedTypeArtifactTypeDto),
                    "WorkerIntroducedTypeArtifactType"
                },
                { typeof(TransformWorkerSourceDto), "WorkerSourceInput" },
                { typeof(TransformWorkerFileOutputDto), "WorkerFileOutput" },
                { typeof(TransformWorkerAddedFieldDeclarationDto), "WorkerAddedFieldDeclaration" },
                { typeof(TransformWorkerIntroducedTypeReuseDto), "WorkerIntroducedTypeReuse" },
                { typeof(TransformWorkerIntroducedTypeDto), "WorkerIntroducedType" },
                { typeof(TransformWorkerOutputDto), "WorkerOutput" },
                {
                    typeof(TransformWorkerRemovedMethodSignatureDto),
                    "WorkerRemovedMethodSignature"
                },
                { typeof(TransformWorkerRemovedMemberDto), "WorkerRemovedMember" },
                { typeof(TransformWorkerUnchangedMethodDto), "WorkerUnchangedMethod" },
                { typeof(TransformWorkerEntryDto), "WorkerEntry" },
                { typeof(TransformWorkerSkippedDto), "WorkerSkipped" },
                { typeof(TransformWorkerReasonDto), "WorkerReason" }
            };

        /// <summary>
        /// What: every mapped payload type carries the same field names on both sides of the
        /// process boundary, so neither side can gain or lose a field on its own.
        /// </summary>
        [Test]
        public async Task WorkerAndEditorDtos_FieldNames_Match()
        {
            using (ModuleDefinition module = await ReadWorkerModuleAsync())
            {
                List<string> failures = new List<string>();
                foreach (KeyValuePair<Type, string> pair in WorkerTypeNamesByEditorType)
                {
                    TypeDefinition workerType = FindWorkerType(module, pair.Value);
                    HashSet<string> workerNames = new HashSet<string>(
                        ReadWorkerProperties(workerType).Select(property => ToCamelCase(property.Name)),
                        StringComparer.Ordinal);
                    HashSet<string> editorNames = new HashSet<string>(
                        ReadEditorFields(pair.Key).Select(field => field.Name),
                        StringComparer.Ordinal);

                    AppendMissing(failures, pair, "the Editor type", workerNames, editorNames);
                    AppendMissing(failures, pair, "the worker type", editorNames, workerNames);
                }

                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
        }

        /// <summary>
        /// What: every matching field pair describes the same kind of value, so a field whose type
        /// changed on one side alone cannot deserialize into a different shape on the other.
        /// </summary>
        [Test]
        public async Task WorkerAndEditorDtos_FieldTypes_Match()
        {
            using (ModuleDefinition module = await ReadWorkerModuleAsync())
            {
                List<string> failures = new List<string>();
                foreach (KeyValuePair<Type, string> pair in WorkerTypeNamesByEditorType)
                {
                    TypeDefinition workerType = FindWorkerType(module, pair.Value);
                    Dictionary<string, PropertyDefinition> workerProperties =
                        ReadWorkerProperties(workerType)
                            .ToDictionary(property => ToCamelCase(property.Name), StringComparer.Ordinal);

                    foreach (FieldInfo field in ReadEditorFields(pair.Key))
                    {
                        // Names are the other test's subject; a name that is missing here would
                        // otherwise report as a type mismatch and hide which check actually broke.
                        if (!workerProperties.TryGetValue(field.Name, out PropertyDefinition property))
                        {
                            continue;
                        }

                        string workerShape = DescribeWorkerType(property.PropertyType);
                        string editorShape = DescribeEditorType(field.FieldType);
                        if (workerShape == editorShape)
                        {
                            continue;
                        }

                        failures.Add(
                            pair.Key.Name + "." + field.Name + " is " + editorShape
                            + " but " + pair.Value + "." + property.Name + " is " + workerShape + ".");
                    }
                }

                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
        }

        private static void AppendMissing(
            List<string> failures,
            KeyValuePair<Type, string> pair,
            string missingSide,
            HashSet<string> present,
            HashSet<string> compared)
        {
            foreach (string name in present.Except(compared, StringComparer.Ordinal).OrderBy(name => name))
            {
                failures.Add(
                    pair.Key.Name + " / " + pair.Value + ": '" + name + "' is missing from "
                    + missingSide + ".");
            }
        }

        private static async Task<ModuleDefinition> ReadWorkerModuleAsync()
        {
            TransformWorkerBootstrapResult bootstrap =
                await TransformWorkerBootstrap.EnsureWorkerAsync(CancellationToken.None);
            Assert.That(bootstrap.Success, Is.True, "Worker bootstrap failed: " + bootstrap.ErrorMessage);

            string workerDllPath = Path.Combine(
                bootstrap.WorkerDirectory,
                HotReloadConstants.WorkerDllFileName);
            Assert.That(File.Exists(workerDllPath), Is.True, "Worker assembly missing: " + workerDllPath);

            return ModuleDefinition.ReadModule(workerDllPath);
        }

        private static TypeDefinition FindWorkerType(ModuleDefinition module, string workerTypeName)
        {
            TypeDefinition workerType = module.Types.SingleOrDefault(
                type => type.Name == workerTypeName && string.IsNullOrEmpty(type.Namespace));
            Assert.That(workerType, Is.Not.Null, "Worker type not found: " + workerTypeName);
            return workerType;
        }

        // The worker serializes public readable properties; anything else never reaches the wire.
        private static IEnumerable<PropertyDefinition> ReadWorkerProperties(TypeDefinition workerType)
        {
            return workerType.Properties.Where(
                property => property.GetMethod != null && property.GetMethod.IsPublic);
        }

        private static IEnumerable<FieldInfo> ReadEditorFields(Type editorType)
        {
            return editorType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        // The worker serializes with JsonNamingPolicy.CamelCase, which lowercases a whole run of
        // capitals ('URLValue' becomes 'urlValue'), so lowercasing the first character alone only
        // agrees with it while every payload property is a single capitalized word run. The
        // approximation is pinned here rather than assumed: a name that breaks it fails the test
        // instead of being compared against a name neither side ever writes.
        private static string ToCamelCase(string name)
        {
            if (name.Length > 1 && char.IsUpper(name[1]))
            {
                Assert.Fail(
                    "Worker property '" + name + "' starts with more than one capital, which this"
                    + " comparison cannot convert the way the worker's naming policy does. Name a"
                    + " payload property so that only its first character is capitalized.");
            }

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static string DescribeEditorType(Type type)
        {
            if (type.IsArray)
            {
                return DescribeEditorType(type.GetElementType()) + "[]";
            }

            if (type.IsEnum)
            {
                return "enum:" + type.Name;
            }

            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(bool))
            {
                return "bool";
            }

            if (type == typeof(int))
            {
                return "int";
            }

            if (WorkerTypeNamesByEditorType.ContainsKey(type))
            {
                return "dto:" + type.Name;
            }

            throw new InvalidOperationException(
                "Unmapped Editor field type: " + type.FullName
                + ". Add it to the payload map or teach the comparison about it.");
        }

        private static string DescribeWorkerType(TypeReference type)
        {
            if (type is ArrayType arrayType)
            {
                return DescribeWorkerType(arrayType.ElementType) + "[]";
            }

            if (type.FullName == "System.String")
            {
                return "string";
            }

            if (type.FullName == "System.Boolean")
            {
                return "bool";
            }

            if (type.FullName == "System.Int32")
            {
                return "int";
            }

            TypeDefinition resolved = type.Resolve();
            if (resolved != null && resolved.IsEnum)
            {
                return "enum:" + type.Name;
            }

            KeyValuePair<Type, string> mapped = WorkerTypeNamesByEditorType.FirstOrDefault(
                entry => entry.Value == type.Name);
            if (mapped.Key != null)
            {
                return "dto:" + mapped.Key.Name;
            }

            throw new InvalidOperationException(
                "Unmapped worker property type: " + type.FullName
                + ". Add it to the payload map or teach the comparison about it.");
        }
    }
}
