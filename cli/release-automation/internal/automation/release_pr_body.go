package automation

import (
	"regexp"
	"strings"
)

const releasePRCheckVersionPattern = `(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?`

var (
	releasePRCheckPlainUnityPackageSummary = regexp.MustCompile(`<details><summary>(` + releasePRCheckVersionPattern + `)</summary>`)
	releasePRCheckDetailsBlock             = regexp.MustCompile(`(?s)<details><summary>[^<]+</summary>.*?</details>`)
	releasePRCheckComponentSummary         = regexp.MustCompile(`<details><summary>([^<:]+): (` + releasePRCheckVersionPattern + `)</summary>`)
)

func clarifyReleasePRCheckComponentLabels(body string) (string, bool) {
	clarifiedBody, summaryChanged := clarifyReleasePRCheckPlainUnityPackageSummary(body)
	clarifiedBody, headingChanged := clarifyReleasePRCheckComponentHeadings(clarifiedBody)
	return clarifiedBody, summaryChanged || headingChanged
}

func clarifyReleasePRCheckPlainUnityPackageSummary(body string) (string, bool) {
	if strings.Contains(body, "<details><summary>unity-package: ") {
		return body, false
	}

	matches := releasePRCheckPlainUnityPackageSummary.FindStringSubmatchIndex(body)
	if matches == nil {
		return body, false
	}

	version := body[matches[2]:matches[3]]
	replacement := "<details><summary>unity-package: " + version + "</summary>"
	return body[:matches[0]] + replacement + body[matches[1]:], true
}

func clarifyReleasePRCheckComponentHeadings(body string) (string, bool) {
	matches := releasePRCheckDetailsBlock.FindAllStringIndex(body, -1)
	if matches == nil {
		return body, false
	}

	builder := strings.Builder{}
	changed := false
	lastIndex := 0
	for _, match := range matches {
		block := body[match[0]:match[1]]
		clarifiedBlock, blockChanged := clarifyReleasePRCheckComponentHeadingBlock(block)

		builder.WriteString(body[lastIndex:match[0]])
		builder.WriteString(clarifiedBlock)
		lastIndex = match[1]
		changed = changed || blockChanged
	}

	if !changed {
		return body, false
	}

	builder.WriteString(body[lastIndex:])
	return builder.String(), true
}

func clarifyReleasePRCheckComponentHeadingBlock(block string) (string, bool) {
	matches := releasePRCheckComponentSummary.FindStringSubmatch(block)
	if matches == nil {
		return block, false
	}

	component := matches[1]
	version := matches[2]
	changed := false

	if summarySlug, found := releasePRCheckComponentSummarySlug(component); found {
		clarifiedSummary := "<details><summary>" + summarySlug + ": " + version + "</summary>"
		block = strings.Replace(block, matches[0], clarifiedSummary, 1)
		changed = true
	}

	displayName, found := releasePRCheckComponentDisplayName(component)
	if !found {
		return block, changed
	}

	heading := "## [" + version + "]("
	if !strings.Contains(block, heading) {
		return block, changed
	}

	clarifiedHeading := "## [" + displayName + " " + version + "]("
	return strings.Replace(block, heading, clarifiedHeading, 1), true
}

// releasePRCheckComponentSummarySlug renames component summary labels whose
// release-please component id lacks the uloop prefix. The release tag keeps
// the bare component id, so consumers that resolve tags from summaries must
// also accept the renamed slug (see release_tag_from_body in
// scripts/sync-published-release-pr-labels.sh).
func releasePRCheckComponentSummarySlug(component string) (string, bool) {
	if component == "dispatcher" {
		return "uloop-dispatcher", true
	}
	return "", false
}

func releasePRCheckComponentDisplayName(component string) (string, bool) {
	switch component {
	case "unity-package":
		return "Unity Package", true
	case "dispatcher", "uloop-dispatcher":
		return "uloop Dispatcher", true
	case "uloop-project-runner":
		return "uloop Project Runner", true
	default:
		return "", false
	}
}
