using System;
using System.Globalization;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A ready-to-write test .asmdef offered when a run discovers no tests and the project has no
    /// test assembly for the requested TestMode.
    /// </summary>
    public sealed class RunTestsTestAsmdefProposal
    {
        public RunTestsTestAsmdefProposal(string assetPath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(content), "content must not be null or empty");

            AssetPath = assetPath;
            Content = content;
        }

        /// <summary>
        /// Project-relative path to save the file at. Test scripts belong under its folder.
        /// </summary>
        public string AssetPath { get; }

        /// <summary>
        /// Complete .asmdef JSON to write verbatim.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// Appends the sentence that points at ProposedTestAsmdef to a no-tests message.
        /// </summary>
        internal static string AppendNotice(string message, RunTestsTestAsmdefProposal proposal)
        {
            Debug.Assert(message != null, "message must not be null");
            Debug.Assert(proposal != null, "proposal must not be null");

            // Why a conditional period: the base NoTestsFound message has no terminator, while the
            // asmdef hints and the predefined-assembly notice that may precede this one end with one.
            string terminator = message.EndsWith(".", StringComparison.Ordinal) ? string.Empty : ".";
            return message
                   + terminator
                   + string.Format(
                       CultureInfo.InvariantCulture,
                       RunTestsConstants.TestAsmdefProposalNoticeFormat,
                       proposal.AssetPath);
        }
    }
}
