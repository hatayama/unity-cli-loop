/**
 * Compile-related helpers live in the CLI because the removed TypeScript server
 * is no longer a shared source boundary.
 */

// CLI tools output to console by design, object keys come from Unity tool responses which are trusted,
// and lock file paths are constructed from trusted project root detection
/* eslint-disable security/detect-object-injection, security/detect-non-literal-fs-filename */

import assert from 'node:assert';
import { existsSync, readFileSync } from 'fs';
import { join } from 'path';

// Only alphanumeric, underscore, and hyphen — blocks path separators and traversal sequences
const SAFE_REQUEST_ID_PATTERN: RegExp = /^[a-zA-Z0-9_-]+$/;

const COMPILE_FORCE_RECOMPILE_ARG_KEYS = [
  'ForceRecompile',
  'forceRecompile',
  'force_recompile',
  'force-recompile',
] as const;

const COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS = [
  'WaitForDomainReload',
  'waitForDomainReload',
  'wait_for_domain_reload',
  'wait-for-domain-reload',
] as const;

export interface CompileExecutionOptions {
  forceRecompile: boolean;
  waitForDomainReload: boolean;
}

interface CompileCompletionWaitOptions {
  projectRoot: string;
  requestId: string;
  timeoutMs: number;
  pollIntervalMs: number;
  isUnityReadyWhenIdle?: () => Promise<boolean>;
}

type CompileCompletionOutcome = 'completed' | 'timed_out';

interface CompileCompletionResult<T> {
  outcome: CompileCompletionOutcome;
  result?: T;
}

/**
 * Between compilationFinished and beforeAssemblyReload, there is a brief gap (~50ms measured)
 * where no lock files exist. This grace period prevents false "completed" detection during that gap.
 */
const LOCK_GRACE_PERIOD_MS = 500;

function toBoolean(value: unknown): boolean {
  if (typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'string') {
    return value.toLowerCase() === 'true';
  }

  return false;
}

function getCompileBooleanArg(args: Record<string, unknown>, keys: readonly string[]): boolean {
  for (const key of keys) {
    if (!(key in args)) {
      continue;
    }

    return toBoolean(args[key]);
  }

  return false;
}

export function resolveCompileExecutionOptions(
  args: Record<string, unknown>,
): CompileExecutionOptions {
  return {
    forceRecompile: getCompileBooleanArg(args, COMPILE_FORCE_RECOMPILE_ARG_KEYS),
    waitForDomainReload: getCompileBooleanArg(args, COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS),
  };
}

function createCompileRequestId(): string {
  const timestamp = Date.now();
  const randomToken = Math.floor(Math.random() * 1000000)
    .toString()
    .padStart(6, '0');
  return `compile_${timestamp}_${randomToken}`;
}

export function ensureCompileRequestId(args: Record<string, unknown>): string {
  const existingRequestId = args['RequestId'];
  if (typeof existingRequestId === 'string' && existingRequestId.length > 0) {
    if (SAFE_REQUEST_ID_PATTERN.test(existingRequestId)) {
      return existingRequestId;
    }
  }

  const requestId: string = createCompileRequestId();
  args['RequestId'] = requestId;
  return requestId;
}

function getCompileResultFilePath(projectRoot: string, requestId: string): string {
  assert(
    SAFE_REQUEST_ID_PATTERN.test(requestId),
    `requestId contains unsafe characters: '${requestId}'`,
  );
  return join(projectRoot, 'Temp', 'uLoopMCP', 'compile-results', `${requestId}.json`);
}

function isUnityBusyByLockFiles(projectRoot: string): boolean {
  const compilingLockPath = join(projectRoot, 'Temp', 'compiling.lock');
  if (existsSync(compilingLockPath)) {
    return true;
  }

  const domainReloadLockPath = join(projectRoot, 'Temp', 'domainreload.lock');
  return existsSync(domainReloadLockPath);
}

function stripUtf8Bom(content: string): string {
  if (content.charCodeAt(0) === 0xfeff) {
    return content.slice(1);
  }

  return content;
}

function tryReadCompileResult<T>(projectRoot: string, requestId: string): T | undefined {
  const resultFilePath = getCompileResultFilePath(projectRoot, requestId);
  if (!existsSync(resultFilePath)) {
    return undefined;
  }

  try {
    const content = readFileSync(resultFilePath, 'utf-8');
    const parsed = JSON.parse(stripUtf8Bom(content)) as unknown;
    return parsed as T;
  } catch {
    // File may be partially written by Unity or deleted between existsSync and readFileSync (TOCTOU).
    // Return undefined so the polling loop retries on the next tick.
    return undefined;
  }
}

export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Wait until the compile result file exists AND all Unity lock files are gone.
 * After the result file appears and locks disappear, waits an additional grace period
 * to catch the gap between compilationFinished and beforeAssemblyReload (~50ms measured).
 */
export async function waitForCompileCompletion<T>(
  options: CompileCompletionWaitOptions,
): Promise<CompileCompletionResult<T>> {
  const startTime: number = Date.now();
  let idleSinceTimestamp: number | null = null;

  while (Date.now() - startTime < options.timeoutMs) {
    const result: T | undefined = tryReadCompileResult<T>(options.projectRoot, options.requestId);
    const isBusy: boolean = isUnityBusyByLockFiles(options.projectRoot);

    if (result !== undefined && !isBusy) {
      const now: number = Date.now();
      if (idleSinceTimestamp === null) {
        idleSinceTimestamp = now;
      }

      const idleDuration: number = now - idleSinceTimestamp;
      if (idleDuration >= LOCK_GRACE_PERIOD_MS) {
        if (options.isUnityReadyWhenIdle) {
          const isReady: boolean = await options.isUnityReadyWhenIdle();
          if (isReady) {
            return { outcome: 'completed', result };
          }
        } else {
          return { outcome: 'completed', result };
        }
      }
    } else {
      idleSinceTimestamp = null;
    }

    await sleep(options.pollIntervalMs);
  }

  const lastResult: T | undefined = tryReadCompileResult<T>(options.projectRoot, options.requestId);
  if (lastResult !== undefined && !isUnityBusyByLockFiles(options.projectRoot)) {
    // Guard the compilationFinished→beforeAssemblyReload gap, same as the main loop
    await sleep(LOCK_GRACE_PERIOD_MS);
    if (isUnityBusyByLockFiles(options.projectRoot)) {
      return { outcome: 'timed_out' };
    }

    if (options.isUnityReadyWhenIdle) {
      const isReady: boolean = await options.isUnityReadyWhenIdle();
      if (isReady) {
        return { outcome: 'completed', result: lastResult };
      }
    } else {
      return { outcome: 'completed', result: lastResult };
    }
  }

  return { outcome: 'timed_out' };
}
