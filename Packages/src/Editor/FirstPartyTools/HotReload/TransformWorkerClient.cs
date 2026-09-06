using System;
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
    /// Runs the cached transform worker with file-path JSON I/O (UTF-8, no BOM).
    /// </summary>
    internal static class TransformWorkerClient
    {
        /// <summary>
        /// Bootstraps the worker if needed, writes <paramref name="input"/> to a temp JSON file,
        /// runs <c>dotnet worker.dll &lt;in&gt; &lt;out&gt;</c>, and deserializes the output.
        /// </summary>
        public static async Task<TransformWorkerClientResult> RunAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(input.sources != null, "sources must not be null.");
            Debug.Assert(input.sources.Length > 0, "sources must not be empty.");

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

                if (!File.Exists(outputJsonPath))
                {
                    return TransformWorkerClientResult.Failure(
                        "Transform worker did not produce an output JSON file.");
                }

                string outputJson = File.ReadAllText(
                    outputJsonPath,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                TransformWorkerOutputDto output = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(outputJson);
                if (output == null)
                {
                    return TransformWorkerClientResult.Failure(
                        "Failed to deserialize transform worker output JSON.");
                }

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
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
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

            if (output.files == null)
            {
                errorMessage = "Preparation output must contain files.";
                return false;
            }

            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                if (!TryValidatePreparationFile(file, input, out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidatePreparationFile(
            TransformWorkerFileOutputDto file,
            TransformWorkerInputDto input,
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
