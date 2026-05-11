package version

import (
	"strconv"
	"strings"
)

type semanticVersion struct {
	major      int
	minor      int
	patch      int
	prerelease string
}

func IsLessThan(left string, right string) bool {
	result, ok := Compare(left, right)
	return ok && result < 0
}

func Compare(left string, right string) (int, bool) {
	leftVersion, leftOK := parseSemanticVersion(left)
	rightVersion, rightOK := parseSemanticVersion(right)
	if !leftOK || !rightOK {
		return 0, false
	}

	if leftVersion.major != rightVersion.major {
		return compareInt(leftVersion.major, rightVersion.major), true
	}
	if leftVersion.minor != rightVersion.minor {
		return compareInt(leftVersion.minor, rightVersion.minor), true
	}
	if leftVersion.patch != rightVersion.patch {
		return compareInt(leftVersion.patch, rightVersion.patch), true
	}
	return comparePrerelease(leftVersion.prerelease, rightVersion.prerelease), true
}

func parseSemanticVersion(value string) (semanticVersion, bool) {
	trimmed := trimVersionPrefix(value)
	withoutBuildMetadata, _, _ := strings.Cut(trimmed, "+")
	versionPart, prerelease, hasPrerelease := strings.Cut(withoutBuildMetadata, "-")
	parts := strings.Split(versionPart, ".")
	if len(parts) != 3 {
		return semanticVersion{}, false
	}
	if hasPrerelease && !isValidPrerelease(prerelease) {
		return semanticVersion{}, false
	}

	major, ok := parseVersionPart(parts[0])
	if !ok {
		return semanticVersion{}, false
	}
	minor, ok := parseVersionPart(parts[1])
	if !ok {
		return semanticVersion{}, false
	}
	patch, ok := parseVersionPart(parts[2])
	if !ok {
		return semanticVersion{}, false
	}

	return semanticVersion{
		major:      major,
		minor:      minor,
		patch:      patch,
		prerelease: prerelease,
	}, true
}

func trimVersionPrefix(value string) string {
	trimmed := strings.TrimSpace(value)
	if strings.HasPrefix(trimmed, "v") || strings.HasPrefix(trimmed, "V") {
		return trimmed[1:]
	}
	return trimmed
}

func parseVersionPart(value string) (int, bool) {
	if value == "" {
		return 0, false
	}
	if hasLeadingZero(value) {
		return 0, false
	}
	if !containsOnlyDigits(value) {
		return 0, false
	}
	parsed, err := strconv.Atoi(value)
	if err != nil {
		return 0, false
	}
	return parsed, true
}

func isValidPrerelease(value string) bool {
	if value == "" {
		return false
	}
	for _, identifier := range strings.Split(value, ".") {
		if identifier == "" {
			return false
		}
		if !containsOnlyPrereleaseCharacters(identifier) {
			return false
		}
		if containsOnlyDigits(identifier) && hasLeadingZero(identifier) {
			return false
		}
	}
	return true
}

func hasLeadingZero(value string) bool {
	return len(value) > 1 && strings.HasPrefix(value, "0")
}

func containsOnlyDigits(value string) bool {
	for _, character := range value {
		if character < '0' || character > '9' {
			return false
		}
	}
	return true
}

func containsOnlyPrereleaseCharacters(value string) bool {
	for _, character := range value {
		if character >= '0' && character <= '9' {
			continue
		}
		if character >= 'A' && character <= 'Z' {
			continue
		}
		if character >= 'a' && character <= 'z' {
			continue
		}
		if character == '-' {
			continue
		}
		return false
	}
	return true
}

func compareInt(left int, right int) int {
	if left < right {
		return -1
	}
	if left > right {
		return 1
	}
	return 0
}

func comparePrerelease(left string, right string) int {
	if left == right {
		return 0
	}
	if left == "" {
		return 1
	}
	if right == "" {
		return -1
	}
	leftParts := strings.Split(left, ".")
	rightParts := strings.Split(right, ".")
	length := len(leftParts)
	if len(rightParts) > length {
		length = len(rightParts)
	}
	for index := 0; index < length; index++ {
		if index >= len(leftParts) {
			return -1
		}
		if index >= len(rightParts) {
			return 1
		}
		result := comparePrereleasePart(leftParts[index], rightParts[index])
		if result != 0 {
			return result
		}
	}
	return 0
}

func comparePrereleasePart(left string, right string) int {
	leftNumber, leftIsNumber := parseVersionPart(left)
	rightNumber, rightIsNumber := parseVersionPart(right)
	if leftIsNumber && rightIsNumber {
		return compareInt(leftNumber, rightNumber)
	}
	if leftIsNumber {
		return -1
	}
	if rightIsNumber {
		return 1
	}
	return strings.Compare(left, right)
}
