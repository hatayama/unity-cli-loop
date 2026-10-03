package clierrors

import "testing"

// Verifies each project resolution error renders its message and maps to the
// project-not-found envelope with the context command.
func TestProjectResolveErrorsMapToProjectNotFound(t *testing.T) {
	cases := []struct {
		name string
		err  interface {
			error
			ToCLIError(ErrorContext) CLIError
		}
		message string
	}{
		{
			name:    "project not found",
			err:     ProjectNotFoundError{},
			message: "unity project not found. Use --project-path option to specify the target",
		},
		{
			name:    "multiple projects",
			err:     MultipleProjectsFoundError{SearchRoot: "<PROJECT_ROOT>"},
			message: "multiple Unity projects found under <PROJECT_ROOT>; use --project-path to choose one",
		},
		{
			name:    "not a Unity project",
			err:     NotUnityProjectError{ProjectRoot: "<PROJECT_ROOT>"},
			message: "not a Unity project: <PROJECT_ROOT>",
		},
		{
			name:    "not a Unity project with suggestion",
			err:     NotUnityProjectError{ProjectRoot: "/c/<PROJECT_ROOT>", Suggestion: "C:\\<PROJECT_ROOT>"},
			message: "not a Unity project: /c/<PROJECT_ROOT>. This looks like a WSL or Git Bash path. Did you mean: C:\\<PROJECT_ROOT>",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := testCase.err.ToCLIError(ErrorContext{Command: "compile"})

			if testCase.err.Error() != testCase.message {
				t.Fatalf("unexpected Error(): %q", testCase.err.Error())
			}
			if cliErr.ErrorCode != errorCodeProjectNotFound || cliErr.Phase != ErrorPhaseProjectResolve {
				t.Fatalf("unexpected classification: %#v", cliErr)
			}
			if cliErr.Message != testCase.message || cliErr.Command != "compile" {
				t.Fatalf("unexpected message or command: %#v", cliErr)
			}
			if len(cliErr.NextActions) != 2 {
				t.Fatalf("expected two recovery actions, got %#v", cliErr.NextActions)
			}
		})
	}
}
