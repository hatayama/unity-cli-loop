using System;
using System.Collections.Generic;
using System.Diagnostics;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// How a declaration compares against the fingerprint recorded for the artifact this domain
// already serves it from, in the terms the reload decides on: whether it can keep the artifact,
// patch ordinary method bodies on it, add members to it, or has to ask for a compile.
internal enum IntroducedTypeFingerprintMatchKind
{
    Identical,

    /// <summary>
    /// Only bodies the reload can patch differ: ordinary method bodies, and the body of a property
    /// whose getter is its only accessor with a body. The name predates the second of those, which
    /// the artifact hosts the same way it hosts a method body.
    /// </summary>
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

    private const string RemovedDetailPrefix = "removed:";

    // How IntroducedTypeMemberRegions spells the key of an enum member, which is the one member
    // kind whose edit is known to move the header hash as well.
    private const string EnumMemberKeyPrefix = "enum:";

    private const int MaxNamedOmittedAdditions = 3;

    private const string OrderDetail = "order";

    private const string HeaderDetail = "header";

    private static readonly string[] NoKeys = new string[0];

    private static readonly string[] UnreadableRecordDetails = { "record" };

    private IntroducedTypeFingerprintMatch(
        IntroducedTypeFingerprintMatchKind kind,
        IReadOnlyList<string> changedMethodSyntaxKeys,
        IReadOnlyList<string> changedGetterPropertySyntaxKeys,
        IReadOnlyList<string> changedOtherKeys,
        IReadOnlyList<string> details)
    {
        Kind = kind;
        ChangedMethodSyntaxKeys = changedMethodSyntaxKeys;
        ChangedGetterPropertySyntaxKeys = changedGetterPropertySyntaxKeys;
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

    /// <summary>
    /// Properties whose getter is their only accessor with a body and whose body changed, named by
    /// the syntax property key the emit stages spell a property with. Empty unless the kind is
    /// MethodBodiesOnly or MembersAdded. Held apart from the method keys because the two are
    /// spelled in namespaces of their own.
    /// </summary>
    internal IReadOnlyList<string> ChangedGetterPropertySyntaxKeys { get; }

    /// <summary>Members whose body changed and that the reload cannot patch. Empty unless the kind is OtherBodiesChanged.</summary>
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
            return ReportDeclarationChanged(comparison, recorded, memberIndex);
        }

        // The added-member machinery holds ordinary methods, fields and properties. A constructor,
        // operator, event, indexer, nested type or enum member has to be in the assembly itself.
        foreach (string addedMemberKey in addedMemberKeys)
        {
            if (memberIndex.FindMemberKind(addedMemberKey) == IntroducedTypeMemberKind.Other)
            {
                return ReportDeclarationChanged(comparison, recorded, memberIndex);
            }
        }

        if (orderChanged && !IsOrderExplainedByAdditions(recorded, memberIndex, addedMemberKeys))
        {
            return ReportDeclarationChanged(comparison, recorded, memberIndex);
        }

        SplitChangedBodyKeys(
            comparison,
            memberIndex,
            out List<string> changedMethodSyntaxKeys,
            out List<string> changedGetterPropertySyntaxKeys,
            out List<string> changedOtherKeys);

        // Why a single member of the third group settles it: the reload patches one body at a
        // time, and a constructor or setter body it cannot patch would be left running the
        // artifact's version while its neighbours ran the edited one.
        if (changedOtherKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.OtherBodiesChanged,
                NoKeys,
                NoKeys,
                changedOtherKeys,
                NoKeys);
        }

        if (addedMemberKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.MembersAdded,
                changedMethodSyntaxKeys,
                changedGetterPropertySyntaxKeys,
                NoKeys,
                comparison.Details);
        }

        if (changedMethodSyntaxKeys.Count > 0 || changedGetterPropertySyntaxKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.MethodBodiesOnly,
                changedMethodSyntaxKeys,
                changedGetterPropertySyntaxKeys,
                NoKeys,
                NoKeys);
        }

        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.Identical,
            NoKeys,
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

    // The three groups a changed body falls into: an ordinary method the reload patches, a
    // property whose getter is its only accessor with a body and which it patches the same way,
    // and everything else, which it cannot patch at all.
    private static void SplitChangedBodyKeys(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        IntroducedTypeDeclarationMemberIndex memberIndex,
        out List<string> changedMethodSyntaxKeys,
        out List<string> changedGetterPropertySyntaxKeys,
        out List<string> changedOtherKeys)
    {
        changedMethodSyntaxKeys = new List<string>();
        changedGetterPropertySyntaxKeys = new List<string>();
        changedOtherKeys = new List<string>();
        foreach (string memberKey in comparison.ChangedBodyKeys)
        {
            string syntaxMethodKey = memberIndex.FindSyntaxMethodKey(memberKey);
            if (syntaxMethodKey != null)
            {
                changedMethodSyntaxKeys.Add(syntaxMethodKey);
                continue;
            }

            string syntaxGetterPropertyKey = memberIndex.FindSyntaxGetterPropertyKey(memberKey);
            if (syntaxGetterPropertyKey != null)
            {
                changedGetterPropertySyntaxKeys.Add(syntaxGetterPropertyKey);
                continue;
            }

            changedOtherKeys.Add(memberKey);
        }
    }

    // Why the comparison is filtered rather than reported as it stands: a reader told that an
    // addition this reload already applied is part of why a compile is required takes the addition
    // back out, which is the one edit that cannot help. Only the differences that actually block
    // the reload are named; the applicable additions are counted and named so they are not
    // mistaken for a silent omission either.
    private static IntroducedTypeFingerprintMatch ReportDeclarationChanged(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        HotReloadIntroducedTypeFingerprint recorded,
        IntroducedTypeDeclarationMemberIndex memberIndex)
    {
        // Order is a hash of the recorded key list, so a removal cannot be taken back out of it
        // the way an addition can be taken out of the declaration: whether the members that are
        // left kept their recorded order is not decidable. Naming it anyway would not change what
        // the reader does, because the removal itself already blocks the reload.
        bool hasRemoval = HasDetailWithPrefix(comparison, RemovedDetailPrefix);

        // An enum member that appears or disappears always moves both hashes: the separating comma
        // between members belongs to no member node, so it counts as header, and the key list it
        // joins counts as order. Both are the added or removed member restated, and the member is
        // named already. A base-type change made in the same edit hides behind this, which costs
        // the reader nothing: the addition or removal asks for a compile either way.
        bool hasEnumMemberEdit =
            HasDetailWithPrefix(comparison, AddedDetailPrefix + EnumMemberKeyPrefix)
            || HasDetailWithPrefix(comparison, RemovedDetailPrefix + EnumMemberKeyPrefix);
        bool omitOrder =
            IsOrderExplainedByAllAdditions(comparison, recorded, memberIndex)
            || hasRemoval
            || hasEnumMemberEdit;
        bool omitHeader = hasEnumMemberEdit;
        List<string> blocking = new List<string>();
        List<string> omittedAdditions = new List<string>();
        foreach (string detail in comparison.Details)
        {
            if (detail.StartsWith(AddedDetailPrefix, StringComparison.Ordinal)
                && memberIndex.FindMemberKind(detail.Substring(AddedDetailPrefix.Length))
                    != IntroducedTypeMemberKind.Other)
            {
                omittedAdditions.Add(detail.Substring(AddedDetailPrefix.Length));
                continue;
            }

            if (omitOrder && string.Equals(detail, OrderDetail, StringComparison.Ordinal))
            {
                continue;
            }

            if (omitHeader && string.Equals(detail, HeaderDetail, StringComparison.Ordinal))
            {
                continue;
            }

            blocking.Add(detail);
        }

        if (omittedAdditions.Count > 0)
        {
            blocking.Add(DescribeOmittedAdditions(omittedAdditions));
        }

        Debug.Assert(
            blocking.Count > 0,
            "A declaration reported as changed must name at least one blocking difference.");

        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.DeclarationChanged,
            NoKeys,
            NoKeys,
            NoKeys,
            blocking);
    }

    // Why the names and not the count alone: a reader who cannot tell which additions were left
    // out has to diff the declaration against the artifact by hand to be sure the edit they made
    // is among them. Why a head and a remainder: a declaration can gain many members at once, and
    // the blocking differences this line sits next to are what the reader has to act on.
    private static string DescribeOmittedAdditions(List<string> omittedAdditions)
    {
        int namedCount = Math.Min(omittedAdditions.Count, MaxNamedOmittedAdditions);
        string names = string.Join(", ", omittedAdditions.GetRange(0, namedCount));
        if (namedCount < omittedAdditions.Count)
        {
            names += " and " + (omittedAdditions.Count - namedCount) + " more";
        }

        return omittedAdditions.Count + " applicable addition(s) omitted: " + names;
    }

    private static bool HasDetailWithPrefix(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        string prefix)
    {
        foreach (string detail in comparison.Details)
        {
            if (detail.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Evaluated once over every added key, Other kinds included: an insertion explains the order
    // difference only when dropping all of the added members leaves the recorded order.
    private static bool IsOrderExplainedByAllAdditions(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        HotReloadIntroducedTypeFingerprint recorded,
        IntroducedTypeDeclarationMemberIndex memberIndex)
    {
        List<string> allAddedKeys = new List<string>();
        bool orderChanged = false;
        foreach (string detail in comparison.Details)
        {
            if (detail.StartsWith(AddedDetailPrefix, StringComparison.Ordinal))
            {
                allAddedKeys.Add(detail.Substring(AddedDetailPrefix.Length));
                continue;
            }

            if (string.Equals(detail, OrderDetail, StringComparison.Ordinal))
            {
                orderChanged = true;
            }
        }

        return orderChanged && IsOrderExplainedByAdditions(recorded, memberIndex, allAddedKeys);
    }
}
