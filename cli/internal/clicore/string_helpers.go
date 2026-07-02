package clicore

import "strings"

func FirstHelpLine(description string) string {
	for _, line := range strings.Split(description, "\n") {
		trimmed := strings.TrimSpace(line)
		if trimmed != "" {
			return trimmed
		}
	}
	return ""
}

func FirstNonEmpty(values ...string) string {
	for _, value := range values {
		if value != "" {
			return value
		}
	}
	return ""
}

func ErrorMessage(err error) string {
	if err == nil {
		return ""
	}
	return err.Error()
}
