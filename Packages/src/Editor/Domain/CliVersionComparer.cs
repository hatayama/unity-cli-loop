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

        public static bool IsVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return TryCompareCliVersions(leftVersion, rightVersion, out int comparison) && comparison >= 0;
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

            string normalized = version.Trim().TrimStart('v', 'V');
            string[] buildParts = normalized.Split(new[] { '+' }, 2);
            string versionWithoutBuildMetadata = buildParts[0];
            string[] prereleaseParts = versionWithoutBuildMetadata.Split(new[] { '-' }, 2);
            string coreVersion = prereleaseParts[0];
            string[] coreParts = coreVersion.Split('.');
            if (coreParts.Length != 3)
            {
                return false;
            }

            bool hasMajor = int.TryParse(coreParts[0], out int major);
            bool hasMinor = int.TryParse(coreParts[1], out int minor);
            bool hasPatch = int.TryParse(coreParts[2], out int patch);
            if (!hasMajor || !hasMinor || !hasPatch)
            {
                return false;
            }

            string[] prereleaseIdentifiers = Array.Empty<string>();
            if (prereleaseParts.Length == 2)
            {
                if (string.IsNullOrEmpty(prereleaseParts[1]))
                {
                    return false;
                }

                prereleaseIdentifiers = prereleaseParts[1].Split('.');
            }

            parsedVersion = new ParsedCliVersion(major, minor, patch, prereleaseIdentifiers);
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
            bool leftIsNumeric = int.TryParse(leftIdentifier, out int leftNumber);
            bool rightIsNumeric = int.TryParse(rightIdentifier, out int rightNumber);
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

            return string.CompareOrdinal(leftIdentifier, rightIdentifier);
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
