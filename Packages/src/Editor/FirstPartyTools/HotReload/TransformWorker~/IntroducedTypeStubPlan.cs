using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;

// The bodies one introduced declaration's artifact stubs: the fingerprint keys its record marks as
// stubbed, the method keys the transform has to patch before the artifact may be activated, and
// the changes that replace each body in the declaration's text.
internal sealed class IntroducedTypeStubPlan
{
    internal static readonly IntroducedTypeStubPlan None = new IntroducedTypeStubPlan(
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<TextChange>());

    internal IntroducedTypeStubPlan(
        IReadOnlyList<string> fingerprintKeys,
        IReadOnlyList<string> methodKeys,
        IReadOnlyList<TextChange> bodyChanges)
    {
        FingerprintKeys = fingerprintKeys;
        MethodKeys = methodKeys;
        BodyChanges = bodyChanges;
    }

    internal IReadOnlyList<string> FingerprintKeys { get; }

    /// <summary>Keys in the form the transform's entries are keyed with, one per stubbed member.</summary>
    internal IReadOnlyList<string> MethodKeys { get; }

    /// <summary>Changes at positions of the tree that holds the declaration.</summary>
    internal IReadOnlyList<TextChange> BodyChanges { get; }
}
