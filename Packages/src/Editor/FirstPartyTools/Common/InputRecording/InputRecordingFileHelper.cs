#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides helper operations for Input Recording File behavior.
    /// </summary>
    internal static class InputRecordingFileHelper
    {
        private const string JSON_FILE_PATTERN = "*.json";

        private static readonly JsonSerializerSettings WRITE_SETTINGS = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented
        };

        private static readonly JsonSerializerSettings READ_SETTINGS = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public static void Save(InputRecordingData data, string outputPath)
        {
            Debug.Assert(data != null, "data must not be null");

            string? directoryPath = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string json = JsonConvert.SerializeObject(data, WRITE_SETTINGS);
            File.WriteAllText(outputPath, json);
        }

        public static InputRecordingData? Load(string path)
        {
            Debug.Assert(File.Exists(path), $"Recording file must exist: {path}");

            string json = File.ReadAllText(path);
            InputRecordingData? data = JsonConvert.DeserializeObject<InputRecordingData>(json, READ_SETTINGS);

            if (data?.Frames == null)
            {
                return null;
            }

            return data;
        }

        public static string ResolveOutputPath(string outputPath)
        {
            if (!string.IsNullOrEmpty(outputPath))
            {
                return outputPath;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string fileName = $"{RecordInputConstants.RECORDING_FILE_PREFIX}{timestamp}.json";
            return Path.Combine(RecordInputConstants.DEFAULT_OUTPUT_DIR, fileName);
        }

        public static string ResolveLatestRecording(string inputPath)
        {
            if (!string.IsNullOrEmpty(inputPath))
            {
                return inputPath;
            }

            string outputDir = RecordInputConstants.DEFAULT_OUTPUT_DIR;
            if (!Directory.Exists(outputDir))
            {
                return "";
            }

            string[] files = Directory.GetFiles(outputDir, JSON_FILE_PATTERN);
            if (files.Length == 0)
            {
                return "";
            }

            return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
        }

        /// <summary>
        /// Parses the comma-separated key filter into the keys to record. Entries that name no key
        /// are reported rather than skipped: dropping them silently would record a different set of
        /// keys than the caller asked for, and dropping all of them would record every key.
        /// </summary>
        public static KeyFilterParseResult ParseKeyFilter(string keys)
        {
            if (string.IsNullOrEmpty(keys))
            {
                return new KeyFilterParseResult(null, Array.Empty<string>());
            }

            HashSet<Key> filter = new();
            List<string> invalidKeyNames = new();
            string[] parts = keys.Split(',');

            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                (bool resolved, Key key) = KeyNameResolver.Resolve(trimmed);
                if (!resolved)
                {
                    invalidKeyNames.Add(trimmed);
                    continue;
                }

                filter.Add(key);
            }

            if (filter.Count == 0 && invalidKeyNames.Count == 0)
            {
                // Every entry was empty (for example "," or " "), so the filter would fall back to
                // recording every key while the response looked like no filter was ever given.
                // Why not reject the empty entries themselves: a trailing comma in "W," is harmless
                // once at least one entry names a key.
                invalidKeyNames.Add(keys);
            }

            return new KeyFilterParseResult(filter.Count > 0 ? filter : null, invalidKeyNames);
        }
    }
}
#endif
