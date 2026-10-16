package compilecheck

import (
	"context"
	"path/filepath"
	"testing"
	"time"
)

// Verifies a plan refused for a define the build lacks carries the build-settings report on its
// error, so the run's log can say what the comparison found.
func TestBuildCompilePlanCarriesTheBuildSettingsReportOfARefusal(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO;ADDED_LATER")
	project.dateProjectSettings(t, time.Hour)

	_, err := BuildCompilePlan(project.root, planDagDirectory, false)

	assertBuildRequired(t, err, "ADDED_LATER")
	report, found := BuildSettingsReportOf(err)
	if !found || !report.StampFound || !report.DefinesChecked {
		t.Errorf("report = %+v (found %v), want the report of a run that found the build-trace file", report, found)
	}
}

// writeBuildStamp writes the build-trace file the build-settings record is keyed to.
func writeBuildStamp(t *testing.T, projectRoot string) {
	t.Helper()
	writeFileAt(t, filepath.Join(projectRoot, filepath.FromSlash(buildSettingsStampPath)), "{}")
}

// Verifies a plan that builds carries the build-settings report of the comparison it ran.
func TestBuildCompilePlanCarriesTheBuildSettingsReport(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeBuildStamp(t, projectRoot)

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	if plan.BuildSettings.Record != buildSettingsRecordNew {
		t.Errorf("record = %q, want %q", plan.BuildSettings.Record, buildSettingsRecordNew)
	}
}

// Verifies a run reports the build-settings comparison of its plan in its result.
func TestRunReportsTheBuildSettingsComparison(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeBuildStamp(t, projectRoot)

	result, err := Run(context.Background(), Options{ProjectRoot: projectRoot, EditorExecutablePath: writeFakeEditor(t)})
	if err != nil {
		t.Fatalf("Run failed: %v", err)
	}

	if result.BuildSettings.Record != buildSettingsRecordNew {
		t.Errorf("record = %q, want %q", result.BuildSettings.Record, buildSettingsRecordNew)
	}
}
