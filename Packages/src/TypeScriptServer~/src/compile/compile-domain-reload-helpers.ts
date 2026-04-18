import assert from 'node:assert';
import { existsSync, readFileSync } from 'fs';
import * as net from 'net';
import { join } from 'path';

// Only alphanumeric, underscore, and hyphen — blocks path separators and traversal sequences
const SAFE_REQUEST_ID_PATTERN: RegExp = /^[a-zA-Z0-9_-]+$/;

export const COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS = [
  'WaitForDomainReload',
  'waitForDomainReload',
  'wait_for_domain_reload',
  'wait-for-domain-reload',
] as const;

type CompileCompletionOutcome = 'completed' | 'timed_out';

/**
 * Between compilationFinished and beforeAssemblyReload, there is a brief gap (~50ms measured)
 * where no lock files exist. This grace period prevents false "completed" detection during that gap.
 */
const LOCK_GRACE_PERIOD_MS = 500;

/**
 * After TcpListener.Start() the OS accepts TCP connections, but ServerLoopAsync
 * may not have called AcceptTcpClientAsync() yet. Requests arriving in this gap
 * get a TCP connection but no response. canSendRequestToUnity() verifies that
 * the server can actually process a request, not just accept a TCP connection.
 */

interface CompileCompletionWaitOptions {
  projectRoot: string;
  requestId: string;
  timeoutMs: number;
  pollIntervalMs: number;
  unityPort?: number;
  isUnityReadyWhenIdle?: () => Promise<boolean>;
}

interface CompileCompletionResult<T> {
  outcome: CompileCompletionOutcome;
  result?: T;
}

function toBoolean(value: unknown): boolean {
  if (typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'string') {
    return value.toLowerCase() === 'true';
  }

  return false;
}

export function getCompileBooleanArg(
  args: Record<string, unknown>,
  keys: readonly string[],
): boolean {
  for (const key of keys) {
    if (!(key in args)) {
      continue;
    }

    return toBoolean(args[key]);
  }

  return false;
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

/**
 * Wait until the compile result file exists AND the compile/domain-reload lock files are gone.
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
        if (options.unityPort !== undefined) {
          const isReady: boolean = await canSendRequestToUnity(options.unityPort);
          if (isReady) {
            return { outcome: 'completed', result };
          }
        } else if (options.isUnityReadyWhenIdle) {
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

    if (options.unityPort !== undefined) {
      const isReady: boolean = await canSendRequestToUnity(options.unityPort);
      if (isReady) {
        return { outcome: 'completed', result: lastResult };
      }
    } else if (options.isUnityReadyWhenIdle) {
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

const READINESS_CHECK_TIMEOUT_MS = 3000;
const DEFAULT_HOST = '127.0.0.1';
const CONTENT_LENGTH_HEADER = 'Content-Length:';
const HEADER_SEPARATOR = '\r\n\r\n';

/**
 * Verify Unity server can actually process a JSON-RPC request, not just accept TCP.
 * Sends a lightweight get-tool-details request and waits for any valid framed response.
 */
function canSendRequestToUnity(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    const timer = setTimeout(() => {
      socket.destroy();
      resolve(false);
    }, READINESS_CHECK_TIMEOUT_MS);

    const cleanup = (): void => {
      clearTimeout(timer);
      socket.destroy();
    };

    socket.connect(port, DEFAULT_HOST, () => {
      const rpcRequest: string = JSON.stringify({
        jsonrpc: '2.0',
        method: 'get-tool-details',
        params: { IncludeDevelopmentOnly: false },
        id: 0,
      });
      const contentLength: number = Buffer.byteLength(rpcRequest, 'utf8');
      const frame: string = `${CONTENT_LENGTH_HEADER} ${contentLength}${HEADER_SEPARATOR}${rpcRequest}`;
      socket.write(frame);
    });

    let buffer: Buffer = Buffer.alloc(0);
    socket.on('data', (chunk: Buffer) => {
      buffer = Buffer.concat([buffer, chunk]);
      const sepIndex: number = buffer.indexOf(HEADER_SEPARATOR);
      if (sepIndex !== -1) {
        cleanup();
        resolve(true);
      }
    });

    socket.on('error', () => {
      cleanup();
      resolve(false);
    });

    socket.on('close', () => {
      clearTimeout(timer);
      resolve(false);
    });
  });
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
