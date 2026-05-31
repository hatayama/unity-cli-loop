package cli

import "fmt"

type argumentError struct {
	message      string
	option       string
	received     string
	expectedType string
	command      string
	nextActions  []string
}

func (err *argumentError) Error() string {
	return err.message
}

func (err *argumentError) toCLIError(context errorContext) cliError {
	command := firstNonEmpty(err.command, context.command)
	details := map[string]any{}
	if err.option != "" {
		details["option"] = err.option
	}
	if err.received != "" {
		details["received"] = err.received
	}
	if err.expectedType != "" {
		details["expectedType"] = err.expectedType
	}

	nextActions := err.nextActions
	if len(nextActions) == 0 {
		nextActions = []string{"Correct the command arguments and retry."}
	}

	return cliError{
		ErrorCode:   errorCodeInvalidArgument,
		Phase:       errorPhaseArgumentParsing,
		Message:     err.message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.projectRoot,
		Command:     command,
		NextActions: nextActions,
		Details:     details,
	}
}

func missingValueArgumentError(option string) *argumentError {
	return &argumentError{
		message:      fmt.Sprintf("%s requires a value", option),
		option:       option,
		expectedType: "string",
		nextActions:  []string{fmt.Sprintf("Pass a value after `%s` or use `%s=<value>`.", option, option)},
	}
}

func invalidValueArgumentError(option string, received string, expectedType string) *argumentError {
	return &argumentError{
		message:      fmt.Sprintf("Invalid %s value for %s: %s", expectedType, option, received),
		option:       option,
		received:     received,
		expectedType: expectedType,
		nextActions:  []string{fmt.Sprintf("Pass a valid %s value for `%s`.", expectedType, option)},
	}
}
