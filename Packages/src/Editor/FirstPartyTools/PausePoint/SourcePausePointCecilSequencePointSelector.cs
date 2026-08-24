using System.Collections.Generic;
using System.Diagnostics;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies monotonic sequence-point selection to one Cecil method's PDB points.
    /// </summary>
    internal static class SourcePausePointCecilSequencePointSelector
    {
        public static SequencePoint SelectInMethod(
            MethodDefinition method,
            string normalizedFilePath,
            int requestedLine,
            int sourceEndLine)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedFilePath), "normalizedFilePath must not be empty.");
            Debug.Assert(requestedLine > 0, "requestedLine must be a positive 1-based line.");

            if (!method.HasBody)
            {
                return null;
            }

            MethodDebugInformation debugInformation = method.DebugInformation;
            if (debugInformation == null || !debugInformation.HasSequencePoints)
            {
                return null;
            }

            List<SequencePoint> filePoints = new List<SequencePoint>();
            List<SourcePausePointSequencePointCandidate> candidates =
                new List<SourcePausePointSequencePointCandidate>();
            foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
            {
                if (!SourcePausePointPathNormalizer.PathsReferToSameFile(
                        sequencePoint.Document.Url,
                        normalizedFilePath))
                {
                    continue;
                }

                filePoints.Add(sequencePoint);
                candidates.Add(
                    new SourcePausePointSequencePointCandidate(
                        sequencePoint.StartLine,
                        sequencePoint.Offset,
                        sequencePoint.IsHidden));
            }

            int selectedIndex = SourcePausePointSequencePointSelector.SelectIndex(
                candidates,
                requestedLine,
                sourceEndLine);
            if (selectedIndex < 0)
            {
                return null;
            }

            return filePoints[selectedIndex];
        }
    }
}
