using System;
using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// How a declaration compares against the fingerprint recorded for the artifact this domain
// already serves it from, in the terms the reload decides on: whether it can keep the artifact,
// patch ordinary method bodies on it, add members to it, or has to ask for a compile.
internal enum IntroducedTypeFingerprintMatchKind
{
    Identical,
    MethodBodiesOnly,
    MembersAdded,
    OtherBodiesChanged,
    DeclarationChanged,
    RecordUnreadable
}

// The single place that reads a fingerprint comparison as a reload decision. Keeping it here
// rather than at each caller is what stops the prepare and the transform stage from disagreeing
// about whether one edit is a body edit.
internal sealed class IntroducedTypeFingerprintMatch
{
    private const string AddedDetailPrefix = "added:";

    private const string OrderDetail = "order";

    private static readonly string[] NoKeys = new string[0];

    private static readonly string[] UnreadableRecordDetails = { "record" };

    private IntroducedTypeFingerprintMatch(
        IntroducedTypeFingerprintMatchKind kind,
        IReadOnlyList<string> changedMethodSyntaxKeys,
        IReadOnlyList<string> changedOtherKeys,
        IReadOnlyList<string> details)
    {
        Kind = kind;
        ChangedMethodSyntaxKeys = changedMethodSyntaxKeys;
        ChangedOtherKeys = changedOtherKeys;
        Details = details;
    }

    internal IntroducedTypeFingerprintMatchKind Kind { get; }

    /// <summary>
    /// Ordinary methods whose body changed, named by the syntax method key the emit stages spell a
    /// method with rather than by the key the fingerprint recorded. Empty unless the kind is
    /// MethodBodiesOnly or MembersAdded.
    /// </summary>
    internal IReadOnlyList<string> ChangedMethodSyntaxKeys { get; }

    /// <summary>Members that are not ordinary methods whose body changed. Empty unless the kind is OtherBodiesChanged.</summary>
    internal IReadOnlyList<string> ChangedOtherKeys { get; }

    /// <summary>Why the declaration or the record itself did not account for the edit.</summary>
    internal IReadOnlyList<string> Details { get; }

    internal static IntroducedTypeFingerprintMatch Classify(
        string recordedFingerprint,
        string currentFingerprint,
        IntroducedTypeDeclarationMemberIndex memberIndex)
    {
        // Why the text comparison first: it is the decision this stage made before it could tell
        // one difference from another, so an unchanged declaration never depends on the parser.
        if (string.Equals(recordedFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.Identical,
                NoKeys,
                NoKeys,
                NoKeys);
        }

        // Why a record this version cannot read is its own kind: an opaque string would otherwise
        // be reported as a declaration that differs in every part, which says nothing about the
        // source the reader is looking at.
        if (!HotReloadIntroducedTypeFingerprint.TryParse(
                recordedFingerprint,
                out HotReloadIntroducedTypeFingerprint recorded)
            || !HotReloadIntroducedTypeFingerprint.TryParse(
                currentFingerprint,
                out HotReloadIntroducedTypeFingerprint current))
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.RecordUnreadable,
                NoKeys,
                NoKeys,
                UnreadableRecordDetails);
        }

        HotReloadIntroducedTypeFingerprintComparison comparison =
            HotReloadIntroducedTypeFingerprint.Compare(recorded, current);

        // Why every difference is checked before any of them is allowed: a reload that keeps the
        // artifact has to account for all of them, so one difference it cannot apply settles the
        // decision no matter what the others say.
        List<string> addedMemberKeys = new List<string>();
        bool orderChanged = false;
        foreach (string detail in comparison.Details)
        {
            if (detail.StartsWith(AddedDetailPrefix, StringComparison.Ordinal))
            {
                addedMemberKeys.Add(detail.Substring(AddedDetailPrefix.Length));
                continue;
            }

            if (string.Equals(detail, OrderDetail, StringComparison.Ordinal))
            {
                orderChanged = true;
                continue;
            }

            // A removal, a changed signature, a changed header or a changed define set: the
            // artifact no longer describes the declaration, and no added member explains it.
            return ReportDeclarationChanged(comparison);
        }

        // The added-member machinery holds ordinary methods, fields and properties. A constructor,
        // operator, event, indexer, nested type or enum member has to be in the assembly itself.
        foreach (string addedMemberKey in addedMemberKeys)
        {
            if (memberIndex.FindMemberKind(addedMemberKey) == IntroducedTypeMemberKind.Other)
            {
                return ReportDeclarationChanged(comparison);
            }
        }

        if (orderChanged && !IsOrderExplainedByAdditions(recorded, memberIndex, addedMemberKeys))
        {
            return ReportDeclarationChanged(comparison);
        }

        SplitChangedBodyKeys(
            comparison,
            memberIndex,
            out List<string> changedMethodSyntaxKeys,
            out List<string> changedOtherKeys);

        // Why a single non-method member settles it: the reload patches one body at a time, and a
        // constructor or accessor body it cannot patch would be left running the artifact's version
        // while its neighbours ran the edited one.
        if (changedOtherKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.OtherBodiesChanged,
                NoKeys,
                changedOtherKeys,
                NoKeys);
        }

        if (addedMemberKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.MembersAdded,
                changedMethodSyntaxKeys,
                NoKeys,
                comparison.Details);
        }

        if (changedMethodSyntaxKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.MethodBodiesOnly,
                changedMethodSyntaxKeys,
                NoKeys,
                NoKeys);
        }

        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.Identical,
            NoKeys,
            NoKeys,
            NoKeys);
    }

    // The order difference an insertion leaves behind is not a reordering: dropping the added keys
    // from the declared order has to leave exactly the order the record holds. Anything else moved
    // a member the artifact already serves, and the declared order decides implicit enum values
    // and the sequence field initializers run in.
    private static bool IsOrderExplainedByAdditions(
        HotReloadIntroducedTypeFingerprint recorded,
        IntroducedTypeDeclarationMemberIndex memberIndex,
        IReadOnlyList<string> addedMemberKeys)
    {
        if (addedMemberKeys.Count == 0)
        {
            return false;
        }

        HashSet<string> added = new HashSet<string>(addedMemberKeys, StringComparer.Ordinal);
        List<string> remaining = new List<string>();
        foreach (string memberKey in memberIndex.OrderedKeys)
        {
            if (added.Contains(memberKey))
            {
                continue;
            }

            remaining.Add(memberKey);
        }

        return string.Equals(
            IntroducedTypeFingerprint.ComputeMemberOrderHash(remaining),
            recorded.MemberOrderHash,
            StringComparison.Ordinal);
    }

    private static void SplitChangedBodyKeys(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        IntroducedTypeDeclarationMemberIndex memberIndex,
        out List<string> changedMethodSyntaxKeys,
        out List<string> changedOtherKeys)
    {
        changedMethodSyntaxKeys = new List<string>();
        changedOtherKeys = new List<string>();
        foreach (string memberKey in comparison.ChangedBodyKeys)
        {
            string syntaxMethodKey = memberIndex.FindSyntaxMethodKey(memberKey);
            if (syntaxMethodKey != null)
            {
                changedMethodSyntaxKeys.Add(syntaxMethodKey);
                continue;
            }

            changedOtherKeys.Add(memberKey);
        }
    }

    private static IntroducedTypeFingerprintMatch ReportDeclarationChanged(
        HotReloadIntroducedTypeFingerprintComparison comparison)
    {
        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.DeclarationChanged,
            NoKeys,
            NoKeys,
            comparison.Details);
    }
}
