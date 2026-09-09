using System;
using System.IO;
using System.Text;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads a worker output file as the worker wrote it. Null with a reason when the file is
    /// missing, unreadable, or not deserializable JSON.
    /// </summary>
    /// <remarks>
    /// Why nothing else is checked here: the document's content is judged by
    /// TransformWorkerOutputInterpreter.InterpretOutput, which needs the omissions intact and in one order.
    /// Coalescing or counting rows here would either hide them or duplicate that order.
    /// </remarks>
    internal static class TransformWorkerOutputReader
    {
        public static TransformWorkerOutputDto TryRead(string outputJsonPath, out string error)
        {
            error = null;
            // Why Directory.Exists too: a directory sitting at the output path is not a missing file,
            // and reporting it as one would hide a real path collision behind the "no output" reason.
            // Letting it fall through turns it into the read failure it actually is.
            if (!File.Exists(outputJsonPath) && !Directory.Exists(outputJsonPath))
            {
                error = "worker exited 0 but produced no output JSON file";
                return null;
            }

            string outputJson;
            try
            {
                outputJson = File.ReadAllText(outputJsonPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (IOException ex)
            {
                error = "worker output JSON could not be read: " + ex.Message;
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Why here and not at the call sites: without this the exception escapes both the
                // host and the one-shot client, which have no other place to turn it into a result.
                error = "worker output JSON could not be read: " + ex.Message;
                return null;
            }

            return TryDeserialize(outputJson, out error);
        }

        /// <summary>
        /// Deserializes one worker output document. Null with a reason when the text is not JSON
        /// the worker output shape can be read from.
        /// </summary>
        // Why shared instead of a second JsonConvert call site: the process path and the callers
        // that hand in the document directly must fail on the same text for the same reason.
        public static TransformWorkerOutputDto TryDeserialize(string outputJson, out string error)
        {
            error = null;
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

            return output;
        }
    }
}
