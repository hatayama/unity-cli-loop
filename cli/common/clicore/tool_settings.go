package clicore

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
)

const (
	uloopSettingsDir = ".uloop"
	toolSettingsFile = "settings.tools.json"
)

type toolSettingsData struct {
	DisabledTools []string `json:"disabledTools"`
}

func LoadDisabledTools(projectRoot string) []string {
	settingsPath := filepath.Join(projectRoot, uloopSettingsDir, toolSettingsFile)
	content, err := os.ReadFile(settingsPath)
	if err != nil || len(strings.TrimSpace(string(content))) == 0 {
		return []string{}
	}

	settings := toolSettingsData{}
	if err := json.Unmarshal(content, &settings); err != nil {
		return []string{}
	}
	if settings.DisabledTools == nil {
		return []string{}
	}
	return settings.DisabledTools
}

func IsToolDisabledByToolSettings(toolName string, disabledTools []string) bool {
	if len(disabledTools) == 0 {
		return false
	}
	for _, disabledTool := range disabledTools {
		if disabledTool == toolName {
			return true
		}
	}
	return false
}
