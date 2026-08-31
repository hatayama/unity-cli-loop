using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns the shared capture frame into DTOs a CLI response can serialize.
    /// </summary>
    internal static class SourcePausePointVariableFormatter
    {
        private const string OffMainThreadValue = "(captured off main thread)";
        private const string DestroyedValue = "(destroyed)";

        public static (List<UloopCapturedVariable> Variables, bool Truncated) Format(
            object instance, object[] parameterNamesAndValues, object[] localNamesAndValues,
            int maxCollectionPreviewElementCount = SourcePausePointConstants.MaxCollectionPreviewElementCount)
        {
            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                instance, parameterNamesAndValues, localNamesAndValues);
            return FormatFrame(frame, maxCollectionPreviewElementCount);
        }

        public static (List<UloopCapturedVariable> Variables, bool Truncated) FormatFrame(
            UloopPausePointCapturedVariableFrame frame,
            int maxCollectionPreviewElementCount = SourcePausePointConstants.MaxCollectionPreviewElementCount)
        {
            Debug.Assert(frame != null, "frame must not be null");

            List<UloopCapturedVariable> results = new();
            bool truncated = frame.Truncated;
            foreach (UloopPausePointCapturedVariableEntry entry in frame.Entries)
            {
                UloopCapturedVariable variable = FormatVariable(
                    entry.Name, entry.Scope, entry.Value, maxCollectionPreviewElementCount);
                results.Add(variable);
                truncated |= variable.Truncated;
            }

            return (results, truncated);
        }

        private static UloopCapturedVariable FormatVariable(
            string name, string scope, object rawValue, int maxCollectionPreviewElementCount)
        {
            if (rawValue == null)
            {
                return new UloopCapturedVariable(
                    name, scope, string.Empty, "null", string.Empty, string.Empty, 0, truncated: false);
            }

            string typeName = rawValue.GetType().FullName;
            if (rawValue is UnityEngine.Object unityObjectCandidate)
            {
                return FormatUnityObjectVariable(name, scope, typeName, unityObjectCandidate);
            }

            // Why a per-variable flag: overall CapturedVariablesTruncated alone cannot tell which
            // value was clipped after a name filter narrows the list.
            bool variableTruncated = false;
            if (SourcePausePointCollectionPreviewSerializer.TrySerialize(
                    rawValue, maxCollectionPreviewElementCount, ref variableTruncated, out string collectionPreview))
            {
                // Why scale: a per-marker element-count override that raises the element cap
                // without also raising the byte budget would still get clipped by the fixed
                // default-sized value-length cap before all the requested elements fit (the
                // motivating Round-8 case: a 200-element bool board needs ~1200 chars, well
                // past the 1024-char default). Scaling keeps the ~102-chars-per-element budget
                // the default (10 elements, 1024 chars) already implies.
                int scaledValueLengthCap = SourcePausePointConstants.MaxCollectionPreviewValueLength
                    * maxCollectionPreviewElementCount / UloopPausePointRegistry.DefaultMaxPreviewElements;
                string cappedPreview = ApplyValueLengthCap(collectionPreview, scaledValueLengthCap, ref variableTruncated);
                return new UloopCapturedVariable(
                    name, scope, typeName, cappedPreview, string.Empty, string.Empty, 0, variableTruncated);
            }

            string value = ApplyValueLengthCap(
                SafeToString(rawValue), SourcePausePointConstants.MaxCapturedVariableValueLength, ref variableTruncated);
            return new UloopCapturedVariable(
                name, scope, typeName, value, string.Empty, string.Empty, 0, variableTruncated);
        }

        private static UloopCapturedVariable FormatUnityObjectVariable(
            string name, string scope, string typeName, UnityEngine.Object unityObjectCandidate)
        {
            if (!MainThreadSwitcher.IsMainThread)
            {
                return new UloopCapturedVariable(
                    name, scope, typeName, OffMainThreadValue, string.Empty, string.Empty, 0, truncated: false);
            }

            if (unityObjectCandidate == null)
            {
                return new UloopCapturedVariable(
                    name, scope, typeName, DestroyedValue,
                    UloopCapturedVariableUnityObjectKind.Destroyed, string.Empty,
                    UnityObjectIdentifier.GetInstanceId(unityObjectCandidate), truncated: false);
            }

            SourcePausePointUnityObjectClassifier.Classification classification =
                SourcePausePointUnityObjectClassifier.Classify(unityObjectCandidate);
            return new UloopCapturedVariable(
                name, scope, typeName, unityObjectCandidate.name,
                classification.Kind, classification.Path, classification.InstanceId, truncated: false);
        }

        // Why internal: Watch reuses this cap so get-watch-values previews match CapturedVariables
        // length limits instead of duplicating the clipping rule.
        internal static string ApplyValueLengthCap(string value, int maxLength, ref bool truncated)
        {
            if (value.Length <= maxLength)
            {
                return value;
            }

            truncated = true;
            return value.Substring(0, maxLength);
        }

        // Sanctioned try-catch sites in the capture path: user ToString() overrides (below) and
        // materialized-collection enumeration in SourcePausePointCollectionPreviewSerializer.
        // Both must not let untrusted user code crash a pause-point hit.
        private static string SafeToString(object value)
        {
            try
            {
                return value.ToString();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return $"(toString threw {exception.GetType().Name})";
            }
        }
    }
}
