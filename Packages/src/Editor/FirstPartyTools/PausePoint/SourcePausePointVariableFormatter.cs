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
                results.Add(FormatVariable(entry.Name, entry.Scope, entry.Value, maxCollectionPreviewElementCount, ref truncated));
            }

            return (results, truncated);
        }

        private static UloopCapturedVariable FormatVariable(
            string name, string scope, object rawValue, int maxCollectionPreviewElementCount, ref bool truncated)
        {
            if (rawValue == null)
            {
                return new UloopCapturedVariable(name, scope, string.Empty, "null", string.Empty, string.Empty, 0);
            }

            string typeName = rawValue.GetType().FullName;
            if (rawValue is UnityEngine.Object unityObjectCandidate)
            {
                return FormatUnityObjectVariable(name, scope, typeName, unityObjectCandidate);
            }

            if (SourcePausePointCollectionPreviewSerializer.TrySerialize(
                    rawValue, maxCollectionPreviewElementCount, ref truncated, out string collectionPreview))
            {
                string cappedPreview = ApplyValueLengthCap(
                    collectionPreview,
                    SourcePausePointConstants.MaxCollectionPreviewValueLength,
                    ref truncated);
                return new UloopCapturedVariable(name, scope, typeName, cappedPreview, string.Empty, string.Empty, 0);
            }

            string value = ApplyValueLengthCap(
                SafeToString(rawValue), SourcePausePointConstants.MaxCapturedVariableValueLength, ref truncated);
            return new UloopCapturedVariable(name, scope, typeName, value, string.Empty, string.Empty, 0);
        }

        private static UloopCapturedVariable FormatUnityObjectVariable(
            string name, string scope, string typeName, UnityEngine.Object unityObjectCandidate)
        {
            if (!MainThreadSwitcher.IsMainThread)
            {
                return new UloopCapturedVariable(name, scope, typeName, OffMainThreadValue, string.Empty, string.Empty, 0);
            }

            if (unityObjectCandidate == null)
            {
                return new UloopCapturedVariable(
                    name, scope, typeName, DestroyedValue,
                    UloopCapturedVariableUnityObjectKind.Destroyed, string.Empty, UnityObjectIdentifier.GetInstanceId(unityObjectCandidate));
            }

            SourcePausePointUnityObjectClassifier.Classification classification =
                SourcePausePointUnityObjectClassifier.Classify(unityObjectCandidate);
            return new UloopCapturedVariable(
                name, scope, typeName, unityObjectCandidate.name,
                classification.Kind, classification.Path, classification.InstanceId);
        }

        private static string ApplyValueLengthCap(string value, int maxLength, ref bool truncated)
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
