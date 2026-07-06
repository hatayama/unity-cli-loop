using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides shared SessionState key formatting and primitive storage helpers for editor-session repositories.
    /// </summary>
    internal static class UnityCliLoopEditorSessionStateStorage
    {
        internal const string KeyPrefix = "io.github.hatayama.uloopmcp.editorSession.";

        internal static (bool IsValid, long Value) ParseUtcTicks(string utcTicksText)
        {
            if (string.IsNullOrWhiteSpace(utcTicksText))
            {
                return (true, 0);
            }

            string trimmedText = utcTicksText.Trim();
            long value = 0;
            foreach (char character in trimmedText)
            {
                if (character < '0' || character > '9')
                {
                    return (false, 0);
                }

                int digit = character - '0';
                if (value > (long.MaxValue - digit) / 10)
                {
                    return (false, 0);
                }

                value = value * 10 + digit;
            }

            if (value > DateTime.MaxValue.Ticks)
            {
                return (false, 0);
            }

            return (true, value);
        }

        internal static string[] ParseRequestIdIndex(string requestIdIndex)
        {
            if (string.IsNullOrWhiteSpace(requestIdIndex))
            {
                return Array.Empty<string>();
            }

            string[] rawRequestIds = requestIdIndex.Split(
                new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            List<string> requestIds = new List<string>();
            foreach (string rawRequestId in rawRequestIds)
            {
                string requestId = rawRequestId.Trim();
                if (string.IsNullOrWhiteSpace(requestId) || requestIds.Contains(requestId))
                {
                    continue;
                }

                requestIds.Add(requestId);
            }

            return requestIds.ToArray();
        }

        internal static string AddRequestIdToIndex(string requestIdIndex, string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            List<string> requestIds = new List<string>(ParseRequestIdIndex(requestIdIndex));
            if (!requestIds.Contains(requestId))
            {
                requestIds.Add(requestId);
            }

            return FormatRequestIdIndex(requestIds);
        }

        internal static string RemoveRequestIdFromIndex(string requestIdIndex, string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            List<string> requestIds = new List<string>();
            foreach (string indexedRequestId in ParseRequestIdIndex(requestIdIndex))
            {
                if (indexedRequestId == requestId)
                {
                    continue;
                }

                requestIds.Add(indexedRequestId);
            }

            return FormatRequestIdIndex(requestIds);
        }

        internal static string CreateRequestScopedKey(string requestKeyPrefix, string requestId, string suffix)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestKeyPrefix), "requestKeyPrefix must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(suffix), "suffix must not be null or whitespace");

            return requestKeyPrefix + requestId + suffix;
        }

        internal static bool GetBool(string key)
        {
            return SessionState.GetBool(key, false);
        }

        internal static void SetBool(string key, bool value)
        {
            SessionState.SetBool(key, value);
        }

        internal static string GetString(string key)
        {
            return SessionState.GetString(key, "");
        }

        internal static void SetString(string key, string value)
        {
            SessionState.SetString(key, value ?? "");
        }

        private static string FormatRequestIdIndex(List<string> requestIds)
        {
            Debug.Assert(requestIds != null, "requestIds must not be null");
            return string.Join("\n", requestIds.ToArray());
        }
    }
}
