using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A worker reason as the facts it was reported with, kept beside the sentence built from it
    /// so a later step can compare reasons or choose a next step without parsing that sentence.
    /// </summary>
    internal sealed class HotReloadWorkerReasonFacts
    {
        public HotReloadWorkerReasonCode Code { get; }

        // The values the sentence substitutes. A null value is kept as an empty string, because
        // the sentence renders both the same way.
        public IReadOnlyList<string> Args { get; }

        // Project-relative forward-slash paths of the files declaring the types the reason names.
        // Empty when the reason names none or none could be placed.
        public IReadOnlyList<string> DeclaringFiles { get; }

        // Null when the reason does not compose.
        public HotReloadWorkerReasonFacts Detail { get; }

        private HotReloadWorkerReasonFacts(
            HotReloadWorkerReasonCode code,
            IReadOnlyList<string> args,
            IReadOnlyList<string> declaringFiles,
            HotReloadWorkerReasonFacts detail)
        {
            Code = code;
            Args = args;
            DeclaringFiles = declaringFiles;
            Detail = detail;
        }

        public static HotReloadWorkerReasonFacts From(TransformWorkerReasonDto dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new HotReloadWorkerReasonFacts(
                dto.code,
                CopyArgs(dto.args),
                new List<string>(dto.declaringFiles ?? Array.Empty<string>()),
                dto.detail == null ? null : From(dto.detail));
        }

        /// <summary>
        /// Whether both reasons render to the same sentence: the same code, the same values in
        /// order, and matching details. The declaring files are not compared, because the values
        /// already name the types they were resolved from.
        /// </summary>
        public bool Matches(HotReloadWorkerReasonFacts other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (Code != other.Code || !ArgsEqual(Args, other.Args))
            {
                return false;
            }

            if (Detail == null || other.Detail == null)
            {
                return Detail == null && other.Detail == null;
            }

            return Detail.Matches(other.Detail);
        }

        private static List<string> CopyArgs(string[] args)
        {
            List<string> copied = new List<string>();
            foreach (string arg in args ?? Array.Empty<string>())
            {
                copied.Add(arg ?? string.Empty);
            }

            return copied;
        }

        private static bool ArgsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
