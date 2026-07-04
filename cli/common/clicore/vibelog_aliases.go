package clicore

import "github.com/hatayama/unity-cli-loop/common/vibelog"

const (
	CLIVibeLogDirectory = vibelog.CLIVibeLogDirectory
	CLIVibeLogPrefix    = vibelog.CLIVibeLogPrefix
	CLIVibeLogEnvName   = vibelog.CLIVibeLogEnvName
)

type CLIVibeLogEntry = vibelog.CLIVibeLogEntry

func NewCLIVibeCorrelationID() string {
	return vibelog.NewCLIVibeCorrelationID()
}

func WriteCLIVibeLog(projectRoot string, entry CLIVibeLogEntry) error {
	return vibelog.WriteCLIVibeLog(projectRoot, entry)
}

func IsCLIVibeLogEnabled() bool {
	return vibelog.IsCLIVibeLogEnabled()
}

func ProjectIdentity(projectRoot string) string {
	return vibelog.ProjectIdentity(projectRoot)
}
