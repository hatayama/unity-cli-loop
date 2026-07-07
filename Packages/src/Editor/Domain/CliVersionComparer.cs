using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    // Compares npm-style CLI versions for platform compatibility checks.
    /// <summary>
    /// Provides CLI Version Comparer behavior for Unity CLI Loop.
    /// </summary>
    public static class CliVersionComparer
    {
        public static bool IsVersionLessThan(string leftVersion, string rightVersion)
        {
            return TryCompareCliVersions(leftVersion, rightVersion, out int comparison) && comparison < 0;
        }

        public static bool IsVersionGreaterThan(string leftVersion, string rightVersion)
        {
            return TryCompareCliVersions(leftVersion, rightVersion, out int comparison) && comparison > 0;
        }

        public static bool IsVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return TryCompareCliVersions(leftVersion, rightVersion, out int comparison) && comparison >= 0;
        }

        public static bool IsVersionEqual(string leftVersion, string rightVersion)
        {
            return TryCompareCliVersions(leftVersion, rightVersion, out int comparison) && comparison == 0;
        }

        internal static bool TryCompareCliVersions(
            string leftVersion,
            string rightVersion,
            out int comparison)
        {
            comparison = 0;

            bool leftParsed = TryParseCliVersion(leftVersion, out ParsedCliVersion left);
            bool rightParsed = TryParseCliVersion(rightVersion, out ParsedCliVersion right);
            if (!leftParsed || !rightParsed)
            {
                return false;
            }

            comparison = CompareParsedCliVersions(left, right);
            return true;
        }

        private static bool TryParseCliVersion(string version, out ParsedCliVersion parsedVersion)
        {
            parsedVersion = default;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            string normalized = TrimVersionPrefix(version.Trim());
            string[] buildParts = normalized.Split(new[] { '+' }, 2);
            string versionWithoutBuildMetadata = buildParts[0];
            int prereleaseSeparatorIndex = versionWithoutBuildMetadata.IndexOf('-');
            string coreVersion = prereleaseSeparatorIndex >= 0
                ? versionWithoutBuildMetadata.Substring(0, prereleaseSeparatorIndex)
                : versionWithoutBuildMetadata;
            string[] coreParts = coreVersion.Split('.');
            if (coreParts.Length != 3)
            {
                return false;
            }

            (bool hasMajor, int major) = ParseVersionPart(coreParts[0]);
            (bool hasMinor, int minor) = ParseVersionPart(coreParts[1]);
            (bool hasPatch, int patch) = ParseVersionPart(coreParts[2]);
            if (!hasMajor || !hasMinor || !hasPatch)
            {
                return false;
            }

            string[] prereleaseIdentifiers = Array.Empty<string>();
            if (prereleaseSeparatorIndex >= 0)
            {
                string prerelease = versionWithoutBuildMetadata.Substring(prereleaseSeparatorIndex + 1);
                if (!IsValidPrerelease(prerelease))
                {
                    return false;
                }

                prereleaseIdentifiers = prerelease.Split('.');
            }

            parsedVersion = new ParsedCliVersion(major, minor, patch, prereleaseIdentifiers);
            return true;
        }

        private static string TrimVersionPrefix(string version)
        {
            if (version.StartsWith("v", StringComparison.Ordinal) ||
                version.StartsWith("V", StringComparison.Ordinal))
            {
                return version.Substring(1);
            }

            return version;
        }

        private static (bool IsParsed, int Parsed) ParseVersionPart(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return (false, 0);
            }

            if (HasLeadingZero(value))
            {
                return (false, 0);
            }

            if (!ContainsOnlyDigits(value))
            {
                return (false, 0);
            }

            int parsed = 0;
            foreach (char character in value)
            {
                int digit = character - '0';
                if (parsed > (int.MaxValue - digit) / 10)
                {
                    return (false, 0);
                }

                parsed = (parsed * 10) + digit;
            }

            return (true, parsed);
        }

        private static bool IsValidPrerelease(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] identifiers = value.Split('.');
            foreach (string identifier in identifiers)
            {
                if (string.IsNullOrEmpty(identifier))
                {
                    return false;
                }

                if (!ContainsOnlyPrereleaseCharacters(identifier))
                {
                    return false;
                }

                if (ContainsOnlyDigits(identifier) && HasLeadingZero(identifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasLeadingZero(string value)
        {
            return value.Length > 1 && value.StartsWith("0", StringComparison.Ordinal);
        }

        private static bool ContainsOnlyDigits(string value)
        {
            foreach (char character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsOnlyPrereleaseCharacters(string value)
        {
            foreach (char character in value)
            {
                if (character >= '0' && character <= '9')
                {
                    continue;
                }

                if (character >= 'A' && character <= 'Z')
                {
                    continue;
                }

                if (character >= 'a' && character <= 'z')
                {
                    continue;
                }

                if (character == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static int CompareParsedCliVersions(ParsedCliVersion left, ParsedCliVersion right)
        {
            int majorComparison = left.Major.CompareTo(right.Major);
            if (majorComparison != 0)
            {
                return majorComparison;
            }

            int minorComparison = left.Minor.CompareTo(right.Minor);
            if (minorComparison != 0)
            {
                return minorComparison;
            }

            int patchComparison = left.Patch.CompareTo(right.Patch);
            if (patchComparison != 0)
            {
                return patchComparison;
            }

            return ComparePrereleaseIdentifierLists(left.PrereleaseIdentifiers, right.PrereleaseIdentifiers);
        }

        private static int ComparePrereleaseIdentifierLists(string[] leftIdentifiers, string[] rightIdentifiers)
        {
            bool leftIsRelease = leftIdentifiers.Length == 0;
            bool rightIsRelease = rightIdentifiers.Length == 0;
            if (leftIsRelease && rightIsRelease)
            {
                return 0;
            }

            if (leftIsRelease)
            {
                return 1;
            }

            if (rightIsRelease)
            {
                return -1;
            }

            int sharedLength = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
            for (int index = 0; index < sharedLength; index++)
            {
                int identifierComparison = ComparePrereleaseIdentifiers(
                    leftIdentifiers[index],
                    rightIdentifiers[index]);
                if (identifierComparison != 0)
                {
                    return identifierComparison;
                }
            }

            return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
        }

        private static int ComparePrereleaseIdentifiers(string leftIdentifier, string rightIdentifier)
        {
            (bool leftIsNumeric, int leftNumber) = ParseVersionPart(leftIdentifier);
            (bool rightIsNumeric, int rightNumber) = ParseVersionPart(rightIdentifier);
            if (leftIsNumeric && rightIsNumeric)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (leftIsNumeric)
            {
                return -1;
            }

            if (rightIsNumeric)
            {
                return 1;
            }

            return Math.Sign(string.CompareOrdinal(leftIdentifier, rightIdentifier));
        }

        private readonly struct ParsedCliVersion
        {
            public readonly int Major;
            public readonly int Minor;
            public readonly int Patch;
            public readonly string[] PrereleaseIdentifiers;

            public ParsedCliVersion(
                int major,
                int minor,
                int patch,
                string[] prereleaseIdentifiers)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PrereleaseIdentifiers = prereleaseIdentifiers;
            }
        }
    }
}
