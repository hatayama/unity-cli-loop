using System;
using System.IO;
using System.Text;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads and validates a worker output file. Null with a reason when the file is missing,
    /// unreadable as JSON, or does not carry one per-file row per source.
    /// </summary>
    internal static class TransformWorkerOutputReader
    {
        public static TransformWorkerOutputDto TryRead(string outputJsonPath, int expectedFileCount, out string error)
        {
            error = null;
            if (!File.Exists(outputJsonPath))
            {
                error = "worker exited 0 but produced no output JSON file";
                return null;
            }

            string outputJson = File.ReadAllText(outputJsonPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TransformWorkerOutputDto output;
            try
            {
                output = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(outputJson);
            }
            catch (JsonException ex)
            {
                error = "worker output JSON could not be parsed: " + ex.Message;
                return null;
            }

            if (output == null)
            {
                error = "worker output JSON deserialized to null";
                return null;
            }

            TransformWorkerClient.CoalesceOutput(output);
            if (output.parseErrors.Length == 0 && output.files.Length != expectedFileCount)
            {
                error = "worker output carried " + output.files.Length + " file rows for " + expectedFileCount + " sources";
                return null;
            }

            return output;
        }
    }
}
