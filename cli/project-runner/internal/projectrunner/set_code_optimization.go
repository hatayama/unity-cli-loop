package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	setCodeOptimizationDebugStartupCommandName = "set-code-optimization-debug-startup"
	setCodeOptimizationStartupFlag             = "--startup"
)

type setCodeOptimizationArguments struct {
	startup bool
}

type setCodeOptimizationCommandDependencies struct {
	send func(context.Context, unityipc.Connection, string) (json.RawMessage, error)
}

func parseSetCodeOptimizationArguments(args []string) (setCodeOptimizationArguments, error) {
	if len(args) == 0 {
		return setCodeOptimizationArguments{}, setCodeOptimizationModeError("")
	}
	if args[0] != "debug" {
		return setCodeOptimizationArguments{}, setCodeOptimizationModeError(args[0])
	}

	arguments := setCodeOptimizationArguments{}
	for _, argument := range args[1:] {
		if !strings.HasPrefix(argument, "--") {
			return setCodeOptimizationArguments{}, setCodeOptimizationModeError(argument)
		}
		if argument != setCodeOptimizationStartupFlag || arguments.startup {
			return setCodeOptimizationArguments{}, &clierrors.ArgumentError{
				Message: fmt.Sprintf("Unknown option for %s: %s", clicore.SetCodeOptimizationCommandName, argument),
				Option:  argument,
				Command: clicore.SetCodeOptimizationCommandName,
				NextActions: []string{
					"Run `uloop set-code-optimization --help` to inspect supported arguments.",
				},
			}
		}
		arguments.startup = true
	}
	return arguments, nil
}

func setCodeOptimizationModeError(mode string) *clierrors.ArgumentError {
	message := "set-code-optimization requires the mode argument: debug."
	if mode != "" {
		message = fmt.Sprintf("Unsupported Code Optimization mode %q; only debug is supported.", mode)
	}
	return &clierrors.ArgumentError{
		Message: message,
		Option:  mode,
		Command: clicore.SetCodeOptimizationCommandName,
		NextActions: []string{
			"Run `uloop set-code-optimization debug` for this Editor session.",
			"Run `uloop set-code-optimization debug --startup` to also persist the machine-wide startup preference after user approval.",
		},
	}
}

func runSetCodeOptimizationCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	dependencies := setCodeOptimizationCommandDependencies{send: sendSetCodeOptimizationBridgeCommand}
	return runSetCodeOptimizationCommandWithDependencies(
		ctx,
		connection,
		args,
		stdout,
		stderr,
		dependencies)
}

func runSetCodeOptimizationCommandWithDependencies(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	dependencies setCodeOptimizationCommandDependencies,
) int {
	arguments, err := parseSetCodeOptimizationArguments(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.SetCodeOptimizationCommandName,
		})
		return 1
	}

	bridgeCommand := setCodeOptimizationDebugCommandName
	if arguments.startup {
		bridgeCommand = setCodeOptimizationDebugStartupCommandName
	}
	result, err := dependencies.send(ctx, connection, bridgeCommand)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.SetCodeOptimizationCommandName,
		})
		return 1
	}

	clicore.WriteJSON(stdout, result)
	return toolEnvelopeExitCode(result)
}

func sendSetCodeOptimizationBridgeCommand(
	ctx context.Context,
	connection unityipc.Connection,
	bridgeCommand string,
) (json.RawMessage, error) {
	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		ctx,
		bridgeCommand,
		map[string]any{})
	return result, err
}
