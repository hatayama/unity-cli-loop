using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the English sentence for a reason the worker reported. This is the only place a
    /// worker reason is worded: the worker sends a code and the values the sentence needs, so a
    /// wording change never has to be mirrored across the process boundary.
    /// </summary>
    internal static class HotReloadWorkerReasonText
    {
        private static readonly Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> Templates =
            BuildTemplates();

        /// <summary>
        /// The sentence for this reason, with its values substituted and its detail appended.
        /// </summary>
        internal static string Render(TransformWorkerReasonDto reason)
        {
            if (reason == null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            ReasonTemplate template = Templates[reason.code];
            string[] args = reason.args ?? Array.Empty<string>();
            if (args.Length != template.PlaceholderCount)
            {
                throw new ArgumentException(
                    "Reason " + reason.code + " takes " + template.PlaceholderCount
                    + " value(s) but carries " + args.Length + ".",
                    nameof(reason));
            }

            string text = template.Text;
            for (int index = 0; index < args.Length; index++)
            {
                // Why not string.Format: some sentences quote C# source containing braces, which
                // a format string would read as a placeholder and reject.
                text = text.Replace("{" + index + "}", args[index] ?? string.Empty);
            }

            if (reason.detail == null)
            {
                if (template.RequiresDetail)
                {
                    throw new ArgumentException(
                        "Reason " + reason.code + " is only reported with a detail.",
                        nameof(reason));
                }

                return text;
            }

            if (!template.AllowsDetail)
            {
                throw new ArgumentException(
                    "Reason " + reason.code + " has no place for a detail.",
                    nameof(reason));
            }

            return text + template.DetailSeparator + Render(reason.detail) + template.DetailSuffix;
        }

        private static Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> BuildTemplates()
        {
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates =
                new Dictionary<HotReloadWorkerReasonCode, ReasonTemplate>();

            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                Plain("Could not resolve a declared type symbol.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                Plain("Generic introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypePartial,
                Plain("Partial introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeRecord,
                Plain("Record introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNonPublic,
                Plain("Non-public introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeRefLike,
                Plain("Ref-like introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnsafe,
                Plain("Unsafe introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnityObject,
                Plain("Unity object introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeSerializable,
                Plain("Serializable introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeModuleInitializer,
                Plain("Module initializer introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnsupported,
                Plain("Unsupported introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeConstValueUnverifiable,
                Plain("Const value cannot be verified: {0} referenced by {1}", 2));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeConstChanged,
                Plain("Changed const requires a compile: {0} referenced by {1}", 2));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeDelegate,
                Plain("Delegate introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNested,
                Plain("Nested type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNestedDeclaration,
                Plain("Nested declaration inside an introduced type requires a compile: {0}/{1}", 2));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                Plain("Changed introduced type requires a compile: {0}", 1));

            // The one sentence whose value is written by the worker rather than here: the same
            // artifact error is also returned as a fatal transform message, so it stays a single
            // sentence owned by the artifact map instead of being split into codes twice.
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable,
                Plain("Introduced types require a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable,
                Plain(
                    "Introduced types require a compile: the target assembly or its references could not be read.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch,
                Plain(
                    "Introduced types require a compile: the target assembly identity does not match the request.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EditorIsolatedAddedMethodCaller,
                Plain(HotReloadConstants.IsolatedAddedMethodCallerSkipReason, 0));

            return templates;
        }

        private static ReasonTemplate Plain(string text, int placeholderCount)
        {
            return new ReasonTemplate(text, placeholderCount, false, false, string.Empty, string.Empty);
        }

        /// <summary>
        /// One reason's sentence and the shape of reason it words.
        /// </summary>
        private sealed class ReasonTemplate
        {
            internal ReasonTemplate(
                string text,
                int placeholderCount,
                bool allowsDetail,
                bool requiresDetail,
                string detailSeparator,
                string detailSuffix)
            {
                Text = text;
                PlaceholderCount = placeholderCount;
                AllowsDetail = allowsDetail;
                RequiresDetail = requiresDetail;
                DetailSeparator = detailSeparator;
                DetailSuffix = detailSuffix;
            }

            internal string Text { get; }

            internal int PlaceholderCount { get; }

            internal bool AllowsDetail { get; }

            internal bool RequiresDetail { get; }

            internal string DetailSeparator { get; }

            internal string DetailSuffix { get; }
        }
    }
}
