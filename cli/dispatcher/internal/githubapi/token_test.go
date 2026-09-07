package githubapi

import (
	"context"
	"errors"
	"testing"
	"time"
)

// failingRun fails the test when the gh CLI is consulted, for cases where an
// environment token must short-circuit the lookup.
func failingRun(t *testing.T) func(context.Context, string, ...string) ([]byte, error) {
	t.Helper()
	return func(context.Context, string, ...string) ([]byte, error) {
		t.Fatal("the gh CLI must not be consulted when the environment carries a token")
		return nil, nil
	}
}

func foundGhPath(string) (string, error) {
	return "/usr/local/bin/gh", nil
}

// Verifies GITHUB_TOKEN wins over GH_TOKEN so environments that set both behave consistently.
func TestTokenResolverPrefersGitHubTokenEnv(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "env-primary")
	t.Setenv("GH_TOKEN", "env-secondary")
	resolver := newTokenResolverWithCommands(foundGhPath, failingRun(t))

	if token := resolver.Resolve(context.Background()); token != "env-primary" {
		t.Fatalf("token mismatch: got %q", token)
	}
}

// Verifies GH_TOKEN is used when GITHUB_TOKEN is empty.
func TestTokenResolverFallsBackToGHTokenEnv(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "env-secondary")
	resolver := newTokenResolverWithCommands(foundGhPath, failingRun(t))

	if token := resolver.Resolve(context.Background()); token != "env-secondary" {
		t.Fatalf("token mismatch: got %q", token)
	}
}

// Verifies an empty environment borrows the token the gh CLI is logged in with, invoking `gh auth token`.
func TestTokenResolverBorrowsGhCliTokenWhenEnvIsEmpty(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	var invokedName string
	var invokedArgs []string
	resolver := newTokenResolverWithCommands(foundGhPath, func(_ context.Context, name string, args ...string) ([]byte, error) {
		invokedName = name
		invokedArgs = args
		return []byte("gho_fake\n"), nil
	})

	if token := resolver.Resolve(context.Background()); token != "gho_fake" {
		t.Fatalf("token mismatch: got %q", token)
	}
	if invokedName != "/usr/local/bin/gh" {
		t.Fatalf("expected the resolved gh path, got %q", invokedName)
	}
	if len(invokedArgs) != 2 || invokedArgs[0] != "auth" || invokedArgs[1] != "token" {
		t.Fatalf("argument mismatch: got %v", invokedArgs)
	}
}

// Verifies the gh CLI call is bounded by a deadline so a keyring prompt cannot hang the command.
func TestTokenResolverBoundsGhWithTimeout(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	resolver := newTokenResolverWithCommands(foundGhPath, func(ctx context.Context, _ string, _ ...string) ([]byte, error) {
		deadline, ok := ctx.Deadline()
		if !ok {
			t.Fatal("the gh CLI call must carry a deadline")
			return nil, nil
		}
		remaining := time.Until(deadline)
		if remaining <= 0 || remaining > ghTokenTimeout {
			t.Fatalf("deadline out of range: %v", remaining)
		}
		return []byte("gho_fake\n"), nil
	})

	if token := resolver.Resolve(context.Background()); token != "gho_fake" {
		t.Fatalf("token mismatch: got %q", token)
	}
}

// Verifies a machine without the gh CLI installed falls back to anonymous requests.
func TestTokenResolverReturnsEmptyWhenGhIsMissing(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	resolver := newTokenResolverWithCommands(
		func(string) (string, error) { return "", errors.New("not found") },
		failingRun(t),
	)

	if token := resolver.Resolve(context.Background()); token != "" {
		t.Fatalf("expected no token, got %q", token)
	}
}

// Verifies a failing `gh auth token` (not logged in, locked keyring) is treated as no token rather than an error.
func TestTokenResolverReturnsEmptyWhenGhFails(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	resolver := newTokenResolverWithCommands(foundGhPath, func(context.Context, string, ...string) ([]byte, error) {
		return nil, errors.New("not logged in")
	})

	if token := resolver.Resolve(context.Background()); token != "" {
		t.Fatalf("expected no token, got %q", token)
	}
}

// Verifies unexpected multi-line gh output is discarded instead of being sent as a bearer token.
func TestTokenResolverRejectsMultilineGhOutput(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	resolver := newTokenResolverWithCommands(foundGhPath, func(context.Context, string, ...string) ([]byte, error) {
		return []byte("a\nb\n"), nil
	})

	if token := resolver.Resolve(context.Background()); token != "" {
		t.Fatalf("expected no token, got %q", token)
	}
}

// Verifies the gh CLI is consulted once per process even though several API calls ask for the token.
func TestTokenResolverResolvesOnce(t *testing.T) {
	t.Setenv("GITHUB_TOKEN", "")
	t.Setenv("GH_TOKEN", "")
	runCount := 0
	resolver := newTokenResolverWithCommands(foundGhPath, func(context.Context, string, ...string) ([]byte, error) {
		runCount++
		return []byte("gho_fake\n"), nil
	})

	for range 3 {
		if token := resolver.Resolve(context.Background()); token != "gho_fake" {
			t.Fatalf("token mismatch: got %q", token)
		}
	}
	if runCount != 1 {
		t.Fatalf("expected a single gh invocation, got %d", runCount)
	}
}

// Verifies the test-only static source returns exactly what it was configured with.
func TestStaticTokenSourceReturnsConfiguredToken(t *testing.T) {
	if token := StaticTokenSource("x").Resolve(context.Background()); token != "x" {
		t.Fatalf("token mismatch: got %q", token)
	}
	if token := StaticTokenSource("").Resolve(context.Background()); token != "" {
		t.Fatalf("expected no token, got %q", token)
	}
}
