using Microsoft.CodeAnalysis.CSharp.Syntax;

// What one declaration of an edited file amounts to against the artifact record that claims it:
// the declaration as written, the name the record holds it under, how its fingerprint compares to
// the recorded one, and the record itself. The verdict is reported rather than acted on, because
// the same comparison decides both whether the declaration leaves the binding tree and which of
// its method bodies a reload may patch on the retained assembly.
internal sealed class RetainedDeclarationVerdict
{
    internal RetainedDeclarationVerdict(
        BaseTypeDeclarationSyntax declaration,
        string metadataName,
        IntroducedTypeFingerprintMatch match,
        WorkerIntroducedTypeArtifactType record,
        bool holdsOnlyAppliedChanges)
    {
        Declaration = declaration;
        MetadataName = metadataName;
        Match = match;
        Record = record;
        HoldsOnlyAppliedChanges = holdsOnlyAppliedChanges;
    }

    internal BaseTypeDeclarationSyntax Declaration { get; }

    // The metadata name of the declared type, built from its symbol the way the record's own name
    // was, so a later lookup of the retained assembly asks under the name it was recorded with.
    internal string MetadataName { get; }

    internal IntroducedTypeFingerprintMatch Match { get; }

    // The record the declaration was compared against, which is where the identity of the
    // original assembly the retained one stands in for comes from.
    internal WorkerIntroducedTypeArtifactType Record { get; }

    // True when the source is the one the last reload applied in full: whatever differs from the
    // record is what earlier reloads added or edited, and this reload changes nothing of it.
    internal bool HoldsOnlyAppliedChanges { get; }
}
