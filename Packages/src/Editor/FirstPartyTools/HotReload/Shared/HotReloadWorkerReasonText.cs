using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the English sentence for a reason the worker reported. This is the only place a
    /// worker reason is worded: the worker sends a code and the values the sentence needs, so a
    /// wording change never has to be mirrored across the process boundary.
    /// </summary>
    internal static partial class HotReloadWorkerReasonText
    {
        // The next steps a sentence can end with, and the phrase that introduces a rejected
        // accessor rewrite. A template names its next step through EndingWith instead of
        // concatenating it, so a next-step wording change never touches the sentence bodies.
        private const string CompileCallToAction = "Run 'uloop compile'.";

        // Why each names what the compile adds instead of "it" or "them": a sentence that names a
        // rewrite before its call to action left readers unsure whether "it" was the member or
        // the rewrite.
        private const string CompileCallToActionToAddTheMethod = "Run 'uloop compile' to add the method.";

        private const string CompileCallToActionToAddTheField = "Run 'uloop compile' to add the field.";

        private const string CompileCallToActionToAddTheProperty = "Run 'uloop compile' to add the property.";

        // For a sentence that already names a rewrite that needs no compile: the compile is only
        // for keeping the code the way it is written, whether or not the rewrite fits.
        private const string CompileCallToActionToKeepTheCode =
            "Run 'uloop compile' to keep the code as written.";

        // For a row whose detail is a carried-in skip of the property's accessor: that accessor's
        // own Skipped row carries the step the Editor chose from the run, and a compile call here
        // would contradict it when that step needs no compile.
        private const string AccessorRowNamesTheStep =
            "The Skipped row for the property's accessor names the step to take.";

        private const string AccessorRewriteUnavailableSeparator = " Accessor rewrite unavailable: ";

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

            // The next step belongs to the sentence itself, so it comes before any detail: a
            // detail explains the refusal, and the next step still reads as the sentence's end.
            string nextStep = template.NextStepFor(reason.detail);
            if (nextStep.Length > 0)
            {
                text = text + " " + nextStep;
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

            AddMethodTransformTemplates(templates);
            AddAddedMemberTemplates(templates);
            AddAccessorTemplates(templates);
            AddIntroducedTypeTemplates(templates);

            return templates;
        }

        private static ReasonTemplate Plain(string text, int placeholderCount)
        {
            return new ReasonTemplate(
                text, placeholderCount, false, false, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        // A reason that reads on its own but appends a detail when it has one.
        private static ReasonTemplate Composing(
            string text,
            int placeholderCount,
            string detailSeparator,
            string detailSuffix)
        {
            return new ReasonTemplate(
                text, placeholderCount, true, false, detailSeparator, detailSuffix, string.Empty, string.Empty);
        }

        // A reason that is incomplete without its detail.
        private static ReasonTemplate RequiringDetail(
            string text,
            int placeholderCount,
            string detailSeparator,
            string detailSuffix)
        {
            return new ReasonTemplate(
                text, placeholderCount, true, true, detailSeparator, detailSuffix, string.Empty, string.Empty);
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
                string detailSuffix,
                string nextStep,
                string carriedInDetailNextStep)
            {
                Text = text;
                PlaceholderCount = placeholderCount;
                AllowsDetail = allowsDetail;
                RequiresDetail = requiresDetail;
                DetailSeparator = detailSeparator;
                DetailSuffix = detailSuffix;
                NextStep = nextStep;
                CarriedInDetailNextStep = carriedInDetailNextStep;
            }

            internal string Text { get; }

            internal int PlaceholderCount { get; }

            internal bool AllowsDetail { get; }

            internal bool RequiresDetail { get; }

            internal string DetailSeparator { get; }

            internal string DetailSuffix { get; }

            // The call to action that closes the sentence, placed before any detail; empty when
            // the sentence names its own next step or needs none.
            internal string NextStep { get; }

            // Replaces NextStep when the detail is a skip whose step the Editor chooses from the
            // run; empty when the template has no such replacement.
            internal string CarriedInDetailNextStep { get; }

            /// <summary>
            /// The next step that closes the sentence for this detail.
            /// </summary>
            internal string NextStepFor(TransformWorkerReasonDto detail)
            {
                if (detail == null || CarriedInDetailNextStep.Length == 0)
                {
                    return NextStep;
                }

                bool carriedIn = detail.code == HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature
                    || detail.code == HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType;
                return carriedIn ? CarriedInDetailNextStep : NextStep;
            }

            /// <summary>
            /// This template with the sentence closed by the given next step.
            /// </summary>
            internal ReasonTemplate EndingWith(string nextStep)
            {
                if (string.IsNullOrEmpty(nextStep))
                {
                    throw new ArgumentException("A next step must not be empty.", nameof(nextStep));
                }

                return new ReasonTemplate(
                    Text,
                    PlaceholderCount,
                    AllowsDetail,
                    RequiresDetail,
                    DetailSeparator,
                    DetailSuffix,
                    nextStep,
                    CarriedInDetailNextStep);
            }

            /// <summary>
            /// This template with the next step used instead when the detail is a skip whose step
            /// the Editor chooses from the run.
            /// </summary>
            internal ReasonTemplate EndingWithWhenDetailIsCarriedIn(string nextStep)
            {
                if (string.IsNullOrEmpty(nextStep))
                {
                    throw new ArgumentException("A next step must not be empty.", nameof(nextStep));
                }

                return new ReasonTemplate(
                    Text,
                    PlaceholderCount,
                    AllowsDetail,
                    RequiresDetail,
                    DetailSeparator,
                    DetailSuffix,
                    NextStep,
                    nextStep);
            }
        }
    }
}
