using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity and Newtonsoft references.
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How two fingerprints of the same introduced type differ.
    /// </summary>
    internal enum HotReloadIntroducedTypeFingerprintDifference
    {
        Identical,
        BodyOnly,
        DeclarationChanged,
    }

    /// <summary>
    /// One member of an introduced type, split into the part that describes the member to the
    /// outside world and the part that only implements it.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeMemberFingerprint
    {
        internal HotReloadIntroducedTypeMemberFingerprint(string key, string declarationHash, string bodyHash)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key must not be empty.", nameof(key));
            }

            // The canonical form is line based, so a key that carries a line break would make a
            // serialized fingerprint ambiguous to read back.
            if (key.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("key must not contain a line break.", nameof(key));
            }

            if (!HotReloadIntroducedTypeFingerprint.IsCanonicalHash(declarationHash))
            {
                throw new ArgumentException("declarationHash must be 64 lowercase hex digits.", nameof(declarationHash));
            }

            if (bodyHash == null)
            {
                throw new ArgumentException("bodyHash must not be null.", nameof(bodyHash));
            }

            if (bodyHash.Length > 0 && !HotReloadIntroducedTypeFingerprint.IsCanonicalHash(bodyHash))
            {
                throw new ArgumentException("bodyHash must be empty or 64 lowercase hex digits.", nameof(bodyHash));
            }

            Key = key;
            DeclarationHash = declarationHash;
            BodyHash = bodyHash;
        }

        internal string Key { get; }

        internal string DeclarationHash { get; }

        /// <summary>Empty for a member that has no body at all, such as a field or an enum member.</summary>
        internal string BodyHash { get; }
    }

    /// <summary>
    /// The classified difference between two fingerprints, with the keys and reasons behind it.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeFingerprintComparison
    {
        internal HotReloadIntroducedTypeFingerprintComparison(
            HotReloadIntroducedTypeFingerprintDifference kind,
            IReadOnlyList<string> changedBodyKeys,
            IReadOnlyList<string> details)
        {
            Kind = kind;
            ChangedBodyKeys = changedBodyKeys;
            Details = details;
        }

        internal HotReloadIntroducedTypeFingerprintDifference Kind { get; }

        /// <summary>Members whose body hash differs; filled for BodyOnly and DeclarationChanged alike.</summary>
        internal IReadOnlyList<string> ChangedBodyKeys { get; }

        /// <summary>Why the declaration changed: "header", "defines", "order", "added:", "removed:", "declaration:".</summary>
        internal IReadOnlyList<string> Details { get; }
    }

    /// <summary>
    /// The structured fingerprint of one introduced type declaration: what the type declares to
    /// the outside world, which order it declares its members in, and what each member's
    /// declaration and body hash to. Keeping the parts apart is what lets a caller tell a body
    /// edit from a declaration change; folding them back into one string keeps the existing
    /// "same definition or not" decision a single ordinal comparison.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeFingerprint
    {
        private const string FormatVersionLine = "v1";
        private const int HashLength = 64;

        internal HotReloadIntroducedTypeFingerprint(
            string headerHash,
            string definesHash,
            string memberOrderHash,
            IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> members)
        {
            if (!IsCanonicalHash(headerHash))
            {
                throw new ArgumentException("headerHash must be 64 lowercase hex digits.", nameof(headerHash));
            }

            if (!IsCanonicalHash(definesHash))
            {
                throw new ArgumentException("definesHash must be 64 lowercase hex digits.", nameof(definesHash));
            }

            // Required even with no members: the order of an empty member list is still an input
            // that a later declaration can differ from.
            if (!IsCanonicalHash(memberOrderHash))
            {
                throw new ArgumentException("memberOrderHash must be 64 lowercase hex digits.", nameof(memberOrderHash));
            }

            if (members == null)
            {
                throw new ArgumentException("members must not be null.", nameof(members));
            }

            RequireAscendingUniqueKeys(members);

            HeaderHash = headerHash;
            DefinesHash = definesHash;
            MemberOrderHash = memberOrderHash;
            Members = members;
        }

        internal string HeaderHash { get; }

        internal string DefinesHash { get; }

        /// <summary>Hash of the member keys in source order, which sorting Members by key would otherwise drop.</summary>
        internal string MemberOrderHash { get; }

        /// <summary>Members in ascending ordinal key order, each key appearing once.</summary>
        internal IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> Members { get; }

        /// <summary>
        /// Writes the canonical text that travels on the wire. Member keys are length prefixed, so
        /// a key containing the field separator cannot be misread.
        /// </summary>
        internal string Serialize()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(FormatVersionLine).Append('\n');
            builder.Append("h:").Append(HeaderHash).Append('\n');
            builder.Append("d:").Append(DefinesHash).Append('\n');
            builder.Append("o:").Append(MemberOrderHash).Append('\n');
            builder.Append("n:").Append(Members.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int index = 0; index < Members.Count; index++)
            {
                HotReloadIntroducedTypeMemberFingerprint member = Members[index];
                builder.Append("m:");
                builder.Append(member.Key.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':').Append(member.Key);
                builder.Append(':').Append(member.DeclarationHash);
                builder.Append(':').Append(member.BodyHash);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reads back canonical text. Returns false rather than throwing, because fingerprints
        /// recorded by earlier versions and by tests are opaque strings that never parsed.
        /// </summary>
        internal static bool TryParse(string text, out HotReloadIntroducedTypeFingerprint value)
        {
            value = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] lines = text.Split('\n');
            if (lines.Length < 6 || lines[0] != FormatVersionLine)
            {
                return false;
            }

            string headerHash = ReadHashLineOrNull(lines[1], "h:");
            string definesHash = ReadHashLineOrNull(lines[2], "d:");
            string memberOrderHash = ReadHashLineOrNull(lines[3], "o:");
            if (headerHash == null || definesHash == null || memberOrderHash == null)
            {
                return false;
            }

            if (!lines[4].StartsWith("n:", StringComparison.Ordinal))
            {
                return false;
            }

            int memberCount;
            if (!int.TryParse(
                lines[4].Substring(2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out memberCount))
            {
                return false;
            }

            // Five header lines, one line per member, and the empty tail after the final newline.
            if (lines.Length != memberCount + 6 || lines[lines.Length - 1].Length > 0)
            {
                return false;
            }

            List<HotReloadIntroducedTypeMemberFingerprint> members =
                new List<HotReloadIntroducedTypeMemberFingerprint>(memberCount);
            for (int index = 0; index < memberCount; index++)
            {
                HotReloadIntroducedTypeMemberFingerprint member = ReadMemberLineOrNull(lines[index + 5]);
                if (member == null)
                {
                    return false;
                }

                members.Add(member);
            }

            if (!AreKeysAscendingAndUnique(members))
            {
                return false;
            }

            value = new HotReloadIntroducedTypeFingerprint(headerHash, definesHash, memberOrderHash, members);
            return true;
        }

        /// <summary>
        /// Classifies how two fingerprints of the same type differ. Identical here is exactly the
        /// case where both serialize to the same text.
        /// </summary>
        internal static HotReloadIntroducedTypeFingerprintComparison Compare(
            HotReloadIntroducedTypeFingerprint left,
            HotReloadIntroducedTypeFingerprint right)
        {
            if (left == null)
            {
                throw new ArgumentException("left must not be null.", nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentException("right must not be null.", nameof(right));
            }

            List<string> details = new List<string>();
            if (!string.Equals(left.HeaderHash, right.HeaderHash, StringComparison.Ordinal))
            {
                details.Add("header");
            }

            if (!string.Equals(left.DefinesHash, right.DefinesHash, StringComparison.Ordinal))
            {
                details.Add("defines");
            }

            if (!string.Equals(left.MemberOrderHash, right.MemberOrderHash, StringComparison.Ordinal))
            {
                details.Add("order");
            }

            List<string> changedBodyKeys = new List<string>();
            CollectMemberDifferences(left.Members, right.Members, details, changedBodyKeys);

            if (details.Count > 0)
            {
                return new HotReloadIntroducedTypeFingerprintComparison(
                    HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged,
                    changedBodyKeys,
                    details);
            }

            if (changedBodyKeys.Count > 0)
            {
                return new HotReloadIntroducedTypeFingerprintComparison(
                    HotReloadIntroducedTypeFingerprintDifference.BodyOnly,
                    changedBodyKeys,
                    details);
            }

            return new HotReloadIntroducedTypeFingerprintComparison(
                HotReloadIntroducedTypeFingerprintDifference.Identical,
                changedBodyKeys,
                details);
        }

        /// <summary>True when the text is a SHA256 digest in the canonical 64 lowercase hex form.</summary>
        internal static bool IsCanonicalHash(string text)
        {
            if (text == null || text.Length != HashLength)
            {
                return false;
            }

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isLowerHexLetter = character >= 'a' && character <= 'f';
                if (!isDigit && !isLowerHexLetter)
                {
                    return false;
                }
            }

            return true;
        }

        // Walks both member lists once. Both are in ascending key order, so a key that falls behind
        // on one side is missing from the other.
        private static void CollectMemberDifferences(
            IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> left,
            IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> right,
            List<string> details,
            List<string> changedBodyKeys)
        {
            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Count || rightIndex < right.Count)
            {
                if (rightIndex >= right.Count)
                {
                    details.Add("removed:" + left[leftIndex].Key);
                    leftIndex++;
                    continue;
                }

                if (leftIndex >= left.Count)
                {
                    details.Add("added:" + right[rightIndex].Key);
                    rightIndex++;
                    continue;
                }

                HotReloadIntroducedTypeMemberFingerprint leftMember = left[leftIndex];
                HotReloadIntroducedTypeMemberFingerprint rightMember = right[rightIndex];
                int order = string.CompareOrdinal(leftMember.Key, rightMember.Key);
                if (order < 0)
                {
                    details.Add("removed:" + leftMember.Key);
                    leftIndex++;
                    continue;
                }

                if (order > 0)
                {
                    details.Add("added:" + rightMember.Key);
                    rightIndex++;
                    continue;
                }

                if (!string.Equals(leftMember.DeclarationHash, rightMember.DeclarationHash, StringComparison.Ordinal))
                {
                    details.Add("declaration:" + leftMember.Key);
                }

                if (!string.Equals(leftMember.BodyHash, rightMember.BodyHash, StringComparison.Ordinal))
                {
                    changedBodyKeys.Add(leftMember.Key);
                }

                leftIndex++;
                rightIndex++;
            }
        }

        private static void RequireAscendingUniqueKeys(IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> members)
        {
            for (int index = 1; index < members.Count; index++)
            {
                int order = string.CompareOrdinal(members[index - 1].Key, members[index].Key);
                if (order == 0)
                {
                    throw new ArgumentException("members must not repeat a key.", nameof(members));
                }

                if (order > 0)
                {
                    throw new ArgumentException("members must be sorted by ordinal key.", nameof(members));
                }
            }
        }

        private static bool AreKeysAscendingAndUnique(IReadOnlyList<HotReloadIntroducedTypeMemberFingerprint> members)
        {
            for (int index = 1; index < members.Count; index++)
            {
                if (string.CompareOrdinal(members[index - 1].Key, members[index].Key) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ReadHashLineOrNull(string line, string prefix)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            string hash = line.Substring(prefix.Length);
            return IsCanonicalHash(hash) ? hash : null;
        }

        private static HotReloadIntroducedTypeMemberFingerprint ReadMemberLineOrNull(string line)
        {
            if (!line.StartsWith("m:", StringComparison.Ordinal))
            {
                return null;
            }

            int keyLengthEnd = line.IndexOf(':', 2);
            if (keyLengthEnd < 0)
            {
                return null;
            }

            int keyLength;
            if (!int.TryParse(
                line.Substring(2, keyLengthEnd - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out keyLength))
            {
                return null;
            }

            int keyStart = keyLengthEnd + 1;
            int declarationStart = keyStart + keyLength + 1;
            if (keyLength == 0 || declarationStart + HashLength > line.Length)
            {
                return null;
            }

            if (line[declarationStart - 1] != ':')
            {
                return null;
            }

            string key = line.Substring(keyStart, keyLength);
            string declarationHash = line.Substring(declarationStart, HashLength);
            int bodyStart = declarationStart + HashLength;
            if (bodyStart >= line.Length || line[bodyStart] != ':')
            {
                return null;
            }

            string bodyHash = line.Substring(bodyStart + 1);
            if (!IsCanonicalHash(declarationHash))
            {
                return null;
            }

            if (bodyHash.Length > 0 && !IsCanonicalHash(bodyHash))
            {
                return null;
            }

            return new HotReloadIntroducedTypeMemberFingerprint(key, declarationHash, bodyHash);
        }
    }
}
