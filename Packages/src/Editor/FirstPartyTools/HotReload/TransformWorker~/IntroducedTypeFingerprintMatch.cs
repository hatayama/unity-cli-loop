using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// How a declaration compares against the fingerprint recorded for the artifact this domain
// already serves it from, in the terms the reload decides on: whether it can keep the artifact,
// patch ordinary method bodies on it, or has to ask for a compile.
internal enum IntroducedTypeFingerprintMatchKind
{
    Identical,
    MethodBodiesOnly,
    OtherBodiesChanged,
    DeclarationChanged,
    RecordUnreadable
}

// The single place that reads a fingerprint comparison as a reload decision. Keeping it here
// rather than at each caller is what stops the prepare and the transform stage from disagreeing
// about whether one edit is a body edit.
internal sealed class IntroducedTypeFingerprintMatch
{
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
    /// MethodBodiesOnly.
    /// </summary>
    internal IReadOnlyList<string> ChangedMethodSyntaxKeys { get; }

    /// <summary>Members that are not ordinary methods whose body changed. Empty unless the kind is OtherBodiesChanged.</summary>
    internal IReadOnlyList<string> ChangedOtherKeys { get; }

    /// <summary>Why the declaration or the record itself did not account for the edit.</summary>
    internal IReadOnlyList<string> Details { get; }

    internal static IntroducedTypeFingerprintMatch Classify(
        string recordedFingerprint,
        string currentFingerprint,
        IReadOnlyDictionary<string, string> syntaxMethodKeysByMemberKey)
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
        if (comparison.Kind == HotReloadIntroducedTypeFingerprintDifference.BodyOnly)
        {
            return ClassifyChangedBodies(comparison, syntaxMethodKeysByMemberKey);
        }

        if (comparison.Kind == HotReloadIntroducedTypeFingerprintDifference.Identical)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.Identical,
                NoKeys,
                NoKeys,
                NoKeys);
        }

        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.DeclarationChanged,
            NoKeys,
            NoKeys,
            comparison.Details);
    }

    /// <summary>
    /// The declaration's ordinary methods, keyed by the name the fingerprint records them under
    /// and valued by the syntax method key the emit stages spell the same method with. The keys
    /// are built along the path the fingerprint builds its own keys on, so the two never disagree
    /// about how a member is named; the values are what a later stage can match a method
    /// declaration against without normalizing it a second way.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> CollectOrdinaryMethodKeys(
        BaseTypeDeclarationSyntax declaration,
        string typeMetadataName)
    {
        Dictionary<string, string> syntaxKeysByMemberKey = new Dictionary<string, string>(StringComparer.Ordinal);

        // Why the values get their type name from the syntax and not from typeMetadataName: the
        // emit stages spell a method with the name the declaration itself carries, and an escaped
        // identifier such as `class @Sample` is written "@Sample" there while the symbol the
        // fingerprint was keyed from reports "Sample". A value built from the symbol's name would
        // never be found by the stage looking the key up, which reads an edited body as unchanged.
        if (!(declaration is TypeDeclarationSyntax typeDeclaration))
        {
            return syntaxKeysByMemberKey;
        }

        string syntaxTypeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
        IReadOnlyList<MemberDeclarationSyntax> members = IntroducedTypeMemberRegions.CollectMembers(declaration);
        for (int index = 0; index < members.Count; index++)
        {
            if (!(members[index] is MethodDeclarationSyntax methodDeclaration))
            {
                continue;
            }

            // Why the trivia is stripped first: a key builder keeps a comment written inside a
            // parameter type, and the fingerprint recorded its keys from a stripped declaration.
            // The same builder on an unstripped one spells the same method differently, which
            // would read an edited body as a member the reload cannot patch.
            MemberDeclarationSyntax member = IntroducedTypeMemberRegions.StripTrivia(methodDeclaration);
            string memberKey = IntroducedTypeMemberRegions.BuildMemberKey(member, typeMetadataName, index);

            // Two methods spelling the same key do not compile, and the fingerprint numbers them
            // instead. Leaving the second one out keeps the first mapping intact, and a changed
            // body of either is then read as a member the reload cannot patch, which is the
            // refusal such a source has to get anyway.
            syntaxKeysByMemberKey.TryAdd(
                memberKey,
                WorkerSyntaxIndex.BuildSyntaxMethodKey(syntaxTypeMetadataName, methodDeclaration));
        }

        return syntaxKeysByMemberKey;
    }

    // Why a single non-method member settles it: the reload patches one body at a time, and a
    // constructor or accessor body it cannot patch would be left running the artifact's version
    // while its neighbours ran the edited one.
    private static IntroducedTypeFingerprintMatch ClassifyChangedBodies(
        HotReloadIntroducedTypeFingerprintComparison comparison,
        IReadOnlyDictionary<string, string> syntaxMethodKeysByMemberKey)
    {
        List<string> methodKeys = new List<string>();
        List<string> otherKeys = new List<string>();
        foreach (string key in comparison.ChangedBodyKeys)
        {
            if (syntaxMethodKeysByMemberKey.TryGetValue(key, out string syntaxMethodKey))
            {
                methodKeys.Add(syntaxMethodKey);
                continue;
            }

            otherKeys.Add(key);
        }

        if (otherKeys.Count > 0)
        {
            return new IntroducedTypeFingerprintMatch(
                IntroducedTypeFingerprintMatchKind.OtherBodiesChanged,
                NoKeys,
                otherKeys,
                NoKeys);
        }

        return new IntroducedTypeFingerprintMatch(
            IntroducedTypeFingerprintMatchKind.MethodBodiesOnly,
            methodKeys,
            NoKeys,
            NoKeys);
    }
}
