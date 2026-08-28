package clierrors

import "fmt"

const projectNotFoundMessage = "unity project not found. Use --project-path option to specify the target"

type ProjectNotFoundError struct{}

func (err ProjectNotFoundError) Error() string {
	return projectNotFoundMessage
}

func (err ProjectNotFoundError) ToCLIError(context ErrorContext) CLIError {
	return projectResolveCLIError(err.Error(), context)
}

type MultipleProjectsFoundError struct {
	SearchRoot string
}

func (err MultipleProjectsFoundError) Error() string {
	return fmt.Sprintf(
		"multiple Unity projects found under %s; use --project-path to choose one",
		err.SearchRoot)
}

func (err MultipleProjectsFoundError) ToCLIError(context ErrorContext) CLIError {
	return projectResolveCLIError(err.Error(), context)
}

type NotUnityProjectError struct {
	ProjectRoot string
	Suggestion  string
}

func (err NotUnityProjectError) Error() string {
	if err.Suggestion == "" {
		return fmt.Sprintf("not a Unity project: %s", err.ProjectRoot)
	}
	return fmt.Sprintf(
		"not a Unity project: %s. This looks like a WSL or Git Bash path. Did you mean: %s",
		err.ProjectRoot,
		err.Suggestion)
}

func (err NotUnityProjectError) ToCLIError(context ErrorContext) CLIError {
	return projectResolveCLIError(err.Error(), context)
}

func projectResolveCLIError(message string, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeProjectNotFound,
		Phase:       ErrorPhaseProjectResolve,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		Command:     context.Command,
		NextActions: []string{
			"Run the command from inside a Unity project.",
			"Pass `--project-path <path>` when targeting another Unity project.",
		},
	}
}
