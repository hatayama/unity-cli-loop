package githubapi

import (
	"context"
	"os"
	"os/exec"
	"strings"
	"sync"
	"time"
)

// ghTokenTimeout bounds `gh auth token`: a locked keyring makes gh prompt for a
// password, which would otherwise hang every uloop command that touches the
// GitHub API.
const ghTokenTimeout = 5 * time.Second

// ghExecutableName is looked up rather than executed by name so a missing gh
// install is detected before a process is started.
const ghExecutableName = "gh"

// TokenSource yields the bearer token for api.github.com calls, or "" for
// anonymous requests. It is an interface so tests in other packages can pin a
// fixed value instead of reading the developer's environment.
type TokenSource interface {
	Resolve(ctx context.Context) string
}

// DefaultTokenSource is shared by every api.github.com caller in the dispatcher
// so the gh CLI is consulted at most once per process, even though a single
// `uloop update` resolves both the release list and a tag's commit SHA.
var DefaultTokenSource TokenSource = NewTokenResolver()

// StaticTokenSource returns the same token on every call. Test-only: it lets
// other packages pin DefaultTokenSource without reaching into this one.
type StaticTokenSource string

func (s StaticTokenSource) Resolve(context.Context) string {
	return string(s)
}

// TokenResolver finds the GitHub token to authenticate API requests with:
// GITHUB_TOKEN, then GH_TOKEN, then the token the gh CLI is logged in with.
// GITHUB_TOKEN wins so CI environments that set both behave consistently, and
// the environment wins over the gh CLI so an explicit setting is never
// silently overridden by whichever account gh happens to hold.
type TokenResolver struct {
	lookPath func(file string) (string, error)
	run      func(ctx context.Context, name string, args ...string) ([]byte, error)
	once     sync.Once
	token    string
}

func NewTokenResolver() *TokenResolver {
	return newTokenResolverWithCommands(exec.LookPath, runCommandOutput)
}

func newTokenResolverWithCommands(
	lookPath func(file string) (string, error),
	run func(ctx context.Context, name string, args ...string) ([]byte, error),
) *TokenResolver {
	return &TokenResolver{lookPath: lookPath, run: run}
}

// runCommandOutput leaves stdin unconnected, so a gh subcommand that wants
// interactive input is cut off by the deadline instead of waiting on a terminal.
func runCommandOutput(ctx context.Context, name string, args ...string) ([]byte, error) {
	return exec.CommandContext(ctx, name, args...).Output()
}

// Resolve returns the token, or "" when none is available. A missing or
// unauthenticated gh CLI is an expected condition rather than an error: the
// caller simply falls back to an anonymous request, which is what uloop did
// before the gh CLI was consulted at all.
func (r *TokenResolver) Resolve(ctx context.Context) string {
	r.once.Do(func() {
		r.token = r.resolve(ctx)
	})
	return r.token
}

func (r *TokenResolver) resolve(ctx context.Context) string {
	for _, env := range []string{"GITHUB_TOKEN", "GH_TOKEN"} {
		if value := os.Getenv(env); value != "" {
			return value
		}
	}
	return r.resolveFromGhCLI(ctx)
}

func (r *TokenResolver) resolveFromGhCLI(ctx context.Context) string {
	ghPath, err := r.lookPath(ghExecutableName)
	if err != nil {
		return ""
	}
	ghCtx, cancel := context.WithTimeout(ctx, ghTokenTimeout)
	defer cancel()
	output, err := r.run(ghCtx, ghPath, "auth", "token")
	if err != nil {
		return ""
	}
	token := strings.TrimSpace(string(output))
	// Anything but a single word is not a token; sending broken output as a
	// bearer credential would turn a missing login into a confusing 401.
	if token == "" || strings.ContainsAny(token, " \t\r\n") {
		return ""
	}
	return token
}
