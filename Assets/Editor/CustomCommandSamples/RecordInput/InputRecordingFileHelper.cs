#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace io.github.hatayama.uLoopMCP
{
    internal static class InputRecordingFileHelper
    {
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

            string[] files = Directory.GetFiles(outputDir, ReplayInputConstants.JSON_FILE_PATTERN);
            if (files.Length == 0)
            {
                return "";
            }

            return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
        }

        public static HashSet<Key>? ParseKeyFilter(string keys)
        {
            if (string.IsNullOrEmpty(keys))
            {
                return null;
            }

            HashSet<Key> filter = new HashSet<Key>();
            string[] parts = keys.Split(',');

            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (Enum.TryParse<Key>(trimmed, ignoreCase: true, out Key key) && key != Key.None)
                {
                    filter.Add(key);
                }
                else
                {
                    Debug.LogWarning($"[InputRecordingFileHelper] Unknown key name in filter: '{trimmed}'");
                }
            }

            return filter.Count > 0 ? filter : null;
        }
    }
}
