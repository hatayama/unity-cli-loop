/**
 * Tool execution logic for CLI.
 * Handles dynamic tool execution by connecting to Unity and sending requests.
 */

// CLI tools output to console by design, object keys come from Unity tool responses which are trusted,
// and lock file paths are constructed from trusted project root detection
/* eslint-disable no-console, security/detect-object-injection, security/detect-non-literal-fs-filename */

import { PRODUCT_DISPLAY_NAME } from './cli-constants';
import * as readline from 'readline';
import { existsSync } from 'fs';
import { join } from 'path';
import * as semver from 'semver';
import { DirectUnityClient } from './direct-unity-client.js';
import {
  resolveUnityPort,
  UnityNotRunningError,
  UnityServerNotRunningError,
  validateProjectPath,
} from './port-resolver.js';
import { validateConnectedProject } from './project-validator.js';
import { saveToolsCache, getCacheFilePath, ToolsCache, ToolDefinition } from './tool-cache.js';
import { VERSION } from './version.js';
import { createSpinner } from './spinner.js';
import { findUnityProjectRoot } from './project-root.js';
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
  port?: string;
  projectPath?: string;
}

function stripInternalFields(result: Record<string, unknown>): Record<string, unknown> {
  const cleaned = { ...result };
  delete cleaned['ProjectRoot'];
  return cleaned;
}

const RETRY_DELAY_MS = 500;
const MAX_RETRIES = 3;
const COMPILE_WAIT_TIMEOUT_MS = 90000;
const COMPILE_WAIT_POLL_INTERVAL_MS = 100;

interface ConnectionFailureDiagnosisDependencies {
  findRunningUnityProcessForProjectFn: typeof findRunningUnityProcessForProject;
}

const defaultConnectionFailureDiagnosisDependencies: ConnectionFailureDiagnosisDependencies = {
  findRunningUnityProcessForProjectFn: findRunningUnityProcessForProject,
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
  if (!isRetryableError(error) || !shouldDiagnoseProjectState || projectRoot === null) {
    return false;
  }

  const runningProcess = await dependencies.findRunningUnityProcessForProjectFn(projectRoot).catch(
    () => undefined,
  );
  return runningProcess !== null && runningProcess !== undefined;
}

export async function resolveRecoveryPortOrKeepCurrent(
  currentPort: number,
  explicitPort: number | undefined,
  projectPath: string | undefined,
  resolveUnityPortFn: typeof resolveUnityPort = resolveUnityPort,
): Promise<number> {
  if (explicitPort !== undefined) {
    return currentPort;
  }

  try {
    return await resolveUnityPortFn(undefined, projectPath);
  } catch {
    return currentPort;
  }
}

async function throwFinalToolError(
  error: unknown,
  projectRoot: string | null,
  shouldDiagnoseProjectState: boolean,
): Promise<never> {
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

// Distinct from isRetryableError(): that function covers pre-connection failures
// (ECONNREFUSED, EADDRNOTAVAIL) which cannot occur after dispatch.
// This function covers post-dispatch TCP failures where Unity may have received
// the request but the response was lost — file-based recovery is appropriate.
export function isTransportDisconnectError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }
  const message: string = error.message;
  return message === 'UNITY_NO_RESPONSE' || message.startsWith('Connection lost:');
}

/**
 * Compare two semantic versions safely.
 * Returns true if v1 < v2, false otherwise.
 * Falls back to string comparison if versions are invalid.
 */
export function isVersionOlder(v1: string, v2: string): boolean {
  const parsed1 = semver.valid(v1);
  const parsed2 = semver.valid(v2);

  if (parsed1 && parsed2) {
    return semver.lt(parsed1, parsed2);
  }

  return v1 < v2;
}

/**
 * Print version mismatch warning to stderr.
 * Does not block execution - just warns the user.
 */
function printVersionWarning(cliVersion: string, serverVersion: string): void {
  const isCliOlder = isVersionOlder(cliVersion, serverVersion);
  const updateCommand = isCliOlder
    ? `npm install -g uloop-cli@${serverVersion}`
    : `Update ${PRODUCT_DISPLAY_NAME} package to ${cliVersion} via Unity Package Manager`;

  console.error('\x1b[33m⚠️ Version mismatch detected!\x1b[0m');
  console.error(`   uloop-cli version:    ${cliVersion}`);
  console.error(`   uloop server version: ${serverVersion}`);
  console.error('');
  console.error('   This may cause unexpected behavior or errors.');
  console.error('');
  console.error(`   ${isCliOlder ? 'To update CLI:' : 'To update server:'} ${updateCommand}`);
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

/**
 * Check if Unity is in a busy state (compiling, reloading, or server starting).
 * Throws an error with appropriate message if busy.
 */
function checkUnityBusyState(projectPath?: string): void {
  const projectRoot =
    projectPath !== undefined ? validateProjectPath(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    return;
  }

  const compilingLock = join(projectRoot, 'Temp', 'compiling.lock');
  if (existsSync(compilingLock)) {
    throw new Error('UNITY_COMPILING');
  }

  const domainReloadLock = join(projectRoot, 'Temp', 'domainreload.lock');
  if (existsSync(domainReloadLock)) {
    throw new Error('UNITY_DOMAIN_RELOAD');
  }

  const serverStartingLock = join(projectRoot, 'Temp', 'serverstarting.lock');
  if (existsSync(serverStartingLock)) {
    throw new Error('UNITY_SERVER_STARTING');
  }
}

function checkUnityBusyStateBeforeProjectResolution(globalOptions: GlobalOptions): void {
  if (globalOptions.port !== undefined) {
    return;
  }

  checkUnityBusyState(globalOptions.projectPath);
}

export async function executeToolCommand(
  toolName: string,
  params: Record<string, unknown>,
  globalOptions: GlobalOptions,
): Promise<void> {
  let portNumber: number | undefined;
  if (globalOptions.port) {
    const parsed = parseInt(globalOptions.port, 10);
    if (isNaN(parsed)) {
      throw new Error(`Invalid port number: ${globalOptions.port}`);
    }
    portNumber = parsed;
  }
  checkUnityBusyStateBeforeProjectResolution(globalOptions);
  let port = await resolveUnityPort(portNumber, globalOptions.projectPath);
  const compileOptions = getCompileExecutionOptions(toolName, params);
  const shouldWaitForDomainReload = compileOptions.waitForDomainReload;
  const compileRequestId = shouldWaitForDomainReload ? ensureCompileRequestId(params) : undefined;

  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner('Connecting to Unity...');

  let lastError: unknown;
  let immediateResult: Record<string, unknown> | undefined;
  const projectRoot =
    globalOptions.projectPath !== undefined
      ? validateProjectPath(globalOptions.projectPath)
      : findUnityProjectRoot();

  // Validate project identity only when port was auto-resolved (not --port) and project root is known
  const shouldValidateProject = portNumber === undefined && projectRoot !== null;

  // Monotonically-increasing flag: once true, retries cannot reset it to false.
  // The retry loop overwrites `lastError` and `immediateResult` on each attempt,
  // which destroys the evidence of whether an earlier attempt successfully dispatched
  // the request to Unity. This flag preserves that information across retries.
  // See: git log cb3d63e..HEAD for the history of oscillating fixes caused by
  // inferring dispatch status from `immediateResult` alone.
  let requestDispatched = false;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    checkUnityBusyStateBeforeProjectResolution(globalOptions);

    const client = new DirectUnityClient(port);
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
      const result = await client.sendRequest<Record<string, unknown>>(toolName, params);

      if (result === undefined || result === null) {
        throw new Error('UNITY_NO_RESPONSE');
      }

      immediateResult = result;
      if (!shouldWaitForDomainReload) {
        spinner.stop();
        restoreStdin();

        checkServerVersion(result);
        console.log(JSON.stringify(stripInternalFields(result), null, 2));
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
          // Unity may have received the request before the TCP drop.
          // Break out of retry loop → proceed to file-based recovery below.
          spinner.update('Connection lost during compile. Waiting for result file...');
          break;
        }
        // JSON-RPC error (e.g. "Unity error: ..."): Unity processed the request
        // and returned an explicit error. No result file will be written
        // (confirmed: CompileUseCase.ExecuteAsync() is not reached when
        // JSON-RPC error occurs at parameter validation / security check).
        spinner.stop();
        restoreStdin();
        throw error instanceof Error ? error : new Error(String(error));
      }

      if (await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldValidateProject)) {
        spinner.update('Unity Editor is running, waiting for CLI Loop server to recover...');
        await sleep(RETRY_DELAY_MS);
        port = await resolveRecoveryPortOrKeepCurrent(
          port,
          portNumber,
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
    // but the TCP connection dropped before the response arrived (domain reload scenario).
    // In that case, Unity may have already written the result file, so we proceed to
    // file-based polling recovery.
    if (immediateResult === undefined && !requestDispatched) {
      spinner.stop();
      restoreStdin();
      if (lastError !== undefined) {
        await throwFinalToolError(lastError, projectRoot, shouldValidateProject);
      }
      throw new Error(
        'Compile request never reached Unity. Check that Unity is running and retry.',
      );
    }

    const projectRootFromUnity: string | undefined =
      immediateResult !== undefined
        ? (immediateResult['ProjectRoot'] as string | undefined)
        : undefined;
    const effectiveProjectRoot: string | null = projectRootFromUnity ?? projectRoot;

    // File-based polling requires a known project root
    if (effectiveProjectRoot === null) {
      spinner.stop();
      restoreStdin();
      if (immediateResult !== undefined) {
        checkServerVersion(immediateResult);
        console.log(JSON.stringify(stripInternalFields(immediateResult), null, 2));
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
      unityPort: port,
    });

    if (outcome === 'timed_out') {
      lastError = new Error(
        `Compile wait timed out after ${COMPILE_WAIT_TIMEOUT_MS}ms. Run 'uloop fix' and retry.`,
      );
    } else {
      const finalResult = storedResult ?? immediateResult;
      if (finalResult !== undefined) {
        spinner.stop();
        restoreStdin();
        checkServerVersion(finalResult);
        console.log(JSON.stringify(stripInternalFields(finalResult), null, 2));
        return;
      }
    }
  }

  spinner.stop();
  restoreStdin();
  if (lastError === undefined) {
    throw new Error('Tool execution failed without error details.');
  }
  await throwFinalToolError(lastError, projectRoot, shouldValidateProject);
}

export async function listAvailableTools(globalOptions: GlobalOptions): Promise<void> {
  let portNumber: number | undefined;
  if (globalOptions.port) {
    const parsed = parseInt(globalOptions.port, 10);
    if (isNaN(parsed)) {
      throw new Error(`Invalid port number: ${globalOptions.port}`);
    }
    portNumber = parsed;
  }
  checkUnityBusyStateBeforeProjectResolution(globalOptions);
  const port = await resolveUnityPort(portNumber, globalOptions.projectPath);
  const projectRoot =
    globalOptions.projectPath !== undefined
      ? validateProjectPath(globalOptions.projectPath)
      : findUnityProjectRoot();
  const shouldValidateProject = portNumber === undefined && projectRoot !== null;

  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner('Connecting to Unity...');

  let lastError: unknown;
  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    checkUnityBusyStateBeforeProjectResolution(globalOptions);

    const client = new DirectUnityClient(port);
    try {
      await client.connect();

      if (shouldValidateProject) {
        await validateConnectedProject(client, projectRoot);
      }

      spinner.update('Fetching tool list...');
      const result = await client.sendRequest<{
        Tools: Array<{ name: string; description: string }>;
      }>('get-tool-details', { IncludeDevelopmentOnly: false });

      if (!result.Tools || !Array.isArray(result.Tools)) {
        throw new Error('Unexpected response from Unity: missing Tools array');
      }

      // Success - stop spinner and output result
      spinner.stop();
      restoreStdin();
      for (const tool of result.Tools) {
        console.log(`  - ${tool.name}`);
      }
      return;
    } catch (error) {
      lastError = error;
      client.disconnect();

      if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
        break;
      }
      spinner.update('Retrying connection...');
      await sleep(RETRY_DELAY_MS);
    } finally {
      client.disconnect();
    }
  }

  spinner.stop();
  restoreStdin();
  await throwFinalToolError(lastError, projectRoot, shouldValidateProject);
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
  let portNumber: number | undefined;
  if (globalOptions.port) {
    const parsed = parseInt(globalOptions.port, 10);
    if (isNaN(parsed)) {
      throw new Error(`Invalid port number: ${globalOptions.port}`);
    }
    portNumber = parsed;
  }
  checkUnityBusyStateBeforeProjectResolution(globalOptions);
  const port = await resolveUnityPort(portNumber, globalOptions.projectPath);
  const projectRoot =
    globalOptions.projectPath !== undefined
      ? validateProjectPath(globalOptions.projectPath)
      : findUnityProjectRoot();
  const shouldValidateProject = portNumber === undefined && projectRoot !== null;

  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner('Connecting to Unity...');

  let lastError: unknown;
  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    checkUnityBusyStateBeforeProjectResolution(globalOptions);

    const client = new DirectUnityClient(port);
    try {
      await client.connect();

      if (shouldValidateProject) {
        await validateConnectedProject(client, projectRoot);
      }

      spinner.update('Syncing tools...');
      const result = await client.sendRequest<{
        Tools: UnityToolInfo[];
        Ver?: string;
      }>('get-tool-details', { IncludeDevelopmentOnly: false });

      spinner.stop();
      if (!result.Tools || !Array.isArray(result.Tools)) {
        restoreStdin();
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
      restoreStdin();
      return;
    } catch (error) {
      lastError = error;
      client.disconnect();

      if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
        break;
      }
      spinner.update('Retrying connection...');
      await sleep(RETRY_DELAY_MS);
    } finally {
      client.disconnect();
    }
  }

  spinner.stop();
  restoreStdin();
  await throwFinalToolError(lastError, projectRoot, shouldValidateProject);
}
