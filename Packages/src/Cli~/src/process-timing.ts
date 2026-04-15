const CLI_PROCESS_STARTED_AT_MS = Date.now();

export function getCliProcessAgeMilliseconds(): number {
  return Date.now() - CLI_PROCESS_STARTED_AT_MS;
}
