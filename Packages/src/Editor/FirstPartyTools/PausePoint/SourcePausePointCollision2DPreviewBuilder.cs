using Newtonsoft.Json.Linq;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds a Collision2D capture preview that exposes collider hierarchy paths via properties.
    /// Why: raw internal fields are instance IDs (and their type changes across Unity versions —
    /// int on 2022.3, EntityId later), so they cannot identify either colliding object without an
    /// extra execute-dynamic-code round-trip; property access yields the live Collider2D handles.
    /// </summary>
    internal static class SourcePausePointCollision2DPreviewBuilder
    {
        private const string NoneColliderPreview = "(none)";

        /// <summary>
        /// Tries to build a Collision2D preview token from a captured value.
        /// Why: Classify uses AssetDatabase and must run on the main thread; off-thread callers
        /// fall back to the generic field-token path instead.
        /// </summary>
        public static bool TryBuildToken(object value, out JToken token)
        {
            token = null;
            if (value is not Collision2D collision)
            {
                return false;
            }

            if (!MainThreadSwitcher.IsMainThread)
            {
                return false;
            }

            token = BuildPreviewToken(
                collision.collider,
                collision.otherCollider,
                collision.relativeVelocity,
                collision.contactCount);
            return true;
        }

        /// <summary>
        /// Builds the Collision2D preview JObject from already-extracted property values.
        /// Why: EditMode tests cannot construct a real Collision2D (physics simulation only), so
        /// the preview shape is unit-tested through this builder rather than TryBuildToken.
        /// </summary>
        internal static JToken BuildPreviewToken(
            Collider2D collider, Collider2D otherCollider, Vector2 relativeVelocity, int contactCount)
        {
            return new JObject
            {
                ["Collider"] = FormatCollider(collider),
                ["OtherCollider"] = FormatCollider(otherCollider),
                ["RelativeVelocity"] = relativeVelocity.ToString(),
                ["ContactCount"] = contactCount
            };
        }

        private static JToken FormatCollider(Collider2D collider)
        {
            // Why: Unity's overloaded == covers destroyed/"fake null" references that a plain
            // ReferenceEquals check would miss.
            if (collider == null)
            {
                return new JValue(NoneColliderPreview);
            }

            return new JObject
            {
                ["Name"] = collider.name,
                ["UnityObjectPath"] = SourcePausePointUnityObjectClassifier.Classify(collider).Path
            };
        }
    }
}
