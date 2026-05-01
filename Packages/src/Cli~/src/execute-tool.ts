/**
 * Tool execution logic for CLI.
 * Handles dynamic tool execution by connecting to Unity and sending requests.
 */

// CLI tools output to console by design, object keys come from Unity tool responses which are trusted,
// and lock file paths are constructed from trusted project root detection
/* eslint-disable no-console, security/detect-object-injection, security/detect-non-literal-fs-filename */

import { PRODUCT_DISPLAY_NAME } from './cli-constants';
import * as readline from 'readline';
import { spawnSync } from 'child_process';
import { existsSync, statSync, Stats } from 'fs';
import { join } from 'path';
import { DirectUnityClient } from './direct-unity-client.js';
import {
  type ResolvedUnityConnection,
  resolveUnityConnection,
  UnityNotRunningError,
  UnityServerNotRunningError,
  validateProjectPath,
} from './port-resolver.js';
import { validateConnectedProject } from './project-validator.js';
import { saveToolsCache, getCacheFilePath, ToolsCache, ToolDefinition } from './tool-cache.js';
import { isToolEnabled } from './tool-settings-loader.js';
import { VERSION } from './version.js';
import { createSpinner } from './spinner.js';
import { findUnityProjectRoot } from './project-root.js';
import { getCliProcessAgeMilliseconds } from './process-timing.js';
import { isRetryableFastProjectValidationErrorMessage } from './request-metadata.js';
import { findRunningUnityProcessForProject } from './unity-process.js';
import {
  type CompileExecutionOptions,
  ensureCompileRequestId,
  resolveCompileExecutionOptions,
  sleep,
  waitForCompileCompletion,
} from './compile-helpers.js';

/**
 * Suppress stdin echo during async operation to prevent escape sequences from being displayed.
 * Returns a cleanup function to restore stdin state.
 */
function suppressStdinEcho(): () => void {
  if (!process.stdin.isTTY) {
    return () => {};
  }

  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false,
  });

  process.stdin.setRawMode(true);
  process.stdin.resume();

  const onData = (data: Buffer): void => {
    // Ctrl+C (0x03) should trigger process exit
    if (data[0] === 0x03) {
      process.exit(130);
    }
  };
  process.stdin.on('data', onData);

  return () => {
    process.stdin.off('data', onData);
    process.stdin.setRawMode(false);
    process.stdin.pause();
    rl.close();
  };
}

export interface GlobalOptions {
  projectPath?: string;
}

interface SpinnerHandle {
  update(message: string): void;
  stop(): void;
}

interface OutputSanitizerOptions {
  exposeServerVersion?: boolean;
}

export function stripInternalFields(
  result: Record<string, unknown>,
  options: OutputSanitizerOptions = {},
): Record<string, unknown> {
  const cleaned = { ...result };
  delete cleaned['ProjectRoot'];
  if (options.exposeServerVersion !== true) {
    delete cleaned['Ver'];
  }
  return cleaned;
}

const RETRY_DELAY_MS = 500;
const MAX_RETRIES = 3;
const SERVER_STARTING_LOCK_STALE_THRESHOLD_MS = 30000;
const COMPILE_WAIT_TIMEOUT_MS = 90000;
const COMPILE_WAIT_POLL_INTERVAL_MS = 100;
const FORCE_COMPILE_INDETERMINATE_MESSAGE_PREFIX = 'Force compilation executed.';
const POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE =
  'UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }';
const POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE =
  'using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { Debug.unityLogger.filterLogType = previous; }';
// Why: the fully-qualified probe keeps post-reload warmup stable while startup state is still
// settling, but measurements showed the first user-visible `using UnityEngine; Debug.Log(...)`
// request still paid a separate cold path unless we warmed that wrapper shape too.
// Why not warm only the stable probe: that avoids startup failures, but it still leaves the first
// real request noticeably slower than the warmed steady state.
const POST_COMPILE_DYNAMIC_CODE_PREWARM_CODES = [
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE,
];
// Why: launch readiness and startup auto-prewarm already cover Unity-side initialization, but
// measurements on Unity 2022.3 still showed the first CLI execute-dynamic-code request paying
// one extra cold pass until a real user-like CLI request had run once.
// Why not reuse the full post-compile sequence here: launch only needs to warm the isolated CLI
// boundary one final time, and extra hidden passes would just add restart latency after the editor
// is already ready.
const POST_LAUNCH_DYNAMIC_CODE_PREWARM_CODES = [POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE];
const POST_COMPILE_DYNAMIC_CODE_PREWARM_DELAY_MS = 500;
// Why: four passes now have to survive startup noise plus at least one retryable failure without
// turning a successful compile into a warmup false negative.
// Why not keep the old total budget of six: that was tuned for three passes and started failing
// once the new user-like warmup consumed the remaining retries.
const POST_COMPILE_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS = 10;
const POST_LAUNCH_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS = 3;
const POST_COMPILE_DYNAMIC_CODE_PREWARM_TIMEOUT_MS = 5000;
const POST_COMPILE_DYNAMIC_CODE_PREWARM_COMPILATION_PROVIDER_SUBSTRINGS = ['warming up'];
const POST_COMPILE_DYNAMIC_CODE_PREWARM_UNITY_ERROR_SUBSTRINGS = [
  'can only be called from the main thread',
];
const POST_COMPILE_DYNAMIC_CODE_PREWARM_TRANSIENT_ERROR_SUBSTRINGS = [
  'internal error',
  'preusingresolver.resolve',
  'system.nullreferenceexception',
];
const SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY = 'ULOOP_INTERNAL_SKIP_SERVER_STARTING_BUSY_CHECK';
const EXECUTION_IN_PROGRESS_ERROR_MESSAGE = 'Another execution is already in progress';
const EXECUTION_CANCELLED_ERROR_MESSAGE = 'Execution was cancelled or timed out';
const POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE = 'Request timed out';

interface PostCompileDynamicCodePrewarmDependencies {
  spawnCliProcess: (args: string[]) => {
    status: number | null;
    error?: Error;
    stdout?: string;
    stderr?: string;
  };
}

interface PostCompileDynamicCodePrewarmTarget {
  projectRoot?: string;
}

const defaultPostCompileDynamicCodePrewarmDependencies: PostCompileDynamicCodePrewarmDependencies =
  {
    spawnCliProcess: (args: string[]) =>
      spawnSync(process.execPath, [process.argv[1], ...args], {
        stdio: ['ignore', 'pipe', 'pipe'],
        encoding: 'utf8',
        timeout: POST_COMPILE_DYNAMIC_CODE_PREWARM_TIMEOUT_MS,
        windowsHide: true,
        env: {
          ...process.env,
          [SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY]: '1',
        },
      }),
  };

interface ConnectionFailureDiagnosisDependencies {
  findRunningUnityProcessForProjectFn: typeof findRunningUnityProcessForProject;
  existsSyncFn?: typeof existsSync;
  statSyncFn?: typeof statSync;
}

const defaultConnectionFailureDiagnosisDependencies: ConnectionFailureDiagnosisDependencies = {
  findRunningUnityProcessForProjectFn: findRunningUnityProcessForProject,
  existsSyncFn: existsSync,
  statSyncFn: statSync,
};

function getCompileExecutionOptions(
  toolName: string,
  params: Record<string, unknown>,
): CompileExecutionOptions {
  if (toolName !== 'compile') {
    return {
      forceRecompile: false,
      waitForDomainReload: false,
    };
  }

  return resolveCompileExecutionOptions(params);
}

function isRetryableError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }
  const message = error.message;
  return (
    message.includes('ECONNREFUSED') ||
    message.includes('EADDRNOTAVAIL') ||
    message.includes('ENOENT') ||
    message === 'UNITY_NO_RESPONSE'
  );
}

export async function diagnoseRetryableProjectConnectionError(
  error: unknown,
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
): Promise<unknown> {
  if (!shouldDiagnoseProjectState || projectRoot === null || !isRetryableError(error)) {
    return error;
  }

  const runningProcess = await dependencies
    .findRunningUnityProcessForProjectFn(projectRoot)
    .catch(() => undefined);

  if (runningProcess === undefined) {
    return error;
  }

  if (runningProcess === null) {
    return new UnityNotRunningError(projectRoot);
  }

  return new UnityServerNotRunningError(projectRoot);
}

export async function shouldRetryWhenUnityProcessIsRunning(
  error: unknown,
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
): Promise<boolean> {
  if (
    !shouldDiagnoseProjectState ||
    projectRoot === null ||
    !isRetryableProjectRecoveryError(error)
  ) {
    return false;
  }

  const runningProcess = await dependencies
    .findRunningUnityProcessForProjectFn(projectRoot)
    .catch(() => undefined);
  return runningProcess !== null && runningProcess !== undefined;
}

function isRetryableProjectRecoveryError(error: unknown): boolean {
  if (isRetryableError(error)) {
    return true;
  }

  if (!(error instanceof Error)) {
    return false;
  }

  return isRetryableFastProjectValidationErrorMessage(error.message);
}

export async function resolveRecoveryConnectionOrKeepCurrent(
  currentConnection: ResolvedUnityConnection,
  projectPath: string | undefined,
  resolveUnityConnectionFn: typeof resolveUnityConnection = resolveUnityConnection,
): Promise<ResolvedUnityConnection> {
  try {
    return await resolveUnityConnectionFn(projectPath);
  } catch {
    if (currentConnection.requestMetadata === null || currentConnection.projectRoot === null) {
      return currentConnection;
    }

    return {
      ...currentConnection,
      requestMetadata: null,
      shouldValidateProject: true,
    };
  }
}

function isServerStarting(
  projectRoot: string | null,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
): boolean {
  if (projectRoot === null) {
    return false;
  }

  const serverStartingLockPath = join(projectRoot, 'Temp', 'serverstarting.lock');
  const existsSyncFn = dependencies.existsSyncFn ?? existsSync;
  const statSyncFn = dependencies.statSyncFn ?? statSync;

  if (!existsSyncFn(serverStartingLockPath)) {
    return false;
  }

  try {
    const lockStat: Stats = statSyncFn(serverStartingLockPath);
    return Date.now() - lockStat.mtimeMs <= SERVER_STARTING_LOCK_STALE_THRESHOLD_MS;
  } catch {
    return false;
  }
}

export function isSettingsReadError(error: unknown): boolean {
  return (
    error instanceof Error &&
    error.message.startsWith('Could not read Unity server session from settings.')
  );
}

export async function shouldReportServerStarting(
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
): Promise<boolean> {
  if (
    !shouldDiagnoseProjectState ||
    !isServerStarting(projectRoot, dependencies) ||
    projectRoot === null
  ) {
    return false;
  }

  const runningProcess = await dependencies
    .findRunningUnityProcessForProjectFn(projectRoot)
    .catch(() => undefined);
  return runningProcess !== null && runningProcess !== undefined;
}

export async function shouldPromoteToServerStartingError(
  error: unknown,
  toolName: string,
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
): Promise<boolean> {
  if (!shouldPromoteServerStartingResolutionFailures(toolName)) {
    return false;
  }

  if (!isRetryableProjectRecoveryError(error) && !isSettingsReadError(error)) {
    return false;
  }

  return shouldReportServerStarting(projectRoot, shouldDiagnoseProjectState, dependencies);
}

export async function resolveUnityConnectionWithStartupDiagnosis(
  toolName: string,
  projectPath: string | undefined,
  dependencies: ConnectionFailureDiagnosisDependencies = defaultConnectionFailureDiagnosisDependencies,
  resolveUnityConnectionFn: typeof resolveUnityConnection = resolveUnityConnection,
): Promise<ResolvedUnityConnection> {
  try {
    return await resolveUnityConnectionFn(projectPath);
  } catch (error) {
    if (!isRetryableProjectRecoveryError(error) && !isSettingsReadError(error)) {
      throw error;
    }

    const projectRoot =
      projectPath !== undefined ? validateProjectPath(projectPath) : findUnityProjectRoot();
    if (
      await shouldPromoteToServerStartingError(error, toolName, projectRoot, true, dependencies)
    ) {
      throw createServerStartingError(error);
    }

    throw error;
  }
}

async function throwFinalToolError(
  error: unknown,
  toolName: string,
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
): Promise<never> {
  if (
    await shouldPromoteToServerStartingError(
      error,
      toolName,
      projectRoot,
      shouldDiagnoseProjectState,
    )
  ) {
    throw createServerStartingError(error);
  }

  const diagnosedError = await diagnoseRetryableProjectConnectionError(
    error,
    projectRoot,
    shouldDiagnoseProjectState,
  );

  if (diagnosedError instanceof Error) {
    throw diagnosedError;
  }

  if (typeof diagnosedError === 'string') {
    throw new Error(diagnosedError);
  }

  const serializedError = JSON.stringify(diagnosedError);
  throw new Error(serializedError ?? 'Unknown error');
}

function createServerStartingError(cause: unknown): Error {
  if (cause instanceof Error) {
    return new Error('UNITY_SERVER_STARTING', { cause });
  }

  return new Error('UNITY_SERVER_STARTING');
}

// Distinct from isRetryableError(): that function covers pre-connection failures
// (for example ECONNREFUSED, EADDRNOTAVAIL, or ENOENT) which cannot occur after dispatch.
// This function covers post-dispatch transport failures where Unity may have received
// the request but the response was lost — file-based recovery is appropriate.
export function isTransportDisconnectError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }
  const message: string = error.message;
  return message === 'UNITY_NO_RESPONSE' || message.startsWith('Connection lost:');
}

function tryParseRequestTotalMilliseconds(timings: unknown): number | undefined {
  if (!Array.isArray(timings)) {
    return undefined;
  }

  const prefix = '[Perf] RequestTotal: ';
  const suffix = 'ms';
  for (const timingEntry of timings) {
    if (typeof timingEntry !== 'string') {
      continue;
    }

    if (!timingEntry.startsWith(prefix) || !timingEntry.endsWith(suffix)) {
      continue;
    }

    const numericText = timingEntry.slice(prefix.length, -suffix.length);
    const parsedMilliseconds = Number.parseFloat(numericText);
    if (Number.isNaN(parsedMilliseconds)) {
      continue;
    }

    return parsedMilliseconds;
  }

  return undefined;
}

export function appendCliTimingsToDynamicCodeResult(
  result: Record<string, unknown>,
  cliTotalMilliseconds: number,
  cliProcessTotalMilliseconds: number,
): void {
  const timings = result['Timings'];
  if (!Array.isArray(timings)) {
    return;
  }

  timings.push(`[Perf] CliTotal: ${cliTotalMilliseconds.toFixed(1)}ms`);
  timings.push(`[Perf] CliProcessTotal: ${cliProcessTotalMilliseconds.toFixed(1)}ms`);
  timings.push(
    `[Perf] CliBootstrap: ${Math.max(0, cliProcessTotalMilliseconds - cliTotalMilliseconds).toFixed(1)}ms`,
  );

  const requestTotalMilliseconds = tryParseRequestTotalMilliseconds(timings);
  if (requestTotalMilliseconds === undefined) {
    return;
  }

  const cliOverheadMilliseconds = Math.max(0, cliTotalMilliseconds - requestTotalMilliseconds);
  timings.push(`[Perf] CliOverhead: ${cliOverheadMilliseconds.toFixed(1)}ms`);
}

export function shouldPrewarmDynamicCodeAfterCompile(result: Record<string, unknown>): boolean {
  const success = result['Success'];
  const errorCount = result['ErrorCount'];
  if (success === true && errorCount === 0) {
    return true;
  }

  const message = result['Message'];
  // Why: force-recompile + wait-for-domain-reload intentionally persists an indeterminate
  // response because Unity cannot summarize compiler messages until after reload.
  // Why not gate the hidden prewarm on Success===true: that would skip the exact domain-reload
  // path we need to warm, leaving the next user-visible execute-dynamic-code request cold.
  return (
    success === null &&
    errorCount === 0 &&
    typeof message === 'string' &&
    message.startsWith(FORCE_COMPILE_INDETERMINATE_MESSAGE_PREFIX)
  );
}

export async function prewarmDynamicCodeAfterCompile(
  target: PostCompileDynamicCodePrewarmTarget,
  dependencies: PostCompileDynamicCodePrewarmDependencies = defaultPostCompileDynamicCodePrewarmDependencies,
): Promise<void> {
  await prewarmDynamicCodeWithIsolatedCli(
    target,
    POST_COMPILE_DYNAMIC_CODE_PREWARM_CODES,
    POST_COMPILE_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS,
    dependencies,
  );
}

export async function prewarmDynamicCodeAfterLaunch(
  target: PostCompileDynamicCodePrewarmTarget,
  dependencies: PostCompileDynamicCodePrewarmDependencies = defaultPostCompileDynamicCodePrewarmDependencies,
): Promise<void> {
  await prewarmDynamicCodeWithIsolatedCli(
    target,
    POST_LAUNCH_DYNAMIC_CODE_PREWARM_CODES,
    POST_LAUNCH_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS,
    dependencies,
  );
}

async function prewarmDynamicCodeWithIsolatedCli(
  target: PostCompileDynamicCodePrewarmTarget,
  codes: readonly string[],
  maxTotalAttemptCount: number,
  dependencies: PostCompileDynamicCodePrewarmDependencies,
): Promise<void> {
  let totalAttemptCount = 0;

  for (const code of codes) {
    const args = createPostCompileDynamicCodePrewarmArgs(target, code);
    let lastError: Error | undefined;

    while (totalAttemptCount < maxTotalAttemptCount) {
      if (totalAttemptCount > 0) {
        await sleep(POST_COMPILE_DYNAMIC_CODE_PREWARM_DELAY_MS);
      }
      totalAttemptCount++;
      const prewarmResult = dependencies.spawnCliProcess(args);
      if (didPostCompileDynamicCodePrewarmSucceed(prewarmResult)) {
        lastError = undefined;
        break;
      }

      lastError = createPostCompileDynamicCodePrewarmError(prewarmResult);
      if (!isRetryablePostCompileDynamicCodePrewarmError(lastError)) {
        break;
      }
    }

    if (lastError !== undefined) {
      throw lastError;
    }
  }
}

function createPostCompileDynamicCodePrewarmArgs(
  target: PostCompileDynamicCodePrewarmTarget,
  code: string,
): string[] {
  if (target.projectRoot !== undefined) {
    return [
      'execute-dynamic-code',
      '--code',
      code,
      '--yield-to-foreground-requests',
      'true',
      '--project-path',
      target.projectRoot,
    ];
  }

  throw new Error('Post-compile dynamic code prewarm requires a project path.');
}

function didPostCompileDynamicCodePrewarmSucceed(result: {
  status: number | null;
  stdout?: string;
}): boolean {
  if (result.status !== 0) {
    return false;
  }

  if (typeof result.stdout !== 'string' || result.stdout.trim().length === 0) {
    return false;
  }

  const parsed = tryParsePostCompileDynamicCodePrewarmStdout(result.stdout);
  if (parsed === undefined) {
    return false;
  }

  return parsed['Success'] === true;
}

function createPostCompileDynamicCodePrewarmError(result: {
  status: number | null;
  error?: Error;
  stdout?: string;
  stderr?: string;
}): Error {
  if (result.error !== undefined) {
    return result.error;
  }

  if (typeof result.stderr === 'string') {
    const stderrError = tryParsePostCompileDynamicCodePrewarmStderr(result.stderr);
    if (stderrError !== undefined) {
      return stderrError;
    }
  }

  if (result.status === 0 && typeof result.stdout === 'string' && result.stdout.trim().length > 0) {
    const parsed = tryParsePostCompileDynamicCodePrewarmStdout(result.stdout);
    if (parsed !== undefined) {
      const errorMessage = parsed['ErrorMessage'];
      if (typeof errorMessage === 'string' && errorMessage.length > 0) {
        return new Error(errorMessage);
      }
    }
  }

  return new Error('Post-compile dynamic code prewarm failed.');
}

function stripAnsiControlSequences(text: string): string {
  let result = '';
  let isInsideEscapeSequence = false;

  for (const character of text) {
    if (character === String.fromCharCode(27)) {
      isInsideEscapeSequence = true;
      continue;
    }

    if (isInsideEscapeSequence) {
      const code = character.charCodeAt(0);
      if (code >= 0x40 && code <= 0x7e) {
        isInsideEscapeSequence = false;
      }
      continue;
    }

    result += character;
  }

  return result;
}

function tryParsePostCompileDynamicCodePrewarmStderr(stderr: string): Error | undefined {
  const normalizedStderr = stripAnsiControlSequences(stderr).replace(/\r/g, '');

  if (normalizedStderr.includes(EXECUTION_IN_PROGRESS_ERROR_MESSAGE)) {
    return new Error(EXECUTION_IN_PROGRESS_ERROR_MESSAGE);
  }

  if (normalizedStderr.includes(EXECUTION_CANCELLED_ERROR_MESSAGE)) {
    return new Error(EXECUTION_CANCELLED_ERROR_MESSAGE);
  }

  if (normalizedStderr.includes('Unity server is starting')) {
    return new Error('UNITY_SERVER_STARTING');
  }

  if (
    normalizedStderr.includes('UNITY_DOMAIN_RELOAD') ||
    normalizedStderr.includes('Unity is reloading (Domain Reload in progress)')
  ) {
    return new Error('UNITY_DOMAIN_RELOAD');
  }

  if (
    normalizedStderr.includes('UNITY_COMPILING') ||
    normalizedStderr.includes('Unity is compiling scripts')
  ) {
    return new Error('UNITY_COMPILING');
  }

  if (
    normalizedStderr.includes('UNITY_NO_RESPONSE') ||
    normalizedStderr.includes('Cannot connect to Unity')
  ) {
    return new Error('UNITY_NO_RESPONSE');
  }

  const connectionLostPrefix = 'Connection lost:';
  const connectionLostIndex = normalizedStderr.indexOf(connectionLostPrefix);
  if (connectionLostIndex >= 0) {
    const connectionLostLine = normalizedStderr.slice(connectionLostIndex).split('\n')[0].trim();
    return new Error(connectionLostLine);
  }

  if (normalizedStderr.includes(POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE)) {
    return new Error(POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE);
  }

  return undefined;
}

function tryParsePostCompileDynamicCodePrewarmStdout(
  stdout: string,
): Record<string, unknown> | undefined {
  try {
    return JSON.parse(stdout) as Record<string, unknown>;
  } catch {
    return undefined;
  }
}

function isRetryablePostCompileDynamicCodePrewarmError(error: Error): boolean {
  return (
    error.message === EXECUTION_IN_PROGRESS_ERROR_MESSAGE ||
    error.message === EXECUTION_CANCELLED_ERROR_MESSAGE ||
    error.message === POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE ||
    error.message === 'UNITY_SERVER_STARTING' ||
    error.message === 'UNITY_DOMAIN_RELOAD' ||
    error.message === 'UNITY_COMPILING' ||
    isRetryablePostCompileDynamicCodePrewarmDisconnect(error) ||
    isRetryablePostCompileDynamicCodePrewarmSpawnError(error) ||
    isRetryableCompilationProviderUnavailable(error.message) ||
    isRetryableUnityStartupMainThreadError(error.message) ||
    isRetryablePostCompileDynamicCodeTransientError(error.message)
  );
}

function isRetryablePostCompileDynamicCodePrewarmDisconnect(error: Error): boolean {
  return error.message === 'UNITY_NO_RESPONSE' || error.message.startsWith('Connection lost:');
}

function isRetryablePostCompileDynamicCodePrewarmSpawnError(error: Error): boolean {
  return error.message.includes('ETIMEDOUT') || error.message.toLowerCase().includes('timed out');
}

function isRetryableCompilationProviderUnavailable(errorMessage: string): boolean {
  if (!errorMessage.startsWith('COMPILATION_PROVIDER_UNAVAILABLE:')) {
    return false;
  }

  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_COMPILATION_PROVIDER_SUBSTRINGS.some((substring) =>
    normalizedMessage.includes(substring),
  );
}

function isRetryableUnityStartupMainThreadError(errorMessage: string): boolean {
  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_UNITY_ERROR_SUBSTRINGS.some((substring) =>
    normalizedMessage.includes(substring),
  );
}

function isRetryablePostCompileDynamicCodeTransientError(errorMessage: string): boolean {
  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_TRANSIENT_ERROR_SUBSTRINGS.some((substring) =>
    normalizedMessage.includes(substring),
  );
}

/**
 * Print project-local CLI version mismatch warning to stderr.
 * Does not block execution - just warns the user.
 */
function printVersionWarning(cliVersion: string, serverVersion: string): void {
  console.error('\x1b[33m⚠️ Version mismatch detected!\x1b[0m');
  console.error(`   Project CLI version:  ${cliVersion}`);
  console.error(`   Unity package version:${serverVersion}`);
  console.error('');
  console.error('   This may cause unexpected behavior or errors.');
  console.error('');
  console.error(
    `   Reopen Unity or reload ${PRODUCT_DISPLAY_NAME} so the package can refresh the project-local CLI.`,
  );
  console.error('');
}

/**
 * Check server version from response and print warning if mismatched.
 */
function checkServerVersion(result: Record<string, unknown>): void {
  const serverVersion = result['Ver'] as string | undefined;
  if (serverVersion && serverVersion !== VERSION) {
    printVersionWarning(VERSION, serverVersion);
  }
}

function formatToolOutput(
  toolName: string,
  result: Record<string, unknown>,
): Record<string, unknown> {
  return stripInternalFields(result, { exposeServerVersion: toolName === 'get-version' });
}

/**
 * Check if Unity is in a busy state (compiling or reloading).
 * Throws an error with appropriate message if busy.
 */
function shouldTreatServerStartingAsBusy(toolName: string): boolean {
  return toolName === 'execute-dynamic-code';
}

// Why: non-dynamic tools should stop pre-blocking on serverstarting.lock once the listener is up,
// but fresh-lock resolution failures still need the retryable UNITY_SERVER_STARTING surface.
// Why not reuse shouldTreatServerStartingAsBusy for both paths: that collapses two different
// contracts and turns normal startup resolution failures back into raw config/connection errors.
function shouldPromoteServerStartingResolutionFailures(_toolName: string): boolean {
  return true;
}

async function checkUnityBusyState(toolName: string, projectPath?: string): Promise<void> {
  const projectRoot =
    projectPath !== undefined ? validateProjectPath(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    return;
  }

  if (
    shouldTreatServerStartingAsBusy(toolName) &&
    !shouldSkipServerStartingBusyCheck() &&
    (await shouldReportServerStarting(projectRoot, true))
  ) {
    throw new Error('UNITY_SERVER_STARTING');
  }

  const compilingLock = join(projectRoot, 'Temp', 'compiling.lock');
  if (existsSync(compilingLock)) {
    throw new Error('UNITY_COMPILING');
  }

  const domainReloadLock = join(projectRoot, 'Temp', 'domainreload.lock');
  if (existsSync(domainReloadLock)) {
    throw new Error('UNITY_DOMAIN_RELOAD');
  }
}

function shouldSkipServerStartingBusyCheck(): boolean {
  return process.env[SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY] === '1';
}

async function checkUnityBusyStateBeforeProjectResolution(
  toolName: string,
  globalOptions: GlobalOptions,
): Promise<void> {
  await checkUnityBusyState(toolName, globalOptions.projectPath);
}

function shouldShowInteractiveFeedback(toolName: string): boolean {
  return toolName !== 'execute-dynamic-code';
}

function createNoopSpinner(): SpinnerHandle {
  return {
    update: (_message: string): void => {},
    stop: (): void => {},
  };
}

function noop(): void {}

export async function executeToolCommand(
  toolName: string,
  params: Record<string, unknown>,
  globalOptions: GlobalOptions,
): Promise<void> {
  const commandStartedAt: number = Date.now();
  await checkUnityBusyStateBeforeProjectResolution(toolName, globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    toolName,
    globalOptions.projectPath,
  );
  const compileOptions = getCompileExecutionOptions(toolName, params);
  const shouldWaitForDomainReload = compileOptions.waitForDomainReload;
  const compileRequestId = shouldWaitForDomainReload ? ensureCompileRequestId(params) : undefined;

  // execute-dynamic-code is latency-sensitive enough that spinner setup costs more than the
  // feedback is worth, and skipping stdin suppression avoids extra TTY setup on the hot path.
  const shouldShowFeedback = shouldShowInteractiveFeedback(toolName);
  const restoreStdin: () => void = shouldShowFeedback ? suppressStdinEcho() : noop;
  const spinner = shouldShowFeedback
    ? createSpinner('Connecting to Unity...')
    : createNoopSpinner();
  let didCleanup = false;
  const cleanup = (): void => {
    if (didCleanup) {
      return;
    }

    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };

  try {
    let lastError: unknown;
    let immediateResult: Record<string, unknown> | undefined;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;

    // Monotonically-increasing flag: once true, retries cannot reset it to false.
    // The retry loop overwrites `lastError` and `immediateResult` on each attempt,
    // which destroys the evidence of whether an earlier attempt successfully dispatched
    // the request to Unity. This flag preserves that information across retries.
    // See: git log cb3d63e..HEAD for the history of oscillating fixes caused by
    // inferring dispatch status from `immediateResult` alone.
    let requestDispatched = false;

    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution(toolName, globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;

      const client = new DirectUnityClient(connection.endpoint);
      try {
        await client.connect();

        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }

        spinner.update(`Executing ${toolName}...`);
        // connect() succeeded: socket is established. sendRequest() calls socket.write()
        // synchronously (direct-unity-client.ts:136), so the data reaches the kernel
        // send buffer before any async error can occur. Safe to mark as dispatched here.
        requestDispatched = true;
        const result = await client.sendRequest<Record<string, unknown>>(toolName, params, {
          requestMetadata: connection.requestMetadata ?? undefined,
        });

        if (result === undefined || result === null) {
          throw new Error('UNITY_NO_RESPONSE');
        }

        immediateResult = result;
        if (!shouldWaitForDomainReload) {
          cleanup();

          if (toolName === 'execute-dynamic-code') {
            appendCliTimingsToDynamicCodeResult(
              result,
              Date.now() - commandStartedAt,
              getCliProcessAgeMilliseconds(),
            );
          }
          checkServerVersion(result);
          console.log(JSON.stringify(formatToolOutput(toolName, result), null, 2));
          return;
        }

        break;
      } catch (error) {
        lastError = error;
        client.disconnect();

        // After a compile request has been dispatched, retrying is counterproductive:
        // the next loop iteration calls checkUnityBusyState() OUTSIDE the try block,
        // which throws UNITY_DOMAIN_RELOAD during domain reload and escapes the
        // entire function — bypassing waitForCompileCompletion() recovery.
        if (requestDispatched && shouldWaitForDomainReload) {
          if (isTransportDisconnectError(error)) {
            // Unity may have received the request before the connection dropped.
            // Break out of retry loop → proceed to file-based recovery below.
            spinner.update('Connection lost during compile. Waiting for result file...');
            break;
          }
          // JSON-RPC error (e.g. "Unity error: ..."): Unity processed the request
          // and returned an explicit error. No result file will be written
          // (confirmed: CompileUseCase.ExecuteAsync() is not reached when
          // JSON-RPC error occurs at parameter validation / security check).
          cleanup();
          throw error instanceof Error ? error : new Error(String(error));
        }

        if (
          await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)
        ) {
          spinner.update('Unity Editor is running, waiting for CLI Loop server to recover...');
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryConnectionOrKeepCurrent(
            connection,
            globalOptions.projectPath,
          );
          continue;
        }

        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update('Retrying connection...');
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }

    if (shouldWaitForDomainReload && compileRequestId) {
      // Fail fast when the compile request never reached Unity.
      // Without this guard, unreachable Unity would cause a 90-second wait for a
      // result file that will never be created.
      // We check both conditions because:
      //  - immediateResult === undefined: no JSON-RPC response was received
      //  - !requestDispatched: no attempt ever successfully connected and called sendRequest()
      // If requestDispatched is true but immediateResult is undefined, the request was sent
      // but the connection dropped before the response arrived (domain reload scenario).
      // In that case, Unity may have already written the result file, so we proceed to
      // file-based polling recovery.
      if (immediateResult === undefined && !requestDispatched) {
        cleanup();
        if (lastError !== undefined) {
          await throwFinalToolError(
            lastError,
            toolName,
            currentProjectRoot,
            currentShouldDiagnoseProjectState,
          );
        }
        throw new Error(
          'Compile request never reached Unity. Check that Unity is running and retry.',
        );
      }

      const projectRootFromUnity: string | undefined =
        immediateResult !== undefined
          ? (immediateResult['ProjectRoot'] as string | undefined)
          : undefined;
      const effectiveProjectRoot: string | null = projectRootFromUnity ?? currentProjectRoot;

      // File-based polling requires a known project root
      if (effectiveProjectRoot === null) {
        cleanup();
        if (immediateResult !== undefined) {
          checkServerVersion(immediateResult);
          console.log(JSON.stringify(formatToolOutput(toolName, immediateResult), null, 2));
          return;
        }
        if (lastError instanceof Error) {
          throw lastError;
        }
        throw new Error(
          'Compile request failed and project root is unknown. Check connection and retry.',
        );
      }

      spinner.update('Waiting for domain reload to complete...');
      const { outcome, result: storedResult } = await waitForCompileCompletion<
        Record<string, unknown>
      >({
        projectRoot: effectiveProjectRoot,
        requestId: compileRequestId,
        timeoutMs: COMPILE_WAIT_TIMEOUT_MS,
        pollIntervalMs: COMPILE_WAIT_POLL_INTERVAL_MS,
      });

      if (outcome === 'timed_out') {
        lastError = new Error(
          `Compile wait timed out after ${COMPILE_WAIT_TIMEOUT_MS}ms. Run 'uloop fix' and retry.`,
        );
      } else {
        const finalResult = storedResult ?? immediateResult;
        if (finalResult !== undefined) {
          if (toolName === 'compile' && shouldPrewarmDynamicCodeAfterCompile(finalResult)) {
            // Why: one hidden execute-dynamic-code request after domain reload warms the same
            // isolated CLI process boundary and Debug.Log path that the next user-visible dynamic
            // execution will use.
            // Why not force the hidden warmup when execute-dynamic-code is disabled: compile must
            // stay usable in projects that intentionally turn that tool off, and running a hidden
            // child command would turn a successful compile into a configuration error.
            if (isToolEnabled('execute-dynamic-code', effectiveProjectRoot)) {
              // Why not swallow warmup failures here: wait-for-domain-reload is the contract that the
              // next dynamic code request is usable, so returning success before this probe completes
              // would report a ready editor while the first execute-dynamic-code can still fail.
              spinner.update('Finalizing dynamic code warmup...');
              await prewarmDynamicCodeAfterCompile({
                projectRoot: effectiveProjectRoot,
              });
            }
          }

          cleanup();
          if (toolName === 'execute-dynamic-code') {
            appendCliTimingsToDynamicCodeResult(
              finalResult,
              Date.now() - commandStartedAt,
              getCliProcessAgeMilliseconds(),
            );
          }
          checkServerVersion(finalResult);
          console.log(JSON.stringify(formatToolOutput(toolName, finalResult), null, 2));
          return;
        }
      }
    }

    cleanup();
    if (lastError === undefined) {
      throw new Error('Tool execution failed without error details.');
    }
    await throwFinalToolError(
      lastError,
      toolName,
      currentProjectRoot,
      currentShouldDiagnoseProjectState,
    );
  } finally {
    cleanup();
  }
}

export async function listAvailableTools(globalOptions: GlobalOptions): Promise<void> {
  await checkUnityBusyStateBeforeProjectResolution('get-tool-details', globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    'get-tool-details',
    globalOptions.projectPath,
  );

  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner('Connecting to Unity...');
  let didCleanup = false;
  const cleanup = (): void => {
    if (didCleanup) {
      return;
    }

    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };

  try {
    let lastError: unknown;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution('get-tool-details', globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;

      const client = new DirectUnityClient(connection.endpoint);
      try {
        await client.connect();

        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }

        spinner.update('Fetching tool list...');
        const result = await client.sendRequest<{
          Tools: Array<{ name: string; description: string }>;
        }>(
          'get-tool-details',
          { IncludeDevelopmentOnly: false },
          { requestMetadata: connection.requestMetadata ?? undefined },
        );

        if (!result.Tools || !Array.isArray(result.Tools)) {
          throw new Error('Unexpected response from Unity: missing Tools array');
        }

        // Success - stop spinner and output result
        cleanup();
        for (const tool of result.Tools) {
          console.log(`  - ${tool.name}`);
        }
        return;
      } catch (error) {
        lastError = error;
        client.disconnect();

        if (
          await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)
        ) {
          spinner.update('Unity Editor is running, waiting for CLI Loop server to recover...');
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryConnectionOrKeepCurrent(
            connection,
            globalOptions.projectPath,
          );
          continue;
        }

        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update('Retrying connection...');
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }

    cleanup();
    await throwFinalToolError(
      lastError,
      'get-tool-details',
      currentProjectRoot,
      currentShouldDiagnoseProjectState,
    );
  } finally {
    cleanup();
  }
}

interface UnityToolInfo {
  name: string;
  description: string;
  parameterSchema: {
    Properties: Record<string, UnityPropertyInfo>;
    Required?: string[];
  };
}

interface UnityPropertyInfo {
  Type: string;
  Description?: string;
  DefaultValue?: unknown;
  Enum?: string[] | null;
}

function convertProperties(
  unityProps: Record<string, UnityPropertyInfo>,
): Record<string, ToolDefinition['inputSchema']['properties'][string]> {
  const result: Record<string, ToolDefinition['inputSchema']['properties'][string]> = {};
  for (const [key, prop] of Object.entries(unityProps)) {
    result[key] = {
      type: prop.Type?.toLowerCase() ?? 'string',
      description: prop.Description,
      default: prop.DefaultValue,
      enum: prop.Enum ?? undefined,
    };
  }
  return result;
}

export async function syncTools(globalOptions: GlobalOptions): Promise<void> {
  await checkUnityBusyStateBeforeProjectResolution('sync-tools', globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    'sync-tools',
    globalOptions.projectPath,
  );

  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner('Connecting to Unity...');
  let didCleanup = false;
  const cleanup = (): void => {
    if (didCleanup) {
      return;
    }

    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };

  try {
    let lastError: unknown;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution('sync-tools', globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;

      const client = new DirectUnityClient(connection.endpoint);
      try {
        await client.connect();

        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }

        spinner.update('Syncing tools...');
        const result = await client.sendRequest<{
          Tools: UnityToolInfo[];
          Ver?: string;
        }>(
          'get-tool-details',
          { IncludeDevelopmentOnly: false },
          { requestMetadata: connection.requestMetadata ?? undefined },
        );

        cleanup();
        if (!result.Tools || !Array.isArray(result.Tools)) {
          throw new Error('Unexpected response from Unity: missing Tools array');
        }

        const cache: ToolsCache = {
          version: VERSION,
          serverVersion: result.Ver,
          updatedAt: new Date().toISOString(),
          tools: result.Tools.map((tool) => ({
            name: tool.name,
            description: tool.description,
            inputSchema: {
              type: 'object',
              properties: convertProperties(tool.parameterSchema.Properties),
              required: tool.parameterSchema.Required,
            },
          })),
        };

        saveToolsCache(cache);

        console.log(`Synced ${cache.tools.length} tools to ${getCacheFilePath()}`);
        console.log('\nTools:');
        for (const tool of cache.tools) {
          console.log(`  - ${tool.name}`);
        }
        return;
      } catch (error) {
        lastError = error;
        client.disconnect();

        if (
          await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)
        ) {
          spinner.update('Unity Editor is running, waiting for CLI Loop server to recover...');
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryConnectionOrKeepCurrent(
            connection,
            globalOptions.projectPath,
          );
          continue;
        }

        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update('Retrying connection...');
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }

    cleanup();
    await throwFinalToolError(
      lastError,
      'sync-tools',
      currentProjectRoot,
      currentShouldDiagnoseProjectState,
    );
  } finally {
    cleanup();
  }
}
