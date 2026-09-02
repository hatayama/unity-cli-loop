package githubapi

import (
	"errors"
	"net/http"
	"strconv"
	"strings"
	"testing"
	"time"
)

func responseWith(status int, headers map[string]string) *http.Response {
	response := &http.Response{StatusCode: status, Header: http.Header{}}
	for key, value := range headers {
		response.Header.Set(key, value)
	}
	return response
}

// Verifies a 403 with an exhausted primary quota is reported with the reset time from X-RateLimit-Reset.
func TestDetectRateLimitReportsExhaustedPrimaryQuota(t *testing.T) {
	resetAt := time.Now().Add(40 * time.Minute).Truncate(time.Second)
	response := responseWith(http.StatusForbidden, map[string]string{
		"X-RateLimit-Remaining": "0",
		"X-RateLimit-Reset":     strconv.FormatInt(resetAt.Unix(), 10),
	})

	rateLimit, ok := DetectRateLimit(response)
	if !ok {
		t.Fatalf("expected rate limit detection")
	}
	if !rateLimit.ResetAt.Equal(resetAt) {
		t.Fatalf("reset time mismatch: got %v want %v", rateLimit.ResetAt, resetAt)
	}
}

// Verifies a 403 that still has quota left is not classified as a rate limit.
func TestDetectRateLimitIgnoresForbiddenWithRemainingQuota(t *testing.T) {
	response := responseWith(http.StatusForbidden, map[string]string{
		"X-RateLimit-Remaining": "12",
	})

	if _, ok := DetectRateLimit(response); ok {
		t.Fatalf("expected no rate limit detection for a forbidden response with quota left")
	}
}

// Verifies a 429 secondary limit is detected and Retry-After seconds become the reset time.
func TestDetectRateLimitReportsSecondaryLimitFromRetryAfter(t *testing.T) {
	response := responseWith(http.StatusTooManyRequests, map[string]string{
		"Retry-After": "90",
	})

	before := time.Now()
	rateLimit, ok := DetectRateLimit(response)
	if !ok {
		t.Fatalf("expected rate limit detection for 429")
	}
	if rateLimit.ResetAt.Before(before.Add(89 * time.Second)) {
		t.Fatalf("expected reset about 90s ahead, got %v", rateLimit.ResetAt)
	}
}

// Verifies non-limit statuses are never classified as a rate limit even with limit headers present.
func TestDetectRateLimitIgnoresOtherStatuses(t *testing.T) {
	response := responseWith(http.StatusInternalServerError, map[string]string{
		"X-RateLimit-Remaining": "0",
	})

	if _, ok := DetectRateLimit(response); ok {
		t.Fatalf("expected no rate limit detection for a 500")
	}
}

// Verifies the error text names the quota problem and the local reset time, and survives wrapping.
func TestRateLimitErrorMessageAndUnwrapping(t *testing.T) {
	resetAt := time.Date(2026, 9, 2, 10, 30, 0, 0, time.Local)
	wrapped := errors.Join(errors.New("outer"), RateLimitError{ResetAt: resetAt})

	var rateLimit RateLimitError
	if !errors.As(wrapped, &rateLimit) {
		t.Fatalf("expected RateLimitError to be recoverable through errors.As")
	}
	message := rateLimit.Error()
	if !strings.Contains(message, "rate limit") || !strings.Contains(message, "10:30") {
		t.Fatalf("unexpected message: %s", message)
	}
}

// Verifies an unknown reset time produces a message without a bogus timestamp.
func TestRateLimitErrorMessageWithoutResetTime(t *testing.T) {
	message := RateLimitError{}.Error()
	if strings.Contains(message, "resets at") {
		t.Fatalf("message should not mention a reset time when unknown: %s", message)
	}
}

// Verifies NextActions offers the token hint first and the reset-time retry only when known.
func TestNextActionsIncludeTokenHintAndResetRetry(t *testing.T) {
	withReset := RateLimitError{ResetAt: time.Date(2026, 9, 2, 10, 30, 0, 0, time.Local)}.NextActions()
	if len(withReset) != 2 || !strings.Contains(withReset[0], "GH_TOKEN") || !strings.Contains(withReset[1], "10:30") {
		t.Fatalf("unexpected next actions with reset: %v", withReset)
	}

	withoutReset := RateLimitError{}.NextActions()
	if len(withoutReset) != 1 || !strings.Contains(withoutReset[0], "GH_TOKEN") {
		t.Fatalf("unexpected next actions without reset: %v", withoutReset)
	}
}
