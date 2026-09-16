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

        /// <summary>
        /// What: a member key length that would overflow the line offsets is refused instead of throwing.
        /// </summary>
        [Test]
        public void TryParse_MemberLineWithOverflowingKeyLength_ReturnsFalse()
        {
            string text = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash
                + "\nn:1\nm:2147483647:k:" + DeclarationHash + ":\n";

            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(text, out _), Is.False);
        }

        /// <summary>
        /// What: a member count written with a leading zero is refused, because it would not serialize back the same way.
        /// </summary>
        [Test]
        public void TryParse_MemberCountWithLeadingZero_ReturnsFalse()
        {
            string text = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash + "\nn:01\n"
                + "m:" + MethodKey.Length + ":" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n";

            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(text, out _), Is.False);
        }

        /// <summary>
        /// What: a member key length written with a leading zero is refused for the same round-trip reason.
        /// </summary>
        [Test]
        public void TryParse_MemberKeyLengthWithLeadingZero_ReturnsFalse()
        {
            string text = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash
                + "\nn:1\nm:08:" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n";

            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(text, out _), Is.False);
        }

        /// <summary>
        /// What: text whose member keys repeat or descend is refused and yields no value.
        /// </summary>
        [Test]
        public void TryParse_MemberKeysRepeatedOrDescending_ReturnsFalseWithNoValue()
        {
            string repeated = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash + "\nn:2\n"
                + "m:" + MethodKey.Length + ":" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n"
                + "m:" + MethodKey.Length + ":" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n";
            string descending = "v1\nh:" + HeaderHash + "\nd:" + DefinesHash + "\no:" + OrderHash + "\nn:2\n"
                + "m:" + MethodKey.Length + ":" + MethodKey + ":" + DeclarationHash + ":" + BodyHash + "\n"
                + "m:" + FieldKey.Length + ":" + FieldKey + ":" + DeclarationHash + ":\n";

            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(repeated, out HotReloadIntroducedTypeFingerprint fromRepeated), Is.False);
            Assert.That(fromRepeated, Is.Null);
            Assert.That(HotReloadIntroducedTypeFingerprint.TryParse(descending, out HotReloadIntroducedTypeFingerprint fromDescending), Is.False);
            Assert.That(fromDescending, Is.Null);
        }

        /// <summary>
        /// What: the fingerprint keeps its own copy of the member list, so a later edit of the caller's list cannot break its invariants.
        /// </summary>
        [Test]
        public void Constructor_CallerMutatesTheListAfterwards_FingerprintIsUnaffected()
        {
            List<HotReloadIntroducedTypeMemberFingerprint> members =
                new List<HotReloadIntroducedTypeMemberFingerprint> { CreateMember(MethodKey, DeclarationHash, BodyHash) };
            HotReloadIntroducedTypeFingerprint fingerprint = new HotReloadIntroducedTypeFingerprint(
                HeaderHash,
                DefinesHash,
                OrderHash,
                members);
            string serializedBefore = fingerprint.Serialize();

            members.Add(CreateMember(MethodKey, OtherHash, BodyHash));

            Assert.That(fingerprint.Members.Count, Is.EqualTo(1));
            Assert.That(fingerprint.Serialize(), Is.EqualTo(serializedBefore));
        }

        /// <summary>
        /// What: a null member is refused at construction rather than surfacing later as a null reference.
        /// </summary>
        [Test]
        public void Constructor_NullMemberInTheList_Throws()
        {
            Assert.Throws<ArgumentException>(() => CreateFingerprint((HotReloadIntroducedTypeMemberFingerprint)null));
            Assert.Throws<ArgumentException>(() => CreateFingerprint(
                null,
                CreateMember(MethodKey, DeclarationHash, BodyHash)));
            Assert.Throws<ArgumentException>(() => CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                null));
        }

        /// <summary>
        /// What: a 64 character value that is not lowercase hex is not accepted as a hash.
        /// </summary>
        [Test]
        public void Constructor_HashOfTheRightLengthButNotLowercaseHex_Throws()
        {
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeFingerprint(
                new string('A', 64),
                DefinesHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint>()));
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeFingerprint(
                new string('z', 64),
                DefinesHash,
                OrderHash,
                new List<HotReloadIntroducedTypeMemberFingerprint>()));
        }

        /// <summary>
        /// What: a member appended at the end of the newer fingerprint is reported as an addition.
        /// </summary>
        [Test]
        public void Compare_MemberAppendedAtTheEnd_ReturnsAddedDetail()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(FieldKey, DeclarationHash, string.Empty));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, BodyHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "added:" + MethodKey }));
        }

        /// <summary>
        /// What: comparing a fingerprint that has members against one that has none lists every member as removed.
        /// </summary>
        [Test]
        public void Compare_RightHasNoMembers_ReportsEveryMemberAsRemoved()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, string.Empty),
                CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint();

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(
                comparison.Details,
                Is.EqualTo(new List<string> { "removed:" + FieldKey, "removed:" + MethodKey }));
        }

        /// <summary>
        /// What: a member whose declaration and body both changed is reported in both the details and the changed bodies.
        /// </summary>
        [Test]
        public void Compare_DeclarationAndBodyBothChanged_ReportsTheKeyInBothLists()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(CreateMember(MethodKey, OtherHash, OtherHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(comparison.Details, Is.EqualTo(new List<string> { "declaration:" + MethodKey }));
            Assert.That(comparison.ChangedBodyKeys, Is.EqualTo(new List<string> { MethodKey }));
        }

        /// <summary>
        /// What: several members changing only their bodies are all listed, and the difference stays body only.
        /// </summary>
        [Test]
        public void Compare_SeveralBodiesChanged_ListsEveryChangedBodyKey()
        {
            HotReloadIntroducedTypeFingerprint left = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, BodyHash),
                CreateMember(MethodKey, DeclarationHash, BodyHash));
            HotReloadIntroducedTypeFingerprint right = CreateFingerprint(
                CreateMember(FieldKey, DeclarationHash, OtherHash),
                CreateMember(MethodKey, DeclarationHash, OtherHash));

            HotReloadIntroducedTypeFingerprintComparison comparison = HotReloadIntroducedTypeFingerprint.Compare(left, right);

            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.BodyOnly));
            Assert.That(comparison.ChangedBodyKeys, Is.EqualTo(new List<string> { FieldKey, MethodKey }));
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
