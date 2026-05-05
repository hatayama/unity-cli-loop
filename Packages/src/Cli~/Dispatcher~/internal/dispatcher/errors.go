package dispatcher

const (
	errorCodeInvalidArgument        = "INVALID_ARGUMENT"
	errorCodeProjectNotFound        = "PROJECT_NOT_FOUND"
	errorCodeProjectLocalCLIMissing = "PROJECT_LOCAL_CLI_MISSING"
	errorCodeInternalError          = "INTERNAL_ERROR"

	errorPhaseArgumentParsing = "argument_parsing"
	errorPhaseProjectResolve  = "project_resolution"
	errorPhaseDispatch        = "dispatch"
	errorPhaseExecution       = "execution"
)

type cliError struct {
	ErrorCode   string         `json:"errorCode"`
	Phase       string         `json:"phase"`
	Message     string         `json:"message"`
	Retryable   bool           `json:"retryable"`
	SafeToRetry bool           `json:"safeToRetry"`
	ProjectRoot string         `json:"projectRoot,omitempty"`
	Command     string         `json:"command,omitempty"`
	NextActions []string       `json:"nextActions"`
	Details     map[string]any `json:"details,omitempty"`
}

type cliErrorEnvelope struct {
	Success bool     `json:"success"`
	Error   cliError `json:"error"`
}

func argumentError(message string, command string) cliError {
	return cliError{
		ErrorCode:   errorCodeInvalidArgument,
		Phase:       errorPhaseArgumentParsing,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		Command:     command,
		NextActions: []string{"Correct the command arguments and retry."},
	}
}

func projectNotFoundError(message string, command string) cliError {
	return cliError{
		ErrorCode:   errorCodeProjectNotFound,
		Phase:       errorPhaseProjectResolve,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		Command:     command,
		NextActions: []string{
			"Run the command from inside a Unity project.",
			"Pass `--project-path <path>` before the command.",
		},
	}
}

func projectLocalCLIMissingError(localPath string, projectRoot string, command string) cliError {
	return cliError{
		ErrorCode:   errorCodeProjectLocalCLIMissing,
		Phase:       errorPhaseDispatch,
		Message:     "Project-local uloop-core CLI was not found.",
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		Command:     command,
		NextActions: []string{
			"Open the Unity project so the package can install the project-local CLI.",
			"Reinstall or update Unity CLI Loop in this project if the file is still missing.",
		},
		Details: map[string]any{
			"path": localPath,
		},
	}
}

func internalError(message string, projectRoot string) cliError {
	return cliError{
		ErrorCode:   errorCodeInternalError,
		Phase:       errorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		NextActions: []string{
			"Read the message and fix the local environment or command input before retrying.",
		},
	}
}
