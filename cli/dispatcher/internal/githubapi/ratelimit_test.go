package githubapi

import (
	"errors"
	"io"
	"net/http"
	"strconv"
	"strings"
	"testing"
	"time"
)

func responseWith(status int, headers map[string]string) *http.Response {
	return responseWithBody(status, headers, "")
}

func responseWithBody(status int, headers map[string]string, body string) *http.Response {
	response := &http.Response{StatusCode: status, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(body))}
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

	rateLimit, ok := DetectRateLimit(response, false)
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

	if _, ok := DetectRateLimit(response, false); ok {
		t.Fatalf("expected no rate limit detection for a forbidden response with quota left")
	}
}

// Verifies a 429 secondary limit is detected and Retry-After seconds become the reset time.
func TestDetectRateLimitReportsSecondaryLimitFromRetryAfter(t *testing.T) {
	response := responseWith(http.StatusTooManyRequests, map[string]string{
		"Retry-After": "90",
	})

	before := time.Now()
	rateLimit, ok := DetectRateLimit(response, false)
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

	if _, ok := DetectRateLimit(response, false); ok {
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

// Verifies NextActions offers the token hint first, then the reset-time retry when known or the wait hint otherwise.
func TestNextActionsIncludeTokenHintAndResetRetry(t *testing.T) {
	withReset := RateLimitError{ResetAt: time.Date(2026, 9, 2, 10, 30, 0, 0, time.Local)}.NextActions()
	if len(withReset) != 2 || !strings.Contains(withReset[0], "GH_TOKEN") || !strings.Contains(withReset[1], "10:30") {
		t.Fatalf("unexpected next actions with reset: %v", withReset)
	}

	withoutReset := RateLimitError{}.NextActions()
	if len(withoutReset) != 2 || !strings.Contains(withoutReset[0], "GH_TOKEN") || !strings.Contains(withoutReset[1], "minute") {
		t.Fatalf("unexpected next actions without reset: %v", withoutReset)
	}
}

// Verifies a 429 carrying both headers takes Retry-After, not the unrelated primary reset time.
func TestDetectRateLimitPrefersRetryAfterForSecondaryLimit(t *testing.T) {
	primaryReset := time.Now().Add(50 * time.Minute).Truncate(time.Second)
	response := responseWith(http.StatusTooManyRequests, map[string]string{
		"Retry-After":       "60",
		"X-RateLimit-Reset": strconv.FormatInt(primaryReset.Unix(), 10),
	})

	rateLimit, ok := DetectRateLimit(response, false)
	if !ok {
		t.Fatalf("expected rate limit detection for 429")
	}
	if !rateLimit.ResetAt.Before(time.Now().Add(2 * time.Minute)) {
		t.Fatalf("expected Retry-After based reset about 60s ahead, got %v", rateLimit.ResetAt)
	}
}

// Verifies a headerless secondary-limit body is still classified for both 403 and 429.
func TestDetectRateLimitRecognizesSecondaryLimitBody(t *testing.T) {
	body := `{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}`
	for _, status := range []int{http.StatusForbidden, http.StatusTooManyRequests} {
		response := responseWithBody(status, map[string]string{"X-RateLimit-Remaining": "4990"}, body)

		rateLimit, ok := DetectRateLimit(response, false)
		if !ok {
			t.Fatalf("expected rate limit detection from body for status %d", status)
		}
		if !rateLimit.ResetAt.IsZero() {
			t.Fatalf("expected unknown reset time without headers, got %v", rateLimit.ResetAt)
		}
	}
}

// Verifies a 403 whose body is an ordinary permission message stays a generic refusal.
func TestDetectRateLimitIgnoresForbiddenWithUnrelatedBody(t *testing.T) {
	response := responseWithBody(http.StatusForbidden, map[string]string{}, `{"message":"Resource not accessible by integration"}`)

	if _, ok := DetectRateLimit(response, true); ok {
		t.Fatalf("expected no rate limit detection for an unrelated 403 body")
	}
}

// Verifies the authenticated flag is carried into the error so callers can tailor guidance.
func TestDetectRateLimitCarriesAuthenticationState(t *testing.T) {
	response := responseWith(http.StatusForbidden, map[string]string{"X-RateLimit-Remaining": "0"})

	rateLimit, ok := DetectRateLimit(response, true)
	if !ok || !rateLimit.Authenticated {
		t.Fatalf("expected an authenticated rate limit error, got ok=%t %+v", ok, rateLimit)
	}
}

// Verifies an authenticated exhaustion never suggests setting a token and names the reset when known.
func TestNextActionsForAuthenticatedRequestsSkipTokenHint(t *testing.T) {
	withReset := RateLimitError{ResetAt: time.Date(2026, 9, 2, 10, 30, 0, 0, time.Local), Authenticated: true}.NextActions()
	if len(withReset) != 1 || strings.Contains(withReset[0], "GH_TOKEN") || !strings.Contains(withReset[0], "10:30") {
		t.Fatalf("unexpected authenticated next actions with reset: %v", withReset)
	}

	withoutReset := RateLimitError{Authenticated: true}.NextActions()
	if len(withoutReset) != 1 || strings.Contains(withoutReset[0], "GH_TOKEN") || !strings.Contains(withoutReset[0], "minute") {
		t.Fatalf("unexpected authenticated next actions without reset: %v", withoutReset)
	}
	if !strings.Contains(RateLimitError{Authenticated: true}.Error(), "configured token") {
		t.Fatalf("authenticated message should name the token quota")
	}
}

// Verifies the displayed reset time includes the calendar date so a reset past midnight is unambiguous.
func TestRateLimitErrorMessageIncludesResetDate(t *testing.T) {
	message := RateLimitError{ResetAt: time.Date(2026, 9, 3, 0, 15, 0, 0, time.Local)}.Error()
	if !strings.Contains(message, "2026-09-03 00:15") {
		t.Fatalf("expected dated reset time, got: %s", message)
	}
}
