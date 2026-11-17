package dispatcher

import (
	"fmt"
	"os"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

// Pins the package default to "no token": GitHub API tests here must not read
// the developer's GITHUB_TOKEN or consult the gh CLI, or whether a request
// carries an Authorization header would depend on the machine running the tests.
//
// Self-update is switched off by default because the periodic self-update runs
// on almost every command: a test that drives runDispatcherWithDeps without
// stubbing the update would otherwise reach GitHub. Tests that exercise the
// self-update clear the variable themselves. The config directory points at a
// throwaway directory so no test can write a version hold into the developer's
// real configuration, and the cache directory likewise so no test takes or leaves
// the real update lock.
func TestMain(m *testing.M) {
	githubapi.DefaultTokenSource = githubapi.StaticTokenSource("")
	os.Exit(runPackageTests(m))
}

func runPackageTests(m *testing.M) int {
	configDir, err := os.MkdirTemp("", "uloop-dispatcher-test-config-")
	if err != nil {
		fmt.Fprintf(os.Stderr, "create test config dir: %v\n", err)
		return 1
	}
	defer func() {
		_ = os.RemoveAll(configDir)
	}()
	if err := os.Setenv(nativepath.ConfigDirEnvName, configDir); err != nil {
		fmt.Fprintf(os.Stderr, "set %s: %v\n", nativepath.ConfigDirEnvName, err)
		return 1
	}
	cacheDir, err := os.MkdirTemp("", "uloop-dispatcher-test-cache-")
	if err != nil {
		fmt.Fprintf(os.Stderr, "create test cache dir: %v\n", err)
		return 1
	}
	defer func() {
		_ = os.RemoveAll(cacheDir)
	}()
	if err := os.Setenv(nativepath.CacheDirEnvName, cacheDir); err != nil {
		fmt.Fprintf(os.Stderr, "set %s: %v\n", nativepath.CacheDirEnvName, err)
		return 1
	}
	if err := os.Setenv(dispatcherDisableSelfUpdateEnvName, "1"); err != nil {
		fmt.Fprintf(os.Stderr, "set %s: %v\n", dispatcherDisableSelfUpdateEnvName, err)
		return 1
	}
	return m.Run()
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

// enableSelfUpdateForTest lifts the package-wide self-update switch for one test
// that exercises the self-update itself.
func enableSelfUpdateForTest(t *testing.T) {
	t.Helper()
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "")
}

// runFromInstallDir makes deps report that the dispatcher runs from its install dir, the only
// place a required update can replace it.
func runFromInstallDir(deps dispatcherRunDeps) dispatcherRunDeps {
	deps.resolveSelfUpdateLocation = func() (string, string, error) {
		return "/install/uloop", "/install", nil
	}
	return deps
}
