// Package githubapi holds GitHub REST API response handling shared by the
// dispatcher's attestation and self-update paths.
package githubapi

import (
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"
)

const (
	headerRateLimitRemaining = "X-RateLimit-Remaining"
	headerRateLimitReset     = "X-RateLimit-Reset"
	headerRetryAfter         = "Retry-After"
	// Why date included: a reset shortly after midnight would otherwise read as earlier today.
	resetTimeLayout = "2006-01-02 15:04 MST"
	// Why bounded: the body is only inspected for GitHub's short JSON error message.
	maxInspectedBodyBytes = 4096
	// Why one minute: GitHub documents it as the minimum wait for a secondary limit without Retry-After.
	secondaryLimitFallbackWait = "Wait at least a minute, then retry; GitHub's secondary rate limit clears on its own."
)

// tokenNextAction tells the user how to move from the shared anonymous quota
// to their own authenticated one.
const tokenNextAction = "Set GH_TOKEN (or GITHUB_TOKEN) to a GitHub token so uloop uses your authenticated API quota, then retry."

// RateLimitError reports that GitHub refused a REST API request because the
// caller's request quota is exhausted. Anonymous quota is shared by every
// machine behind the same public IP, so this is a first-run failure in
// offices and on CI runners rather than a misconfiguration.
type RateLimitError struct {
	// ResetAt is when GitHub will accept requests again; zero when unknown.
	ResetAt time.Time
	// Authenticated is true when the refused request already carried a token,
	// so suggesting a token would not change anything.
	Authenticated bool
}

func (e RateLimitError) Error() string {
	message := "GitHub API rate limit exhausted (anonymous requests share a per-IP quota)"
	if e.Authenticated {
		message = "GitHub API rate limit exhausted for the configured token"
	}
	if e.ResetAt.IsZero() {
		return message
	}
	return message + "; resets at " + e.ResetAt.Local().Format(resetTimeLayout)
}

// NextActions returns user-facing recovery steps for the error envelope.
func (e RateLimitError) NextActions() []string {
	if e.Authenticated {
		if e.ResetAt.IsZero() {
			return []string{secondaryLimitFallbackWait}
		}
		return []string{"Retry after " + e.ResetAt.Local().Format(resetTimeLayout) + " when the token's quota resets."}
	}
	actions := []string{tokenNextAction}
	if e.ResetAt.IsZero() {
		// Why the wait hint: an unknown reset means a headerless secondary limit, which
		// clears on its own even without a token.
		return append(actions, "Or: "+secondaryLimitFallbackWait)
	}
	return append(actions, "Or retry after "+e.ResetAt.Local().Format(resetTimeLayout)+" when the anonymous quota resets.")
}

// DetectRateLimit classifies a non-2xx response. GitHub signals an exhausted
// primary quota with 403 plus X-RateLimit-Remaining: 0, a secondary (abuse)
// limit with 429 plus Retry-After, and sometimes a secondary limit with only
// its documented body message; every other refusal is left to the caller's
// generic handling so a real permission problem is not mislabeled.
// authenticated says whether the request carried a token, which decides the
// recovery guidance. On a positive match the response body has been consumed.
func DetectRateLimit(response *http.Response, authenticated bool) (RateLimitError, bool) {
	if response.StatusCode != http.StatusForbidden && response.StatusCode != http.StatusTooManyRequests {
		return RateLimitError{}, false
	}
	remaining := response.Header.Get(headerRateLimitRemaining)
	retryAfter := response.Header.Get(headerRetryAfter)
	if remaining != "0" && retryAfter == "" && !bodyReportsRateLimit(response.Body) {
		return RateLimitError{}, false
	}
	resetAt := resetTime(response.StatusCode, response.Header.Get(headerRateLimitReset), retryAfter)
	return RateLimitError{ResetAt: resetAt, Authenticated: authenticated}, true
}

// bodyReportsRateLimit recognizes GitHub's documented rate-limit messages when
// the headers are absent, which happens for some secondary-limit responses.
func bodyReportsRateLimit(body io.Reader) bool {
	if body == nil {
		return false
	}
	raw, err := io.ReadAll(io.LimitReader(body, maxInspectedBodyBytes))
	if err != nil {
		return false
	}
	text := strings.ToLower(string(raw))
	return strings.Contains(text, "secondary rate limit") || strings.Contains(text, "rate limit exceeded")
}

// resetTime prefers Retry-After for a 429 because a secondary limit clears on
// its own schedule, while a 403 primary exhaustion is timed by X-RateLimit-Reset.
func resetTime(statusCode int, resetHeader string, retryAfterHeader string) time.Time {
	fromReset := parseResetHeader(resetHeader)
	fromRetryAfter := parseRetryAfterHeader(retryAfterHeader)
	if statusCode == http.StatusTooManyRequests && !fromRetryAfter.IsZero() {
		return fromRetryAfter
	}
	if !fromReset.IsZero() {
		return fromReset
	}
	return fromRetryAfter
}

func parseResetHeader(resetHeader string) time.Time {
	resetUnix, err := strconv.ParseInt(resetHeader, 10, 64)
	if err != nil {
		return time.Time{}
	}
	return time.Unix(resetUnix, 0)
}

func parseRetryAfterHeader(retryAfterHeader string) time.Time {
	retryAfterSeconds, err := strconv.ParseInt(retryAfterHeader, 10, 64)
	if err != nil {
		return time.Time{}
	}
	return time.Now().Add(time.Duration(retryAfterSeconds) * time.Second).Truncate(time.Second)
}
