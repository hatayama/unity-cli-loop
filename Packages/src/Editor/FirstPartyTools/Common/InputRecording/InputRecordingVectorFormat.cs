#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Globalization;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats and parses comma-separated vector values stored in recorded input events.
    /// </summary>
    internal static class InputRecordingVectorFormat
    {
        public static string FormatVector2(Vector2 v)
        {
            return v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture);
        }

        public static Vector2 ParseVector2(string data)
        {
            int commaIndex = data.IndexOf(',');
            if (commaIndex < 0)
            {
                return Vector2.zero;
            }

            string xStr = data.Substring(0, commaIndex);
            string yStr = data.Substring(commaIndex + 1);

            if (!float.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                return Vector2.zero;
            }

            if (!float.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return Vector2.zero;
            }

            return new Vector2(x, y);
        }
    }
}
#endif
