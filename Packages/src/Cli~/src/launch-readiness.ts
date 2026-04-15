import { DirectUnityClient } from './direct-unity-client.js';
import { sleep } from './compile-helpers.js';
import {
  type ResolvedUnityConnection,
  resolveUnityConnection,
  UnityNotRunningError,
} from './port-resolver.js';
import { ProjectMismatchError, validateConnectedProject } from './project-validator.js';
import { isRetryableFastProjectValidationErrorMessage } from './request-metadata.js';

const LAUNCH_READINESS_TIMEOUT_MS = 180000;
const LAUNCH_READINESS_RETRY_MS = 1000;
const LAUNCH_READINESS_CODE = 'return null;';
const LAUNCH_READINESS_REQUEST_TOTAL_THRESHOLD_MS = 250;
const LAUNCH_READINESS_SETTLE_TIMEOUT_MS = 10000;
const TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES = [
  'Another execution is already in progress',
  'Execution was cancelled or timed out',
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
}

const defaultDependencies: DynamicCodeLaunchReadinessDependencies = {
  resolveUnityConnectionFn: resolveUnityConnection,
  createClient: (port: number) => new DirectUnityClient(port),
  sleepFn: sleep,
  nowFn: () => Date.now(),
};

function isTransientExecuteDynamicCodeFailure(
  payload: ExecuteDynamicCodeReadinessResponse,
): boolean {
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

  return isTransientCompilationProviderUnavailable(errorMessage);
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
  if (error instanceof ProjectMismatchError) {
    return false;
  }

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

export async function waitForDynamicCodeReadyAfterLaunch(
  projectPath: string,
  dependencies: DynamicCodeLaunchReadinessDependencies = defaultDependencies,
): Promise<void> {
  const startTime: number = dependencies.nowFn();
  let firstSuccessfulProbeTime: number | null = null;

  while (dependencies.nowFn() - startTime < LAUNCH_READINESS_TIMEOUT_MS) {
    let client: DirectUnityClient | null = null;

    try {
      const connection: ResolvedUnityConnection = await dependencies.resolveUnityConnectionFn(
        undefined,
        projectPath,
      );
      client = dependencies.createClient(connection.port);
      await client.connect();
      if (connection.shouldValidateProject && connection.projectRoot !== null) {
        await validateConnectedProject(client, connection.projectRoot);
      }

      const payload = await client.sendRequest<ExecuteDynamicCodeReadinessResponse>(
        'execute-dynamic-code',
        {
          Code: LAUNCH_READINESS_CODE,
          CompileOnly: false,
        },
        {
          requestMetadata: connection.requestMetadata ?? undefined,
        },
      );

      if (payload.Success) {
        if (isLaunchReadinessStable(payload)) {
          return;
        }

        if (firstSuccessfulProbeTime === null) {
          firstSuccessfulProbeTime = dependencies.nowFn();
        } else if (
          dependencies.nowFn() - firstSuccessfulProbeTime >=
          LAUNCH_READINESS_SETTLE_TIMEOUT_MS
        ) {
          return;
        }
      } else if (!isTransientExecuteDynamicCodeFailure(payload)) {
        throw createLaunchReadinessFailure(payload);
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
    `Timed out waiting for execute-dynamic-code to become ready after launch (${LAUNCH_READINESS_TIMEOUT_MS}ms).`,
  );
}
