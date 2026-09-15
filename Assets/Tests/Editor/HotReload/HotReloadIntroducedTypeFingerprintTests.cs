using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadIntroducedTypeFingerprint"/>: the canonical wire
    /// form it serializes to, what it refuses to parse, and how it classifies the difference
    /// between two fingerprints of the same introduced type.
    /// </summary>
    public class HotReloadIntroducedTypeFingerprintTests
    {
        private const string HeaderHash = "1111111111111111111111111111111111111111111111111111111111111111";
        private const string DefinesHash = "2222222222222222222222222222222222222222222222222222222222222222";
        private const string OrderHash = "3333333333333333333333333333333333333333333333333333333333333333";
        private const string DeclarationHash = "4444444444444444444444444444444444444444444444444444444444444444";
        private const string BodyHash = "5555555555555555555555555555555555555555555555555555555555555555";
        private const string OtherHash = "6666666666666666666666666666666666666666666666666666666666666666";

        private const string MethodKey = "type:M()";
        private const string FieldKey = "field:value";

        /// <summary>
        /// What: a fingerprint survives a Serialize / TryParse round trip byte for byte.
        /// </summary>
        [Test]
        public void Serialize_AfterTryParseOfItsOwnText_ProducesTheSameText()
        {
            HotReloadIntroducedTypeFingerprint original = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, BodyHash));
            string text = original.Serialize();

            bool parsed = HotReloadIntroducedTypeFingerprint.TryParse(text, out HotReloadIntroducedTypeFingerprint value);

            Assert.That(parsed, Is.True);
            Assert.That(value.Serialize(), Is.EqualTo(text));
        }

        /// <summary>
        /// What: the opaque fingerprint strings that existing callers and tests use are rejected without throwing.
        /// </summary>
        [Test]
        public void TryParse_OpaqueNonCanonicalText_ReturnsFalseWithoutThrowing()
        {
            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse("original", out _), Is.False);
            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(string.Empty, out _), Is.False);
            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(null, out _), Is.False);
            Assert.That(
                HotReloadIntroducedTypeFingerprint.TryParse(new string('0', 64), out _),
                Is.False);
        }

        /// <summary>
        /// What: a member line whose declared key length does not match the payload is rejected.
        /// </summary>
        [Test]
        public void TryParse_MemberLineWithWrongKeyLength_ReturnsFalse()
        {
            string text = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash
                + "\nn:1\nm:99:" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n";

            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(text, out _), Is.False);
        }

        /// <summary>
        /// What: two fingerprints built from the same inputs compare as Identical.
        /// </summary>
        [Test]
        public void Compare_EqualFingerprints_ReturnsIdentical()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.Identical));
            Assert.That(comparison.ChangedBodyKeys, Is.Empty);
            Assert.That(comparison.Details, Is.Empty);
            Assert.That(left.Serialize(), Is.EqualTo(right.Serialize()));
        }

        /// <summary>
        /// What: a difference confined to one member body is reported as BodyOnly with that member key.
        /// </summary>
        [Test]
        public void Compare_OnlyBodyHashDiffers_ReturnsBodyOnlyWithChangedKey()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, OtherHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.BodyOnly));
            Assert.That(comparison.ChangedBodyKeys, Is.EqualTo(new List<string> { MethodKey }));
            Assert.That(comparison.Details, Is.Empty);
        }

        /// <summary>
        /// What: a difference in a member declaration is reported as DeclarationChanged against that key.
        /// </summary>
        [Test]
        public void Compare_MemberDeclarationHashDiffers_ReturnsDeclarationChanged()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(CreateMember(MethodKey, OtherHash, BodyHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "declaration:" + MethodKey }));
            Assert.That(comparison.ChangedBodyKeys, Is.Empty);
        }

        /// <summary>
        /// What: a header difference is reported as DeclarationChanged with the header detail.
        /// </summary>
        [Test]
        public void Compare_HeaderHashDiffers_ReturnsDeclarationChangedWithHeaderDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = new HotReloadIntroducedTypeFingerprint(
                OtherHash,
                DefinesHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint> { CreateMember(MethodKey, DeclarationHash, BodyHash) });

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "header" }));
        }

        /// <summary>
        /// What: a defines difference is reported as DeclarationChanged with the defines detail.
        /// </summary>
        [Test]
        public void Compare_DefinesHashDiffers_ReturnsDeclarationChangedWithDefinesDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = new HotReloadIntroducedTypeFingerprint(
                HeaderHash,
                OtherHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint> { CreateMember(MethodKey, DeclarationHash, BodyHash) });

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "defines" }));
        }

        /// <summary>
        /// What: members reordered in source keep their hashes but change the member-order hash, which reads as DeclarationChanged.
        /// </summary>
        [Test]
        public void Compare_MemberOrderHashDiffers_ReturnsDeclarationChangedWithOrderDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = new HotReloadIntroducedTypeFingerprint(
                HeaderHash,
                DefinesHash,
                OtherHash,
                new List<HotReloadIntroducedTypeMemberFingerprint> { CreateMember(MethodKey, DeclarationHash, BodyHash) });

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "order" }));
            Assert.That(left.Serialize(), Is.Not.EqualTo(right.Serialize()));
        }

        /// <summary>
        /// What: a member present only in the newer fingerprint is reported as an addition.
        /// </summary>
        [Test]
        public void Compare_MemberAddedOnTheRight_ReturnsDeclarationChangedWithAddedDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, BodyHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "added:" + FieldKey }));
        }

        /// <summary>
        /// What: a member present only in the older fingerprint is reported as a removal.
        /// </summary>
        [Test]
        public void Compare_MemberRemovedOnTheRight_ReturnsDeclarationChangedWithRemovedDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "removed:" + FieldKey }));
        }

        /// <summary>
        /// What: a body change that arrives together with a new member still lists the changed body key.
        /// </summary>
        [Test]
        public void Compare_BodyChangedAndMemberAdded_ReportsBothInOneComparison()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, OtherHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "added:" + FieldKey }));
            Assert.That(comparison.ChangedBodyKeys, Is.EqualTo(new List<string> { MethodKey }));
        }

        /// <summary>
        /// What: two members sharing a key break the identity the comparison walk relies on and are refused.
        /// </summary>
        [Test]
        public void Constructor_DuplicateMemberKeys_Throws()
        {
            Assert.Throws<ArgumentException>(() => CreateFingerprint(
                CreateMember(MethodKey, DeclarationHash, BodyHash),
                CreateMember(MethodKey, OtherHash, BodyHash)));
        }

        /// <summary>
        /// What: members handed over out of key order are refused, because the merge walk assumes ascending keys.
        /// </summary>
        [Test]
        public void Constructor_MembersNotSortedByKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => CreateFingerprint(
                CreateMember(MethodKey, DeclarationHash, BodyHash),
                CreateMember(FieldKey, DeclarationHash, string.Empty)));
        }

        /// <summary>
        /// What: a hash that is not 64 lowercase hex characters is refused.
        /// </summary>
        [Test]
        public void Constructor_MalformedHash_Throws()
        {
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeFingerprint(
                "not-a-hash",
                DefinesHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint>()));
            Assert.Throws<ArgumentException>(() => CreateMember(MethodKey, DeclarationHash, "ABCDEF"));
        }

        /// <summary>
        /// What: a type with no members still carries a member-order hash and round-trips.
        /// </summary>
        [Test]
        public void Serialize_FingerprintWithoutMembers_RoundTrips()
        {
            HotReloadIntroducedTypeFingerprint original = CreateFingerprint();

            bool parsed = HotReloadIntroducedTypeFingerprint.TryParse(
                original.Serialize(),
                out HotReloadIntroducedTypeFingerprint value);

            Assert.That(parsed, Is.True);
            Assert.That(value.Members, Is.Empty);
            Assert.That(value.Serialize(), Is.EqualTo(original.Serialize()));
        }

        private static HotReloadIntroducedTypeFingerprint CreateFingerprint(
            params HotReloadIntroducedTypeMemberFingerprint[] members)
        {
            return new HotReloadIntroducedTypeFingerprint(
                HeaderHash,
                DefinesHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint>(members));
        }

        private static HotReloadIntroducedTypeMemberFingerprint CreateMember(
            string key,
            string declarationHash,
            string bodyHash)
        {
            return new HotReloadIntroducedTypeMemberFingerprint(key, declarationHash, bodyHash);
        }
    }
}
