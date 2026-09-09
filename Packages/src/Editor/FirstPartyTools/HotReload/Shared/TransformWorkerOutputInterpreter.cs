using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns one transform-worker output document into a client result, applying the boundary
    /// checks in the order the process path depends on.
    /// </summary>
    internal sealed class TransformWorkerOutputInterpreter
    {
        private readonly TransformWorkerOutputValidator _validator;

        internal TransformWorkerOutputInterpreter(TransformWorkerOutputValidator validator)
        {
            _validator = validator;
        }

        /// <summary>
        /// Turns one worker output JSON document into the client result: deserializes it, then
        /// applies the same ordered checks every path meets.
        /// </summary>
        // Why internal and separate from RunAsync: the order matters as much as the checks, and a
        // test that calls the checks directly cannot tell whether the run still performs them.
        internal TransformWorkerClientResult InterpretOutputJson(
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
        internal TransformWorkerClientResult InterpretOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");

            if (!_validator.TryValidateRequiredPreparationOutput(input, output, out string preparationError))
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
        internal void CoalesceOutput(TransformWorkerOutputDto output)
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

        internal bool TryValidateOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output,
            out string errorMessage)
        {
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
    }
}
