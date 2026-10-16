package dispatcher

import (
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck"
)

const (
	compileCheckVibeStartOperation    = "cli_compile_check_start"
	compileCheckVibeCompleteOperation = "cli_compile_check_complete"
	// loggedChangedNames is how many of a surface change's names a log entry keeps.
	loggedChangedNames = 10
)

// compileCheckVibeLog writes what one compile-check run did to the CLI VibeLog: a start entry, and a
// complete entry under the same correlation id on every way out of the run.
// Why compile-check needs its own entries: it never talks to the Editor, so nothing on the Editor
// side records which runs compiled, which were checked on their changed files, and why.
type compileCheckVibeLog struct {
	enabled       bool
	projectRoot   string
	correlationID string
	startedAt     time.Time
}

// startCompileCheckVibeLog writes the start entry of a run and returns what writes its complete
// entry. With ULOOP_DEBUG unset it writes nothing and builds no context.
func startCompileCheckVibeLog(projectRoot string, options compileCheckOptions) compileCheckVibeLog {
	if !vibelog.IsCLIVibeLogEnabled() {
		return compileCheckVibeLog{}
	}

	logRun := compileCheckVibeLog{
		enabled:       true,
		projectRoot:   projectRoot,
		correlationID: vibelog.NewCLIVibeCorrelationID(),
		startedAt:     time.Now(),
	}
	logRun.write("INFO", compileCheckVibeStartOperation, "Starting compile-check.", map[string]any{
		"command":          clicore.CompileCheckCommandName,
		"all":              options.all,
		"full":             options.full,
		"jobs":             options.jobs,
		"project_identity": vibelog.ProjectIdentity(projectRoot),
	})

	return logRun
}

// failed writes the complete entry of a run that stopped before it had a response to print.
func (logRun compileCheckVibeLog) failed(err error) {
	if !logRun.enabled {
		return
	}

	context := logRun.completeContext("failed")
	context["error"] = clicore.ErrorMessage(err)
	context["error_code"] = clierrors.ClassifyError(err, clierrors.ErrorContext{
		Command: clicore.CompileCheckCommandName, ProjectRoot: logRun.projectRoot,
	}).ErrorCode
	if report, found := compilecheck.BuildSettingsReportOf(err); found {
		context["build_settings"] = compileCheckVibeBuildSettings(report)
	}
	logRun.write("ERROR", compileCheckVibeCompleteOperation, "compile-check failed.", context)
}

// completed writes the complete entry of a run that produced a response, with the response's own
// lists so the log and the printed JSON can be read side by side.
func (logRun compileCheckVibeLog) completed(response compileCheckResponse, result compilecheck.Result) {
	if !logRun.enabled {
		return
	}

	level, outcome := "INFO", "succeeded"
	if response.ErrorCount > 0 {
		level, outcome = "WARNING", "errors"
	}
	context := logRun.completeContext(outcome)
	context["error_count"] = response.ErrorCount
	context["warning_count"] = response.WarningCount
	context["compiled"] = response.CompiledAssemblies
	context["reused"] = response.ReusedAssemblies
	context["fast_checked"] = response.FastCheckedAssemblies
	context["blocked"] = response.BlockedAssemblies
	context["skipped_count"] = response.SkippedAssemblies
	context["fast_checks"] = compileCheckVibeFastChecks(response.FastChecks)
	context["fallbacks"] = compileCheckVibeReasons(response.FastPathFallbacks)
	context["baseline_notes"] = compileCheckVibeReasons(response.FastPathBaselineNotes)
	context["units"] = compileCheckVibeUnits(result.Units)
	context["surface_checks"] = compileCheckVibeSurfaceChecks(result.Units)
	addCompileCheckVibeNameNarrowing(context, result)
	context["build_settings"] = compileCheckVibeBuildSettings(result.BuildSettings)
	logRun.write(level, compileCheckVibeCompleteOperation, "compile-check finished.", context)
}

// completeContext holds the fields every complete entry carries.
func (logRun compileCheckVibeLog) completeContext(outcome string) map[string]any {
	return map[string]any{
		"command":     clicore.CompileCheckCommandName,
		"outcome":     outcome,
		"duration_ms": time.Since(logRun.startedAt).Milliseconds(),
	}
}

// write appends one entry of this run.
// Why a failed write is ignored: the log is a diagnostic aid, and a run must not fail for it.
func (logRun compileCheckVibeLog) write(level string, operation string, message string, context map[string]any) {
	_ = vibelog.WriteCLIVibeLog(logRun.projectRoot, vibelog.CLIVibeLogEntry{
		Level:         level,
		Operation:     operation,
		Message:       message,
		Context:       context,
		CorrelationID: logRun.correlationID,
	})
}

// compileCheckVibeBuildSettings says whether the last Unity build left a build-trace file, what
// happened to the build-settings record, and which setting a refusal was for.
func compileCheckVibeBuildSettings(report compilecheck.BuildSettingsReport) map[string]any {
	return map[string]any{
		"stamp_found":     report.StampFound,
		"record":          report.Record,
		"refused_part":    report.RefusedPart,
		"defines_checked": report.DefinesChecked,
	}
}

// compileCheckVibeFastChecks lists each fast check by assembly, whether it was replayed, and how
// many files it covered.
func compileCheckVibeFastChecks(checks []compileCheckFastCheck) []map[string]any {
	entries := make([]map[string]any, 0, len(checks))
	for _, check := range checks {
		entries = append(entries, map[string]any{
			"assembly":           check.Assembly,
			"reused":             check.Reused,
			"changed_file_count": len(check.ChangedFiles),
		})
	}

	return entries
}

// compileCheckVibeReasons lists one reason per assembly.
func compileCheckVibeReasons(reasons []compileCheckFastPathFallback) []map[string]any {
	entries := make([]map[string]any, 0, len(reasons))
	for _, reason := range reasons {
		entries = append(entries, map[string]any{"assembly": reason.Assembly, "reason": reason.Reason})
	}

	return entries
}

// compileCheckVibeUnits lists how long each unit this run compiled or checked took, and how it was
// scheduled when that was out of the ordinary: the references it assumed kept Unity's surface, the
// one that made it compile again and the time the compile it threw away took, and that it waited
// for a record that then did not apply - a unit whose record applied is replayed and left out.
// Why replayed units are left out: they took no work, and their durations would only hide which
// units the run spent its time on.
// Why the scheduling keys appear only when they say something: most units carry none of them, and a
// line of empty values would hide the few that do.
func compileCheckVibeUnits(units []compilecheck.UnitResult) []map[string]any {
	entries := make([]map[string]any, 0, len(units))
	for _, unit := range units {
		if unit.Reused || unit.FastCheckReused {
			continue
		}
		entry := map[string]any{
			"assembly":    unit.Assembly,
			"duration_ms": unit.Duration.Milliseconds(),
			"succeeded":   unit.Succeeded,
		}
		addCompileCheckVibeScheduling(entry, unit)
		entries = append(entries, entry)
	}

	return entries
}

// addCompileCheckVibeScheduling adds to a units entry the scheduling keys that apply to the unit.
func addCompileCheckVibeScheduling(entry map[string]any, unit compilecheck.UnitResult) {
	if len(unit.AssumedReferences) > 0 {
		entry["assumed_kept"] = unit.AssumedReferences
	}
	if unit.CompiledAgainAfter != "" {
		entry["compiled_again_after"] = unit.CompiledAgainAfter
	}
	if unit.DiscardedDuration > 0 {
		entry["discarded_ms"] = unit.DiscardedDuration.Milliseconds()
	}
	if unit.WaitedForRecord {
		entry["waited_for_record"] = true
	}
	if len(unit.ChangedReferences) > 0 {
		changedBy := make([]map[string]any, 0, len(unit.ChangedReferences))
		for _, reference := range unit.ChangedReferences {
			entry := map[string]any{"reference": reference.Assembly, "name": reference.Name}
			if reference.Reason != "" {
				entry["reason"] = reference.Reason
			}
			changedBy = append(changedBy, entry)
		}
		entry["changed_by"] = changedBy
	}
}

// addCompileCheckVibeNameNarrowing adds the units left out because they write none of the names a
// reference's change touches, and what reading the names took, each only when there is any.
func addCompileCheckVibeNameNarrowing(context map[string]any, result compilecheck.Result) {
	if len(result.NameSkips) > 0 {
		skips := make([]map[string]any, 0, len(result.NameSkips))
		for _, skip := range result.NameSkips {
			skips = append(skips, map[string]any{"assembly": skip.Assembly, "unaffected_by": skip.ChangedReferences})
		}
		context["name_skips"] = skips
	}
	if result.NameIndex.Units > 0 {
		context["name_index"] = map[string]any{
			"duration_ms":       result.NameIndex.Duration.Milliseconds(),
			"units":             result.NameIndex.Units,
			"with_generated":    result.NameIndex.WithGenerated,
			"without_generated": result.NameIndex.WithoutGenerated,
			"unreadable":        result.NameIndex.Unreadable,
			"sources_read":      result.NameIndex.SourcesRead,
		}
	}
}

// compileCheckVibeSurfaceChecks lists every unit whose surface the worker, or its record of the same
// comparison, settled: what it answered and how long asking took.
// Why replayed units are kept here, unlike in units: a replayed unit is compared just the same, and
// its verdict decides whether its dependents compile.
func compileCheckVibeSurfaceChecks(units []compilecheck.UnitResult) []map[string]any {
	entries := make([]map[string]any, 0, len(units))
	for _, unit := range units {
		if !unit.Surface.Asked {
			continue
		}
		entry := map[string]any{
			"assembly":           unit.Assembly,
			"verdict":            unit.Surface.Reason,
			"detail":             unit.Surface.Detail,
			"duration_ms":        unit.Surface.Duration.Milliseconds(),
			"from_record":        unit.Surface.FromRecord,
			"changed_name_count": len(unit.Surface.ChangedNames),
			"changed_names":      firstChangedNames(unit.Surface.ChangedNames),
		}
		if unit.Surface.Unnamed != "" {
			entry["unnamed"] = unit.Surface.Unnamed
		}
		entries = append(entries, entry)
	}

	return entries
}

// firstChangedNames keeps the log line short: a change can name thousands of identifiers.
func firstChangedNames(names []string) []string {
	if len(names) > loggedChangedNames {
		return names[:loggedChangedNames]
	}
	if names == nil {
		return []string{}
	}

	return names
}
