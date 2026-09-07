package dispatcher

import (
	"os"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
)

// Pins the package default to "no token": GitHub API tests here must not read
// the developer's GITHUB_TOKEN or consult the gh CLI, or whether a request
// carries an Authorization header would depend on the machine running the tests.
func TestMain(m *testing.M) {
	githubapi.DefaultTokenSource = githubapi.StaticTokenSource("")
	os.Exit(m.Run())
}

// useTokenSource pins the token every api.github.com call in this package sees,
// and restores the previous source when the test ends.
func useTokenSource(t *testing.T, token string) {
	t.Helper()
	previous := githubapi.DefaultTokenSource
	githubapi.DefaultTokenSource = githubapi.StaticTokenSource(token)
	t.Cleanup(func() {
		githubapi.DefaultTokenSource = previous
	})
}
