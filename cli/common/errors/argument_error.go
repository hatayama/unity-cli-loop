package clierrors

import "fmt"

type ArgumentError struct {
	Message      string
	Option       string
	Received     string
	ExpectedType string
	Command      string
	NextActions  []string
}

func (err *ArgumentError) Error() string {
	return err.Message
}

func (err *ArgumentError) ToCLIError(context ErrorContext) CLIError {
	command := firstNonEmpty(err.Command, context.Command)
	details := map[string]any{}
	if err.Option != "" {
		details["Option"] = err.Option
	}
	if err.Received != "" {
		details["Received"] = err.Received
	}
	if err.ExpectedType != "" {
		details["ExpectedType"] = err.ExpectedType
	}

	nextActions := err.NextActions
	if len(nextActions) == 0 {
		nextActions = []string{"Correct the command arguments and retry."}
	}

	return CLIError{
		ErrorCode:   ErrorCodeInvalidArgument,
		Phase:       ErrorPhaseArgumentParsing,
		Message:     err.Message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     command,
		NextActions: nextActions,
		Details:     details,
	}
}

func MissingValueArgumentError(option string) *ArgumentError {
	return &ArgumentError{
		Message:      fmt.Sprintf("%s requires a value", option),
		Option:       option,
		ExpectedType: "string",
		NextActions:  []string{fmt.Sprintf("Pass a value after `%s` or use `%s=<value>`.", option, option)},
	}
}

func InvalidValueArgumentError(option string, received string, expectedType string) *ArgumentError {
	return &ArgumentError{
		Message:      fmt.Sprintf("Invalid %s value for %s: %s", expectedType, option, received),
		Option:       option,
		Received:     received,
		ExpectedType: expectedType,
		NextActions:  []string{fmt.Sprintf("Pass a valid %s value for `%s`.", expectedType, option)},
	}
}
