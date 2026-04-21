import { DirectUnityClient } from './direct-unity-client.js';
import { existsSync } from 'fs';
import { join } from 'path';
import { sleep } from './compile-helpers.js';
import {
  type ResolvedUnityConnection,
  resolveUnityConnection,
  UnityNotRunningError,
} from './port-resolver.js';
import { isRetryableFastProjectValidationErrorMessage } from './request-metadata.js';

// Launch readiness lock paths are built from the resolved project root selected by launch.
/* eslint-disable security/detect-non-literal-fs-filename */

const LAUNCH_READINESS_TIMEOUT_MS = 180000;
const LAUNCH_READINESS_RETRY_MS = 1000;
const LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT = 3;
// Why: launch should not report "ready" before the same execute-dynamic-code wrapper
// shape and silent Debug.Log path that startup prewarm targets have both completed.
// Why not probe with `return null;`: that succeeds too early and lets launch return while
// the first real execute-dynamic-code request still pays the startup cold path.
const LAUNCH_READINESS_STABLE_CODE =
  'UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }';
// Why: the stable fully-qualified probe keeps startup retries resilient, but measurements showed
// the first visible `using UnityEngine; Debug.Log(...)` request still had its own cold path.
// Why not keep launch readiness on the stable probe alone: that keeps launch from failing, but
// it still returns before the wrapper shape that users actually type is warmed.
const LAUNCH_READINESS_USER_LIKE_CODE =
  'using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { Debug.unityLogger.filterLogType = previous; }';
const LAUNCH_READINESS_REQUEST_TOTAL_THRESHOLD_MS = 250;
const LAUNCH_READINESS_SETTLE_TIMEOUT_MS = 10000;
const TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES = [
  'Another execution is already in progress',
  'Execution was cancelled or timed out',
  'Internal error',
];
const TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_SUBSTRINGS = [
  'PreUsingResolver.Resolve',
  'System.NullReferenceException',
];
const TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS = ['warming up'];
const RETRYABLE_UNITY_ERROR_SUBSTRINGS = ['can only be called from the main thread'];

interface ExecuteDynamicCodeReadinessResponse {
  Success?: boolean;
  ErrorMessage?: string;
  Timings?: string[];
}

interface DynamicCodeLaunchReadinessDependencies {
  resolveUnityConnectionFn: typeof resolveUnityConnection;
  createClient: (port: number) => DirectUnityClient;
  sleepFn: typeof sleep;
  nowFn: () => number;
  isProjectBusyFn?: (projectPath: string) => boolean;
}

const defaultDependencies: DynamicCodeLaunchReadinessDependencies = {
  resolveUnityConnectionFn: resolveUnityConnection,
  createClient: (port: number) => new DirectUnityClient(port),
  sleepFn: sleep,
  nowFn: () => Date.now(),
  isProjectBusyFn: isProjectBusyByLockFiles,
};

function isProjectBusyByLockFiles(projectPath: string): boolean {
  const compilingLockPath = join(projectPath, 'Temp', 'compiling.lock');
  if (existsSync(compilingLockPath)) {
    return true;
  }

  // Why: launch readiness already proves the listener can execute dynamic code, so the remaining
  // failure we must avoid is the CLI busy guard rejecting the next command during compile/reload.
  // Why not wait on serverstarting.lock here: startup lock ownership can intentionally outlive the
  // listener, and holding launch on it would reintroduce the false busy waits we already removed.
  const domainReloadLockPath = join(projectPath, 'Temp', 'domainreload.lock');
  return existsSync(domainReloadLockPath);
}

function isTransientExecuteDynamicCodeFailure(
  payload: ExecuteDynamicCodeReadinessResponse | undefined | null,
): boolean {
  if (payload === undefined || payload === null) {
    return true;
  }

  if (typeof payload.Success !== 'boolean') {
    return true;
  }

  if (payload.Success) {
    return false;
  }

  const errorMessage: string = payload.ErrorMessage ?? '';
  if (errorMessage.length === 0) {
    return true;
  }

  if (TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES.includes(errorMessage)) {
    return true;
  }

  if (
    TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_SUBSTRINGS.some((substring) =>
      errorMessage.includes(substring),
    )
  ) {
    return true;
  }

  return (
    isTransientCompilationProviderUnavailable(errorMessage) ||
    isRetryableUnityStartupError(`Unity error: ${errorMessage}`)
  );
}

function isTransientCompilationProviderUnavailable(errorMessage: string): boolean {
  if (!errorMessage.startsWith('COMPILATION_PROVIDER_UNAVAILABLE:')) {
    return false;
  }

  const normalizedMessage = errorMessage.toLowerCase();
  return TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS.some((substring) =>
    normalizedMessage.includes(substring),
  );
}

function isRetryableLaunchReadinessError(error: unknown): boolean {
  if (error instanceof UnityNotRunningError) {
    return true;
  }

  if (!(error instanceof Error)) {
    return false;
  }

  const message: string = error.message;
  return (
    isRetryableFastProjectValidationErrorMessage(message) ||
    message.includes('Could not read Unity server port from settings') ||
    message.includes('ECONNREFUSED') ||
    message.includes('EADDRNOTAVAIL') ||
    message === 'UNITY_NO_RESPONSE' ||
    message.startsWith('Connection lost:') ||
    isRetryableUnityStartupError(message)
  );
}

function isRetryableUnityStartupError(message: string): boolean {
  if (!message.startsWith('Unity error:')) {
    return false;
  }

  const normalizedMessage = message.toLowerCase();
  return RETRYABLE_UNITY_ERROR_SUBSTRINGS.some((substring) =>
    normalizedMessage.includes(substring),
  );
}

function createLaunchReadinessFailure(payload: ExecuteDynamicCodeReadinessResponse): Error {
  const errorMessage: string = payload.ErrorMessage ?? 'unknown execute-dynamic-code error';
  return new Error(`execute-dynamic-code launch readiness probe failed: ${errorMessage}`);
}

function parseTimingMilliseconds(
  timings: readonly string[] | undefined,
  label: string,
): number | null {
  if (timings === undefined) {
    return null;
  }

  const prefix = `[Perf] ${label}: `;
  const entry = timings.find((timing: string) => timing.startsWith(prefix));
  if (entry === undefined) {
    return null;
  }

  const valueText = entry.slice(prefix.length).replace(/ms$/, '');
  const value = Number.parseFloat(valueText);
  if (Number.isNaN(value)) {
    return null;
  }

  return value;
}

function isLaunchReadinessStable(payload: ExecuteDynamicCodeReadinessResponse): boolean {
  const requestTotalMilliseconds = parseTimingMilliseconds(payload.Timings, 'RequestTotal');
  if (requestTotalMilliseconds === null) {
    return true;
  }

  return requestTotalMilliseconds <= LAUNCH_READINESS_REQUEST_TOTAL_THRESHOLD_MS;
}

function hasFastSessionMetadata(connection: ResolvedUnityConnection): boolean {
  // Why: launch already waits for the target Unity instance to publish its session identity.
  // Why not fall back to get-version here: startup can answer that probe before project identity
  // is stable, which produces spurious warnings and undermines the readiness contract.
  return connection.requestMetadata !== null;
}

export async function waitForDynamicCodeReadyAfterLaunch(
  projectPath: string,
  dependencies: DynamicCodeLaunchReadinessDependencies = defaultDependencies,
): Promise<ResolvedUnityConnection> {
  const startTime: number = dependencies.nowFn();
  const totalProbeStageCount = LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT + 1;
  const isProjectBusyFn = dependencies.isProjectBusyFn ?? isProjectBusyByLockFiles;
  let currentProbeStage = 0;
  let firstSuccessfulProbeTime: number | null = null;
  let probeSessionId: string | null = null;

  while (dependencies.nowFn() - startTime < LAUNCH_READINESS_TIMEOUT_MS) {
    let client: DirectUnityClient | null = null;

    try {
      const connection: ResolvedUnityConnection = await dependencies.resolveUnityConnectionFn(
        undefined,
        projectPath,
      );
      if (!hasFastSessionMetadata(connection)) {
        currentProbeStage = 0;
        firstSuccessfulProbeTime = null;
        probeSessionId = null;
        await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
        continue;
      }

      const resolvedSessionId = connection.requestMetadata?.expectedServerSessionId ?? null;
      if (probeSessionId !== null && probeSessionId !== resolvedSessionId) {
        currentProbeStage = 0;
        firstSuccessfulProbeTime = null;
      }
      probeSessionId = resolvedSessionId;

      client = dependencies.createClient(connection.port);
      await client.connect();

      if (currentProbeStage >= totalProbeStageCount) {
        if (!isProjectBusyFn(projectPath)) {
          return connection;
        }

        await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
        continue;
      }

      const payload = await client.sendRequest<ExecuteDynamicCodeReadinessResponse>(
        'execute-dynamic-code',
        {
          Code:
            currentProbeStage < LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT
              ? LAUNCH_READINESS_STABLE_CODE
              : LAUNCH_READINESS_USER_LIKE_CODE,
          CompileOnly: false,
          YieldToForegroundRequests: true,
        },
        {
          requestMetadata: connection.requestMetadata ?? undefined,
        },
      );

      const isMalformedPayload =
        payload === undefined || payload === null || typeof payload.Success !== 'boolean';
      if (!isMalformedPayload) {
        if (payload.Success) {
          if (isLaunchReadinessStable(payload)) {
            currentProbeStage++;
            firstSuccessfulProbeTime = null;
            if (currentProbeStage >= totalProbeStageCount && !isProjectBusyFn(projectPath)) {
              return connection;
            }
          } else {
            if (firstSuccessfulProbeTime === null) {
              firstSuccessfulProbeTime = dependencies.nowFn();
            } else if (
              dependencies.nowFn() - firstSuccessfulProbeTime >=
              LAUNCH_READINESS_SETTLE_TIMEOUT_MS
            ) {
              currentProbeStage++;
              firstSuccessfulProbeTime = null;
              if (currentProbeStage >= totalProbeStageCount && !isProjectBusyFn(projectPath)) {
                return connection;
              }
            }
          }
        } else {
          currentProbeStage = 0;
          firstSuccessfulProbeTime = null;
          if (!isTransientExecuteDynamicCodeFailure(payload)) {
            throw createLaunchReadinessFailure(payload);
          }
        }
      } else {
        currentProbeStage = 0;
        firstSuccessfulProbeTime = null;
      }
    } catch (error) {
      if (!isRetryableLaunchReadinessError(error)) {
        throw error;
      }

      currentProbeStage = 0;
      firstSuccessfulProbeTime = null;
    } finally {
      client?.disconnect();
    }

    await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
  }

  throw new Error(
    `Timed out waiting for execute-dynamic-code to become ready after launch (${LAUNCH_READINESS_TIMEOUT_MS}ms).`,
  );
}

export async function waitForLaunchReadyAfterLaunch(
  projectPath: string,
  dependencies: DynamicCodeLaunchReadinessDependencies = defaultDependencies,
): Promise<ResolvedUnityConnection> {
  const startTime: number = dependencies.nowFn();
  const isProjectBusyFn = dependencies.isProjectBusyFn ?? isProjectBusyByLockFiles;

  while (dependencies.nowFn() - startTime < LAUNCH_READINESS_TIMEOUT_MS) {
    let client: DirectUnityClient | null = null;

    try {
      const connection: ResolvedUnityConnection = await dependencies.resolveUnityConnectionFn(
        undefined,
        projectPath,
      );
      if (!hasFastSessionMetadata(connection)) {
        await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
        continue;
      }

      client = dependencies.createClient(connection.port);
      await client.connect();

      if (!isProjectBusyFn(projectPath)) {
        return connection;
      }
    } catch (error) {
      if (!isRetryableLaunchReadinessError(error)) {
        throw error;
      }
    } finally {
      client?.disconnect();
    }

    await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
  }

  throw new Error(
    `Timed out waiting for Unity to finish launch readiness after launch (${LAUNCH_READINESS_TIMEOUT_MS}ms).`,
  );
}
