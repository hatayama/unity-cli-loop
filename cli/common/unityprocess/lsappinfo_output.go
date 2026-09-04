package unityprocess

import (
	"strconv"
	"strings"
)

func parseLsappinfoFrontASN(output string) string {
	return parseLsappinfoASN(firstTrimmedLine(output))
}

func parseLsappinfoFindASN(output string) string {
	return parseLsappinfoASN(firstTrimmedLine(output))
}

func parseLsappinfoPID(output string) int {
	const prefix = `"pid"=`
	line := strings.TrimSpace(output)
	if !strings.HasPrefix(line, prefix) {
		return 0
	}
	pid, err := strconv.Atoi(strings.TrimSpace(strings.TrimPrefix(line, prefix)))
	if err != nil {
		return 0
	}
	return pid
}

func parseLsappinfoBundlePath(output string) string {
	const prefix = `"LSBundlePath"="`
	line := strings.TrimSpace(output)
	if !strings.HasPrefix(line, prefix) {
		return ""
	}
	path, found := strings.CutSuffix(strings.TrimPrefix(line, prefix), `"`)
	if !found || path == "" {
		return ""
	}
	return path
}

func countLsappinfoBundlePath(listOutput string, bundlePath string) int {
	// Exact match after TrimSpace: a prefix check would count Unity.app.bak as Unity.app.
	target := `bundle path="` + bundlePath + `"`
	count := 0
	normalized := strings.ReplaceAll(listOutput, "\r\n", "\n")
	normalized = strings.ReplaceAll(normalized, "\r", "\n")
	for _, line := range strings.Split(normalized, "\n") {
		if strings.TrimSpace(line) == target {
			count++
		}
	}
	return count
}

func parseLsappinfoASN(line string) string {
	if !strings.HasPrefix(line, "ASN:") || !strings.HasSuffix(line, ":") {
		return ""
	}
	asn := strings.TrimSuffix(line, ":")
	if asn == "ASN" || !strings.HasPrefix(asn, "ASN:") {
		return ""
	}
	return asn
}

func firstTrimmedLine(output string) string {
	normalized := strings.ReplaceAll(output, "\r\n", "\n")
	normalized = strings.ReplaceAll(normalized, "\r", "\n")
	line, _, _ := strings.Cut(normalized, "\n")
	return strings.TrimSpace(line)
}
