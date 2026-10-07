package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strconv"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Named here rather than in cli/common because only this dispatch branch needs the name.
const hotReloadCommandName = "hot-reload"

const (
	hotReloadCompileFallbackRequestedValue = "Requested"
	hotReloadCompileResultField            = "Compile"
	hotReloadCompileFallbackNoteField      = "CompileFallbackNote"
	hotReloadSuccessField                  = "Success"
	hotReloadRecommendedNextActionField    = "RecommendedNextAction"
	hotReloadMessageField                  = "Message"
	hotReloadWarningsField                 = "Warnings"
	hotReloadOutcomeField                  = "Outcome"
	hotReloadAutoRefreshHeldField          = "AutoRefreshHeld"
	hotReloadAutoRefreshHoldMessageField   = "AutoRefreshHoldMessage"
	hotReloadTimingField                   = "Timing"
	hotReloadFallbackCompileMsField        = "FallbackCompileMs"
)

// Raw JSON values, because the response fields are edited as encoded JSON.
const (
	hotReloadOutcomeReplacedByCompileJSON = `"ReplacedByCompile"`
	hotReloadAutoRefreshReleasedJSON      = "false"
)

const (
	// %s names the response field that says which edits were left unapplied.
	hotReloadCompileFallbackSucceededNoteFormat = "Hot reload left edits unapplied (see %s), so a compile ran in this same command and succeeded: every edit is compiled in, and the domain reload discarded the active hot-reload patches."
	hotReloadCompileFallbackFailedNoteFormat    = "Hot reload left edits unapplied (see %s), so a compile ran in this same command and failed: see Compile.Errors."
	hotReloadUnappliedInWarnings                = "Warnings"
	// A run can leave edits unapplied with no warning at all, and then the reasons are only on the
	// per-method rows.
	hotReloadUnappliedInMethodReasons        = "Methods[].Reason"
	hotReloadCompileFallbackFailedNextAction = "Fix the errors in Compile.Errors, then rerun 'uloop compile' or 'uloop hot-reload'."
	// The reload's own Message describes only the reload, so without this suffix a reader who stops at
	// Message sees failed outcomes even though the compile then applied every edit.
	hotReloadCompileFallbackSucceededMessageSuffix = " A compile then ran in this same command and succeeded; see CompileFallbackNote."
)

// Test seam, same shape as the run-tests implicit compile.
var hotReloadFallbackCompile = hotReloadFallbackCompileDefault

func hotReloadFallbackCompileDefault(
	ctx context.Context,
	connection unityipc.Connection,
	stderr io.Writer,
) compileExecutionResult {
	return hotReloadFallbackCompileWithDeps(ctx, connection, stderr, defaultCompileWaitDeps())
}

func hotReloadFallbackCompileWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	stderr io.Writer,
	deps compileWaitDeps,
) compileExecutionResult {
	// Why not the plain compile entry: it would hand back an earlier timed-out compile's stored
	// result, which predates the edits this command goes on to rely on.
	return runCompileOfCurrentSourcesResultWithDeps(ctx, connection, stderr, deps)
}

// runHotReloadWithCompileFallback runs hot reload and, when the Editor answered that the run left
// edits unapplied, compiles in the same command so the caller does not have to spend another round
// trip on the compile the response would otherwise only recommend.
func runHotReloadWithCompileFallback(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
) int {
	result := runPlainTool(ctx, connection, hotReloadCommandName, params, stderr)
	if len(result.result) == 0 {
		return result.exitCode
	}
	if !isHotReloadCompileFallbackRequested(result.result) {
		clicore.WriteJSON(stdout, result.result)
		return result.exitCode
	}

	compileStarted := time.Now()
	compileResult := hotReloadFallbackCompile(ctx, connection, stderr)
	compileElapsed := time.Since(compileStarted)
	if len(compileResult.result) == 0 {
		// The transport failure is already classified on stderr; the reload itself still happened.
		clicore.WriteJSON(stdout, result.result)
		return compileResult.exitCode
	}

	merged, err := injectHotReloadCompileFallback(result.result, compileResult.result)
	if err == nil {
		merged, err = addHotReloadFallbackCompileTiming(merged, compileElapsed)
	}
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     hotReloadCommandName,
		})
		return 1
	}
	clicore.WriteJSON(stdout, merged)
	return compileResult.exitCode
}

// A package that does not report the field, or reports a value this CLI does not know, never gets a
// compile: only an explicit request may end a Play session or discard active patches.
func isHotReloadCompileFallbackRequested(raw []byte) bool {
	answer := struct {
		CompileFallback string `json:"CompileFallback"`
	}{}
	if err := json.Unmarshal(raw, &answer); err != nil {
		return false
	}
	return answer.CompileFallback == hotReloadCompileFallbackRequestedValue
}

func injectHotReloadCompileFallback(raw json.RawMessage, compileRaw json.RawMessage) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}
	if fields == nil {
		return nil, errors.New("hot-reload response must be a JSON object")
	}
	compile := struct {
		Success     bool     `json:"Success"`
		NextActions []string `json:"NextActions"`
	}{}
	if err := json.Unmarshal(compileRaw, &compile); err != nil {
		return nil, err
	}

	// Why the compile decides Success: once it succeeded, every edit the reload could not apply is
	// compiled in, which is the outcome the caller asked for; once it failed, the command as a
	// whole did not deliver that outcome.
	success, err := json.Marshal(compile.Success)
	if err != nil {
		return nil, err
	}
	fields[hotReloadCompileResultField] = compileRaw
	fields[hotReloadSuccessField] = success

	note, nextAction, err := hotReloadCompileFallbackAdvice(
		compile.Success,
		hotReloadUnappliedPointer(fields),
		compile.NextActions)
	if err != nil {
		return nil, err
	}
	fields[hotReloadCompileFallbackNoteField] = note
	if !compile.Success {
		fields[hotReloadRecommendedNextActionField] = nextAction
		return json.Marshal(fields)
	}
	// The reload's own next action says to run 'uloop compile', which this command just did.
	delete(fields, hotReloadRecommendedNextActionField)
	// Before the compile sentence is appended, so the hold sentence is still at the end of Message.
	if err := settleHotReloadStateAfterCompile(fields); err != nil {
		return nil, err
	}
	if err := appendHotReloadCompileSucceededMessage(fields); err != nil {
		return nil, err
	}
	return json.Marshal(fields)
}

// Adds the fallback compile's wall time to Timing. The phases the Editor reported stay as they were,
// and a response from an older package, which sends no Timing, gets one holding only the compile.
func addHotReloadFallbackCompileTiming(raw []byte, elapsed time.Duration) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}
	timing := map[string]json.RawMessage{}
	// Why only an object is decoded: JSON null would leave the map nil and any other value would
	// fail the decode, while neither holds a phase worth keeping, so both count as absent.
	if existing := fields[hotReloadTimingField]; len(existing) > 0 && existing[0] == '{' {
		if err := json.Unmarshal(existing, &timing); err != nil {
			return nil, err
		}
	}
	timing[hotReloadFallbackCompileMsField] = json.RawMessage(strconv.FormatInt(elapsed.Milliseconds(), 10))
	encoded, err := json.Marshal(timing)
	if err != nil {
		return nil, err
	}
	fields[hotReloadTimingField] = encoded
	return json.Marshal(fields)
}

// A successful compile reloaded the domain: every edit is compiled in, the patches are gone, and
// the Auto Refresh hold is released, so the reload's own Outcome and hold sentence are stale.
// Outcome is written even for an older package that sent none, so every merged response says the
// compile replaced the reload. AutoRefreshHeld is only corrected, never added: a response without
// it comes from a package that never reported the hold.
func settleHotReloadStateAfterCompile(fields map[string]json.RawMessage) error {
	fields[hotReloadOutcomeField] = json.RawMessage(hotReloadOutcomeReplacedByCompileJSON)
	if _, present := fields[hotReloadAutoRefreshHeldField]; present {
		fields[hotReloadAutoRefreshHeldField] = json.RawMessage(hotReloadAutoRefreshReleasedJSON)
	}
	return removeHotReloadHoldSentence(fields)
}

// Removes " <AutoRefreshHoldMessage>" from the end of Message, where the Editor appended it, so the
// CLI never needs its own copy of the sentence. Anything else is left alone: an older package sends
// no AutoRefreshHoldMessage, and a run that did not arm the hold omits it.
func removeHotReloadHoldSentence(fields map[string]json.RawMessage) error {
	holdSentence, isString, err := readHotReloadStringField(fields, hotReloadAutoRefreshHoldMessageField)
	if err != nil || !isString {
		return err
	}
	message, isString, err := readHotReloadStringField(fields, hotReloadMessageField)
	if err != nil || !isString {
		return err
	}
	withoutHold, found := strings.CutSuffix(message, " "+holdSentence)
	if !found {
		return nil
	}
	trimmed, err := json.Marshal(withoutHold)
	if err != nil {
		return err
	}
	fields[hotReloadMessageField] = trimmed
	return nil
}

// A Message that is missing or not a string is left alone: only an older or unexpected package
// sends one, and inventing a Message would claim a reload summary the Editor never wrote.
func appendHotReloadCompileSucceededMessage(fields map[string]json.RawMessage) error {
	message, isString, err := readHotReloadStringField(fields, hotReloadMessageField)
	if err != nil || !isString {
		return err
	}
	appended, err := json.Marshal(message + hotReloadCompileFallbackSucceededMessageSuffix)
	if err != nil {
		return err
	}
	fields[hotReloadMessageField] = appended
	return nil
}

// readHotReloadStringField decodes a response field that holds a JSON string, and reports false for
// a field that is missing or holds anything else.
// Why the first byte is checked: decoding JSON null into a string succeeds and leaves it empty, so
// the decode alone would treat a null field as an empty string and write a string back over it.
func readHotReloadStringField(fields map[string]json.RawMessage, name string) (string, bool, error) {
	raw := fields[name]
	if len(raw) == 0 || raw[0] != '"' {
		return "", false, nil
	}
	value := ""
	if err := json.Unmarshal(raw, &value); err != nil {
		return "", false, err
	}
	return value, true, nil
}

// hotReloadUnappliedPointer names the response field that explains the unapplied edits, so the
// note never sends the reader to an empty Warnings array.
func hotReloadUnappliedPointer(fields map[string]json.RawMessage) string {
	var warnings []json.RawMessage
	if err := json.Unmarshal(fields[hotReloadWarningsField], &warnings); err != nil || len(warnings) == 0 {
		return hotReloadUnappliedInMethodReasons
	}
	return hotReloadUnappliedInWarnings
}

func hotReloadCompileFallbackAdvice(
	compileSucceeded bool,
	unappliedPointer string,
	compileNextActions []string,
) (json.RawMessage, json.RawMessage, error) {
	if compileSucceeded {
		note, err := json.Marshal(fmt.Sprintf(hotReloadCompileFallbackSucceededNoteFormat, unappliedPointer))
		if err != nil {
			return nil, nil, err
		}
		return note, nil, nil
	}
	note, err := json.Marshal(fmt.Sprintf(hotReloadCompileFallbackFailedNoteFormat, unappliedPointer))
	if err != nil {
		return nil, nil, err
	}
	nextAction, err := json.Marshal(hotReloadCompileFallbackFailedNextActionText(compileNextActions))
	if err != nil {
		return nil, nil, err
	}
	return note, nextAction, nil
}

// A compile that refused to run (for example, in Play Mode under a setting that holds compiles)
// reports no errors, so pointing at Compile.Errors would send the reader to an empty list; the
// compile's own next actions say what actually unblocks it.
func hotReloadCompileFallbackFailedNextActionText(compileNextActions []string) string {
	if len(compileNextActions) == 0 {
		return hotReloadCompileFallbackFailedNextAction
	}
	return strings.Join(compileNextActions, " ")
}
