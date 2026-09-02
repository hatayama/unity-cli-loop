// Package githubapi holds GitHub REST API response handling shared by the
// dispatcher's attestation and self-update paths.
package githubapi

import (
	"net/http"
	"strconv"
	"time"
)

const (
	headerRateLimitRemaining = "X-RateLimit-Remaining"
	headerRateLimitReset     = "X-RateLimit-Reset"
	headerRetryAfter         = "Retry-After"
	resetTimeLayout          = "15:04 MST"
)

// TokenNextAction tells the user how to move from the shared anonymous quota
// to their own authenticated one.
const TokenNextAction = "Set GH_TOKEN (or GITHUB_TOKEN) to a GitHub token so uloop uses your authenticated API quota, then retry."

// RateLimitError reports that GitHub refused a REST API request because the
// caller's request quota is exhausted. Anonymous quota is shared by every
// machine behind the same public IP, so this is a first-run failure in
// offices and on CI runners rather than a misconfiguration.
type RateLimitError struct {
	// ResetAt is when GitHub will accept requests again; zero when unknown.
	ResetAt time.Time
}

func (e RateLimitError) Error() string {
	message := "GitHub API rate limit exhausted (anonymous requests share a per-IP quota)"
	if e.ResetAt.IsZero() {
		return message
	}
	return message + "; resets at " + e.ResetAt.Local().Format(resetTimeLayout)
}

// NextActions returns user-facing recovery steps for the error envelope.
func (e RateLimitError) NextActions() []string {
	actions := []string{TokenNextAction}
	if e.ResetAt.IsZero() {
		return actions
	}
	return append(actions, "Or retry after "+e.ResetAt.Local().Format(resetTimeLayout)+" when the anonymous quota resets.")
}

// DetectRateLimit classifies a non-2xx response. GitHub signals an exhausted
// primary quota with 403 plus X-RateLimit-Remaining: 0, and a secondary
// (abuse) limit with 429 plus Retry-After; every other refusal is left to the
// caller's generic handling so a real permission problem is not mislabeled.
func DetectRateLimit(response *http.Response) (RateLimitError, bool) {
	if response.StatusCode != http.StatusForbidden && response.StatusCode != http.StatusTooManyRequests {
		return RateLimitError{}, false
	}
	remaining := response.Header.Get(headerRateLimitRemaining)
	retryAfter := response.Header.Get(headerRetryAfter)
	if remaining != "0" && retryAfter == "" {
		return RateLimitError{}, false
	}
	return RateLimitError{ResetAt: resetTime(response.Header.Get(headerRateLimitReset), retryAfter)}, true
}

func resetTime(resetHeader string, retryAfterHeader string) time.Time {
	if resetUnix, err := strconv.ParseInt(resetHeader, 10, 64); err == nil {
		return time.Unix(resetUnix, 0)
	}
	if retryAfterSeconds, err := strconv.ParseInt(retryAfterHeader, 10, 64); err == nil {
		return time.Now().Add(time.Duration(retryAfterSeconds) * time.Second).Truncate(time.Second)
	}
	return time.Time{}
}
