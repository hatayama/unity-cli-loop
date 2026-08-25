package projectrunner

import (
	"context"
	"encoding/json"
	"io"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const runTestsCompileNote = "A compile ran before the tests and succeeded. Pass --skip-compile to run the tests without this compile step."

var runTestsImplicitCompile = runTestsImplicitCompileDefault

// runTestsWithImplicitCompile compiles before resolving the run-tests schema so an imported user
// script cannot leave this invocation using a stale cached parameter definition.
func runTestsWithImplicitCompile(
	ctx context.Context,
	connection unityipc.Connection,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	remainingArgs, skipCompile := extractRunTestsSkipCompileFlag(commandArgs)
	if skipCompile {
		return runDynamicProjectToolWithCompileNote(ctx, connection, clicore.RunTestsCommandName, remainingArgs, startPath, stdout, stderr, false)
	}

	compileResult := runTestsImplicitCompile(ctx, connection, stderr)
	if compileResult.exitCode != 0 {
		return writeCompileExecutionResult(stdout, compileResult)
	}

	return runDynamicProjectToolWithCompileNote(ctx, connection, clicore.RunTestsCommandName, remainingArgs, startPath, stdout, stderr, true)
}

func runTestsImplicitCompileDefault(
	ctx context.Context,
	connection unityipc.Connection,
	stderr io.Writer,
) compileExecutionResult {
	return runCompileWithDomainReloadWaitResultWithDeps(
		ctx,
		connection,
		map[string]any{},
		stderr,
		defaultCompileWaitDeps())
}

func extractRunTestsSkipCompileFlag(args []string) ([]string, bool) {
	remaining := make([]string, 0, len(args))
	skipCompile := false
	for _, arg := range args {
		if arg == "--"+tooldocs.RunTestsSkipCompileFlagName {
			skipCompile = true
			continue
		}
		remaining = append(remaining, arg)
	}
	return remaining, skipCompile
}

func injectRunTestsCompileNote(raw []byte) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}
	note, err := json.Marshal(runTestsCompileNote)
	if err != nil {
		return nil, err
	}
	fields["CompileNote"] = note
	return json.Marshal(fields)
}

func writeRunTestsCompileNoteError(stderr io.Writer, connection unityipc.Connection, err error) {
	clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
		ProjectRoot: connection.ProjectRoot,
		Command:     clicore.RunTestsCommandName,
	})
}
