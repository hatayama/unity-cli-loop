using System;
using System.Globalization;

namespace io.github.hatayama.uLoopMCP
{
    internal static class CliVersionComparison
    {
        internal static bool TryCompare(string leftVersionText, string rightVersionText, out int comparison)
        {
            comparison = 0;

            if (!TryParse(leftVersionText, out CliSemanticVersion leftVersion))
            {
                return false;
            }

            if (!TryParse(rightVersionText, out CliSemanticVersion rightVersion))
            {
                return false;
            }

            comparison = Compare(leftVersion, rightVersion);
            return true;
        }

        internal static bool IsSameVersion(string leftVersionText, string rightVersionText)
        {
            if (!TryCompare(leftVersionText, rightVersionText, out int comparison))
            {
                return false;
            }

            return comparison == 0;
        }

        private static bool TryParse(string versionText, out CliSemanticVersion version)
        {
            version = default;

            if (string.IsNullOrWhiteSpace(versionText))
            {
                return false;
            }

            string normalized = versionText.Trim().TrimStart('v', 'V');
            int buildMetadataIndex = normalized.IndexOf('+');
            if (buildMetadataIndex >= 0)
            {
                normalized = normalized.Substring(0, buildMetadataIndex);
            }

            string preRelease = string.Empty;
            int preReleaseIndex = normalized.IndexOf('-');
            if (preReleaseIndex >= 0)
            {
                preRelease = normalized.Substring(preReleaseIndex + 1);
                normalized = normalized.Substring(0, preReleaseIndex);
            }

            if (preReleaseIndex >= 0 && !IsValidPreRelease(preRelease))
            {
                return false;
            }

            string[] components = normalized.Split('.');
            if (components.Length != 3)
            {
                return false;
            }

            if (!TryParseComponent(components[0], out int major))
            {
                return false;
            }

            if (!TryParseComponent(components[1], out int minor))
            {
                return false;
            }

            if (!TryParseComponent(components[2], out int patch))
            {
                return false;
            }

            version = new CliSemanticVersion(major, minor, patch, preRelease);
            return true;
        }

        private static bool TryParseComponent(string component, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(component))
            {
                return false;
            }

            return int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsValidPreRelease(string preRelease)
        {
            if (string.IsNullOrEmpty(preRelease))
            {
                return false;
            }

            string[] identifiers = preRelease.Split('.');
            foreach (string identifier in identifiers)
            {
                if (!IsValidPreReleaseIdentifier(identifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidPreReleaseIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            bool numericOnly = true;
            foreach (char character in identifier)
            {
                bool isDigit = character >= '0' && character <= '9';
                bool isUpper = character >= 'A' && character <= 'Z';
                bool isLower = character >= 'a' && character <= 'z';
                bool isHyphen = character == '-';
                if (!isDigit && !isUpper && !isLower && !isHyphen)
                {
                    return false;
                }

                numericOnly = numericOnly && isDigit;
            }

            return !numericOnly || identifier.Length == 1 || identifier[0] != '0';
        }

        private static int Compare(CliSemanticVersion left, CliSemanticVersion right)
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

            return ComparePreRelease(left, right);
        }

        private static int ComparePreRelease(CliSemanticVersion left, CliSemanticVersion right)
        {
            if (!left.HasPreRelease && !right.HasPreRelease)
            {
                return 0;
            }

            if (!left.HasPreRelease)
            {
                return 1;
            }

            if (!right.HasPreRelease)
            {
                return -1;
            }

            string[] leftIdentifiers = left.PreRelease.Split('.');
            string[] rightIdentifiers = right.PreRelease.Split('.');
            int maxLength = Math.Max(leftIdentifiers.Length, rightIdentifiers.Length);

            for (int i = 0; i < maxLength; i++)
            {
                if (i >= leftIdentifiers.Length)
                {
                    return -1;
                }

                if (i >= rightIdentifiers.Length)
                {
                    return 1;
                }

                int identifierComparison = ComparePreReleaseIdentifier(leftIdentifiers[i], rightIdentifiers[i]);
                if (identifierComparison != 0)
                {
                    return identifierComparison;
                }
            }

            return 0;
        }

        private static int ComparePreReleaseIdentifier(string left, string right)
        {
            bool leftIsNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
            bool rightIsNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);

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

            return string.Compare(left, right, StringComparison.Ordinal);
        }

        private readonly struct CliSemanticVersion
        {
            internal readonly int Major;
            internal readonly int Minor;
            internal readonly int Patch;
            internal readonly string PreRelease;

            internal CliSemanticVersion(int major, int minor, int patch, string preRelease)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PreRelease = preRelease;
            }

            internal bool HasPreRelease => !string.IsNullOrEmpty(PreRelease);
        }
    }
}
