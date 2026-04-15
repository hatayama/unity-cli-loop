export interface UloopRequestMetadata {
  expectedProjectRoot: string;
  expectedServerSessionId: string;
}

const RETRYABLE_FAST_PROJECT_VALIDATION_ERROR_SUBSTRINGS = [
  'Fast project validation is unavailable.',
  'Unity CLI Loop server session changed.',
] as const;

export function isRetryableFastProjectValidationErrorMessage(message: string): boolean {
  return RETRYABLE_FAST_PROJECT_VALIDATION_ERROR_SUBSTRINGS.some((substring) =>
    message.includes(substring),
  );
}
