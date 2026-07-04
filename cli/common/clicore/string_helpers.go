package clicore

import "github.com/hatayama/unity-cli-loop/common/tooldocs"

func FirstHelpLine(description string) string {
	return tooldocs.FirstHelpLine(description)
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
