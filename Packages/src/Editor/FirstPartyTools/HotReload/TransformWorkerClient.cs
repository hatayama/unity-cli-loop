using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs transform requests through the resident worker host, falling back to a single one-shot
    /// worker process when the resident conversation cannot be held.
    /// </summary>
    internal static class TransformWorkerClient
    {
        // Why a test seam on a static class: the client has no instance to inject into, and the
        // resident host must be replaceable so routing tests do not depend on the real worker.
        internal static TransformWorkerHost HostOverrideForTests;

        /// <summary>
        /// Transforms <paramref name="input"/> through the resident worker, and only when two fresh
        /// resident processes broke the conversation, once more through a one-shot worker process.
        /// </summary>
        public static async Task<TransformWorkerClientResult> RunAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(input.sources != null, "sources must not be null.");
            Debug.Assert(input.sources.Length > 0, "sources must not be empty.");

            // Why before the worker runs: a record the worker cannot act on does not always fail
            // there. A record missing its owner or its fingerprint still builds a valid mapping,
            // and the run then silently binds the retained type back to its source.
            if (!TryValidateIntroducedTypeArtifacts(input, out string artifactError))
            {
                return TransformWorkerClientResult.Failure(artifactError);
            }

            return await RunWorkerAsync(input, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the worker on a request that has already been checked, without checking it again:
        /// through the resident host, and only when two fresh resident processes broke the
        /// conversation, once more through a one-shot worker process.
        /// </summary>
        // Why separate from RunAsync: the worker consumes the request JSON as a boundary of its
        // own and has to refuse a record it cannot act on even when the request did not come from
        // this client. Its guard can only be shown by handing it a request this client would have
        // refused first.
        internal static async Task<TransformWorkerClientResult> RunWorkerAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            TransformWorkerHost host = HostOverrideForTests ?? TransformWorkerHost.Shared;
            TransformWorkerHostResult hostResult = await host.RunAsync(input, ct).ConfigureAwait(false);
            if (hostResult.Kind == TransformWorkerHostResultKind.Completed)
            {
                // Why the resident output is interpreted here and not inside the host: the host
                // hands back the document as the worker wrote it, and the ordered boundary checks
                // are what turn it into a result. Keeping them on this side is also what stops a
                // replaced host from returning an output that never met them.
                return InterpretOutput(input, hostResult.Output);
            }

            // WorkerFailed, TimedOut, BootstrapFailed and LifecycleClosed describe the request or a
            // deliberate stop, so repeating them on a one-shot process only costs time.
            if (hostResult.Kind != TransformWorkerHostResultKind.RetryExhausted)
            {
                return TransformWorkerClientResult.Failure(hostResult.ErrorMessage);
            }

            // Why fall back only here: two fresh processes broke the conversation without the worker
            // reporting anything, so the resident path itself is suspect and a one-shot process is
            // the only way to still serve this run.
            VibeLogger.LogWarning(
                HotReloadConstants.VibeLogWorkerHostFallbackOneShot,
                "Resident transform worker conversation broke twice; running this request as a one-shot process.",
                new { reason = hostResult.ErrorMessage });
            return await RunOneShotAsync(input, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Bootstraps the worker if needed, writes <paramref name="input"/> to a temp JSON file, runs
        /// <c>dotnet worker.dll &lt;in&gt; &lt;out&gt;</c> once, and deserializes the output.
        /// </summary>
        private static async Task<TransformWorkerClientResult> RunOneShotAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            TransformWorkerBootstrapResult bootstrapResult =
                await TransformWorkerBootstrap.EnsureWorkerAsync(ct).ConfigureAwait(false);
            if (!bootstrapResult.Success)
            {
                return TransformWorkerClientResult.Failure(bootstrapResult.ErrorMessage);
            }

            // ExternalCompilerPathResolver reads EditorApplication.applicationPath.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            if (paths == null)
            {
                return TransformWorkerClientResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-hot-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string inputJsonPath = Path.Combine(tempDirectory, "input.json");
            string outputJsonPath = Path.Combine(tempDirectory, "output.json");

            try
            {
                WriteUtf8NoBom(inputJsonPath, JsonConvert.SerializeObject(input));

                string workerDllPath = Path.Combine(
                    bootstrapResult.WorkerDirectory,
                    HotReloadConstants.WorkerDllFileName);
                string arguments = "\"" + workerDllPath + "\" \"" + inputJsonPath + "\" \"" + outputJsonPath + "\"";
                (int exitCode, string standardOutput, string standardError) = await HotReloadProcessRunner.RunAsync(
                    paths.DotnetHostPath,
                    arguments,
                    bootstrapResult.WorkerDirectory,
                    TimeSpan.FromMilliseconds(HotReloadConstants.WorkerProcessTimeoutMilliseconds),
                    ct).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    return TransformWorkerClientResult.Failure(
                        "Transform worker exited with code " + exitCode
                        + ".\nstdout:\n" + standardOutput
                        + "\nstderr:\n" + standardError);
                }

                TransformWorkerOutputDto output = TransformWorkerOutputReader.TryRead(
                    outputJsonPath,
                    out string readError);
                if (output == null)
                {
                    return TransformWorkerClientResult.Failure(readError);
                }

                return InterpretOutput(input, output);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Rejects retained-artifact records the worker could not act on faithfully: an incomplete
        /// record, a run that names no target assembly to normalize back to, or two records that
        /// claim the same assembly identity or the same type inside it.
        /// </summary>
        internal static bool TryValidateIntroducedTypeArtifacts(
            TransformWorkerInputDto input,
            out string errorMessage)
        {
            if (input.introducedTypeArtifacts == null || input.introducedTypeArtifacts.Length == 0)
            {
                errorMessage = string.Empty;
                return true;
            }

            // The records normalize a retained type back to the assembly its source belongs to,
            // and that is the assembly this run targets.
            if (string.IsNullOrWhiteSpace(input.targetAssemblyName)
                || string.IsNullOrWhiteSpace(input.targetAssemblyMvid))
            {
                errorMessage = "A run that carries retained artifacts must name the target assembly and its module version id.";
                return false;
            }

            HashSet<string> assemblyFullNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> typeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerIntroducedTypeArtifactDto artifact in input.introducedTypeArtifacts)
            {
                if (artifact == null
                    || string.IsNullOrWhiteSpace(artifact.assemblyFullName)
                    || string.IsNullOrWhiteSpace(artifact.referencePath)
                    || artifact.types == null
                    || artifact.types.Length == 0)
                {
                    errorMessage = "A retained artifact must name its assembly, its reference path and at least one type.";
                    return false;
                }

                // Two records claiming one identity would put the same assembly into the
                // compilation twice, and the worker would then resolve one of them to nothing.
                if (!assemblyFullNames.Add(artifact.assemblyFullName))
                {
                    errorMessage = "Two retained artifacts claim the assembly identity " + artifact.assemblyFullName + ".";
                    return false;
                }

                if (!TryValidateIntroducedTypeArtifactTypes(artifact, typeKeys, out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateIntroducedTypeArtifactTypes(
            TransformWorkerIntroducedTypeArtifactDto artifact,
            HashSet<string> typeKeys,
            out string errorMessage)
        {
            foreach (TransformWorkerIntroducedTypeArtifactTypeDto artifactType in artifact.types)
            {
                // Without the owner and the fingerprint the worker cannot tell whether the edited
                // source still produces the declaration the artifact holds, so it would leave the
                // declaration in place and bind the type from source after all.
                if (artifactType == null
                    || string.IsNullOrWhiteSpace(artifactType.metadataName)
                    || string.IsNullOrWhiteSpace(artifactType.originalAssemblyName)
                    || string.IsNullOrWhiteSpace(artifactType.originalAssemblyMvid)
                    || string.IsNullOrWhiteSpace(artifactType.ownerProjectRelativePath)
                    || string.IsNullOrWhiteSpace(artifactType.declarationFingerprint))
                {
                    errorMessage = "A retained type must carry its metadata name, its original identity, its owner and its fingerprint.";
                    return false;
                }

                if (!typeKeys.Add(artifact.assemblyFullName + "|" + artifactType.metadataName))
                {
                    errorMessage = "A retained artifact lists " + artifactType.metadataName + " more than once.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// Turns one worker output JSON document into the client result: deserializes it, then
        /// applies the same ordered checks every path meets.
        /// </summary>
        // Why internal and separate from RunAsync: the order matters as much as the checks, and a
        // test that calls the checks directly cannot tell whether the run still performs them.
        internal static TransformWorkerClientResult InterpretOutputJson(
            TransformWorkerInputDto input,
            string outputJson)
        {
            TransformWorkerOutputDto output =
                TransformWorkerOutputReader.TryDeserialize(outputJson, out string deserializeError);
            if (output == null)
            {
                return TransformWorkerClientResult.Failure(deserializeError);
            }

            return InterpretOutput(input, output);
        }

        /// <summary>
        /// Turns one worker output document into the client result, applying the boundary checks in
        /// the order the process path depends on.
        /// </summary>
        // Why every path meets this and nothing before it: the required-output check has to see the
        // omissions before coalescing replaces them with empty arrays, so the checks live here
        // rather than in whatever read the document.
        internal static TransformWorkerClientResult InterpretOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");

            if (!TryValidateRequiredPreparationOutput(input, output, out string preparationError))
            {
                return TransformWorkerClientResult.Failure(preparationError);
            }

            CoalesceOutput(output);

            // Why fail here: run-level parseErrors describe a failure that belongs to no
            // single source, so there is no per-file row to carry it. Turning it into a
            // client failure at the process boundary is what makes the per-file row count
            // an invariant for every caller downstream.
            if (output.parseErrors.Length > 0)
            {
                return TransformWorkerClientResult.Failure(string.Join("\n", output.parseErrors));
            }

            if (!TryValidateOutput(input, output, out string validationError))
            {
                return TransformWorkerClientResult.Failure(validationError);
            }

            return TransformWorkerClientResult.SuccessResult(output);
        }

        // Why internal: tests must exercise this path instead of re-implementing ??=.
        internal static void CoalesceOutput(TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");

            output.entries ??= Array.Empty<TransformWorkerEntryDto>();
            output.skipped ??= Array.Empty<TransformWorkerSkippedDto>();
            output.files ??= Array.Empty<TransformWorkerFileOutputDto>();
            output.parseErrors ??= Array.Empty<string>();
            output.siblingConstDriftWarnings ??= Array.Empty<string>();
            output.unchangedMethods ??= Array.Empty<TransformWorkerUnchangedMethodDto>();
            output.shimSource ??= string.Empty;
            foreach (TransformWorkerFileOutputDto fileOutput in output.files)
            {
                if (fileOutput == null)
                {
                    continue;
                }

                fileOutput.sourceContentSha256 ??= string.Empty;
                fileOutput.parseErrors ??= Array.Empty<string>();
                fileOutput.declarationDriftWarnings ??= Array.Empty<string>();
                fileOutput.removedMembers ??= Array.Empty<TransformWorkerRemovedMemberDto>();
                fileOutput.removedMethodSignatures ??= Array.Empty<TransformWorkerRemovedMethodSignatureDto>();
                fileOutput.addedFieldNames ??= Array.Empty<string>();
                fileOutput.addedConstNames ??= Array.Empty<string>();
                fileOutput.introducedTypes ??= Array.Empty<TransformWorkerIntroducedTypeDto>();
                fileOutput.introducedTypeDiagnostics ??= Array.Empty<string>();
                fileOutput.introducedTypeReuses ??= Array.Empty<TransformWorkerIntroducedTypeReuseDto>();
            }

            foreach (TransformWorkerEntryDto entry in output.entries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.patchKind ??= string.Empty;
                entry.calledAddedMethodKeys ??= Array.Empty<string>();
                entry.parameterTypeFullNames ??= Array.Empty<string>();
            }
        }

        internal static bool TryValidateOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output,
            out string errorMessage)
        {
            if (!TryValidateRequiredPreparationOutput(input, output, out errorMessage))
            {
                return false;
            }

            if (output.files == null || output.files.Length != input.sources.Length)
            {
                errorMessage = "Transform worker output files must have the same count as input sources.";
                return false;
            }

            for (int index = 0; index < output.files.Length; index++)
            {
                TransformWorkerFileOutputDto file = output.files[index];
                TransformWorkerSourceDto source = input.sources[index];
                if (file == null || file.projectRelativePath != source.projectRelativePath)
                {
                    errorMessage = "Transform worker output files must preserve input source order.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateRequiredPreparationOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output,
            out string errorMessage)
        {
            if (!string.Equals(input.operation, "prepareIntroducedTypes", StringComparison.Ordinal))
            {
                errorMessage = string.Empty;
                return true;
            }

            // The descriptors repeat the requested identity, so a request that carries none would
            // produce descriptors that pass the "matches its input" check with an identity no
            // retained artifact can be attributed to, and fail far from here when the descriptor
            // is constructed.
            if (string.IsNullOrWhiteSpace(input.targetAssemblyName)
                || string.IsNullOrWhiteSpace(input.targetAssemblyMvid))
            {
                errorMessage = "Preparation input must name the target assembly and its module version id.";
                return false;
            }

            if (output.files == null)
            {
                errorMessage = "Preparation output must contain files.";
                return false;
            }

            // Why the keys are built once for the whole output and not per file: a reuse may only
            // name a type a retained artifact of this very run holds, and the same reuse must not
            // be reported twice across the run, which no single file can see on its own.
            HashSet<string> retainedTypeKeys = CollectRetainedTypeKeys(input);
            HashSet<string> reportedReuseKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                if (!TryValidatePreparationFile(
                        file,
                        input,
                        retainedTypeKeys,
                        reportedReuseKeys,
                        out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private static HashSet<string> CollectRetainedTypeKeys(TransformWorkerInputDto input)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            if (input.introducedTypeArtifacts == null)
            {
                return keys;
            }

            foreach (TransformWorkerIntroducedTypeArtifactDto artifact in input.introducedTypeArtifacts)
            {
                if (artifact?.types == null)
                {
                    continue;
                }

                foreach (TransformWorkerIntroducedTypeArtifactTypeDto type in artifact.types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    keys.Add(
                        BuildRetainedTypeKey(
                            type.metadataName,
                            type.originalAssemblyName,
                            type.originalAssemblyMvid));
                }
            }

            return keys;
        }

        private static string BuildRetainedTypeKey(
            string metadataName,
            string originalAssemblyName,
            string originalAssemblyMvid)
        {
            return metadataName + "|" + originalAssemblyName + "|" + originalAssemblyMvid;
        }

        private static bool TryValidatePreparationFile(
            TransformWorkerFileOutputDto file,
            TransformWorkerInputDto input,
            HashSet<string> retainedTypeKeys,
            HashSet<string> reportedReuseKeys,
            out string errorMessage)
        {
            if (file == null || file.introducedTypes == null || file.introducedTypeDiagnostics == null)
            {
                errorMessage = "Preparation output must contain introducedTypes and introducedTypeDiagnostics.";
                return false;
            }

            foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
            {
                if (!TryValidatePreparationDescriptor(introducedType, file, input, out errorMessage))
                {
                    return false;
                }
            }

            // Why omission is refused rather than coalesced: an omitted list reads the same as a
            // run that reused nothing, and a reload would then report a type it bound from an
            // active artifact as introduced by no one.
            if (file.introducedTypeReuses == null)
            {
                errorMessage = "Preparation output must contain introducedTypeReuses.";
                return false;
            }

            foreach (TransformWorkerIntroducedTypeReuseDto reuse in file.introducedTypeReuses)
            {
                if (!TryValidatePreparationReuse(
                        reuse,
                        input,
                        retainedTypeKeys,
                        reportedReuseKeys,
                        out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        // Why a reuse is refused unless a retained artifact of this run holds the type: a reuse row
        // becomes an AlreadyActive row of the response and takes the declaration out of the tree
        // the transform binds against, so a name the run cannot account for would report a binding
        // against an assembly this domain never retained.
        private static bool TryValidatePreparationReuse(
            TransformWorkerIntroducedTypeReuseDto reuse,
            TransformWorkerInputDto input,
            HashSet<string> retainedTypeKeys,
            HashSet<string> reportedReuseKeys,
            out string errorMessage)
        {
            if (reuse == null)
            {
                errorMessage = "Preparation output must not contain a null introduced type reuse.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(reuse.metadataName))
            {
                errorMessage = "Preparation reuse must name the type it bound.";
                return false;
            }

            if (reuse.originalAssemblyName != input.targetAssemblyName
                || reuse.originalAssemblyMvid != input.targetAssemblyMvid)
            {
                errorMessage = "Preparation reuse assembly identity must match its input.";
                return false;
            }

            string key = BuildRetainedTypeKey(
                reuse.metadataName,
                reuse.originalAssemblyName,
                reuse.originalAssemblyMvid);
            if (!retainedTypeKeys.Contains(key))
            {
                errorMessage = "Preparation reuse must name a type a retained artifact of this run holds.";
                return false;
            }

            if (!reportedReuseKeys.Add(key))
            {
                errorMessage = "Preparation output must not report the same reuse more than once.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidatePreparationDescriptor(
            TransformWorkerIntroducedTypeDto introducedType,
            TransformWorkerFileOutputDto file,
            TransformWorkerInputDto input,
            out string errorMessage)
        {
            if (introducedType == null)
            {
                errorMessage = "Preparation output must not contain a null introduced type descriptor.";
                return false;
            }

            if (introducedType.ownerProjectRelativePath == null
                || introducedType.ownerProjectRelativePath != file.projectRelativePath)
            {
                errorMessage = "Preparation descriptor owner must match its file output.";
                return false;
            }

            if (introducedType.originalAssemblyName != input.targetAssemblyName
                || introducedType.originalAssemblyMvid != input.targetAssemblyMvid)
            {
                errorMessage = "Preparation descriptor assembly identity must match its input.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(introducedType.metadataName)
                || string.IsNullOrWhiteSpace(introducedType.declarationFingerprint)
                || string.IsNullOrWhiteSpace(introducedType.source))
            {
                errorMessage = "Preparation descriptor metadataName, declarationFingerprint, and source are required.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static void WriteUtf8NoBom(string path, string contents)
        {
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>
    /// Outcome of one transform-worker invocation.
    /// </summary>
    internal sealed class TransformWorkerClientResult
    {
        public bool Success { get; }
        public TransformWorkerOutputDto Output { get; }
        public string ErrorMessage { get; }

        private TransformWorkerClientResult(bool success, TransformWorkerOutputDto output, string errorMessage)
        {
            Success = success;
            Output = output;
            ErrorMessage = errorMessage;
        }

        public static TransformWorkerClientResult SuccessResult(TransformWorkerOutputDto output)
        {
            return new TransformWorkerClientResult(true, output, string.Empty);
        }

        public static TransformWorkerClientResult Failure(string errorMessage)
        {
            return new TransformWorkerClientResult(false, null, errorMessage);
        }
    }
}
