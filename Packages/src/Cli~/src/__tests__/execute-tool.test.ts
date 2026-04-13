import {
  diagnoseRetryableProjectConnectionError,
  isTransportDisconnectError,
  resolveRecoveryPortOrKeepCurrent,
  shouldRetryWhenUnityProcessIsRunning,
} from '../execute-tool.js';
import { UnityNotRunningError, UnityServerNotRunningError } from '../port-resolver.js';
import { ProjectMismatchError } from '../project-validator.js';

describe('isTransportDisconnectError', () => {
  it('returns true for UNITY_NO_RESPONSE', () => {
    expect(isTransportDisconnectError(new Error('UNITY_NO_RESPONSE'))).toBe(true);
  });

  it('returns true for Connection lost with details', () => {
    expect(isTransportDisconnectError(new Error('Connection lost: read ECONNRESET'))).toBe(true);
  });

  it('returns true for Connection lost with EPIPE', () => {
    expect(isTransportDisconnectError(new Error('Connection lost: write EPIPE'))).toBe(true);
  });

  it('returns false for JSON-RPC error from Unity', () => {
    expect(isTransportDisconnectError(new Error('Unity error: compilation failed'))).toBe(false);
  });

  it('returns false for connection refused (pre-dispatch error)', () => {
    expect(isTransportDisconnectError(new Error('connect ECONNREFUSED 127.0.0.1:8711'))).toBe(
      false,
    );
  });

  it('returns false for non-Error values', () => {
    expect(isTransportDisconnectError('UNITY_NO_RESPONSE')).toBe(false);
    expect(isTransportDisconnectError(null)).toBe(false);
    expect(isTransportDisconnectError(undefined)).toBe(false);
  });

  it('returns false for UnityNotRunningError', () => {
    expect(isTransportDisconnectError(new UnityNotRunningError('/project'))).toBe(false);
  });

  it('returns false for UnityServerNotRunningError', () => {
    expect(isTransportDisconnectError(new UnityServerNotRunningError('/project'))).toBe(false);
  });

  it('returns false for ProjectMismatchError', () => {
    expect(isTransportDisconnectError(new ProjectMismatchError('/a', '/b'))).toBe(false);
  });
});

describe('diagnoseRetryableProjectConnectionError', () => {
  it('returns UnityNotRunningError when connection fails and Unity is not running', async () => {
    const error = await diagnoseRetryableProjectConnectionError(
      new Error('Connection error: connect ECONNREFUSED 127.0.0.1:8711'),
      '/project',
      true,
      {
        findRunningUnityProcessForProjectFn: jest.fn().mockResolvedValue(null),
      },
    );

    expect(error).toBeInstanceOf(UnityNotRunningError);
  });

  it('returns UnityServerNotRunningError when Unity is running but server is unavailable', async () => {
    const error = await diagnoseRetryableProjectConnectionError(
      new Error('UNITY_NO_RESPONSE'),
      '/project',
      true,
      {
        findRunningUnityProcessForProjectFn: jest.fn().mockResolvedValue({ pid: 1234 }),
      },
    );

    expect(error).toBeInstanceOf(UnityServerNotRunningError);
  });

  it('preserves non-retryable errors', async () => {
    const originalError = new ProjectMismatchError('/expected', '/actual');

    const error = await diagnoseRetryableProjectConnectionError(originalError, '/project', true, {
      findRunningUnityProcessForProjectFn: jest.fn(),
    });

    expect(error).toBe(originalError);
  });

  it('preserves retryable errors when project diagnosis is disabled', async () => {
    const originalError = new Error('Connection error: connect ECONNREFUSED 127.0.0.1:8711');

    const error = await diagnoseRetryableProjectConnectionError(originalError, '/project', false, {
      findRunningUnityProcessForProjectFn: jest.fn(),
    });

    expect(error).toBe(originalError);
  });

  it('preserves the original error when OS-level process inspection fails', async () => {
    const originalError = new Error('Connection error: connect ECONNREFUSED 127.0.0.1:8711');

    const error = await diagnoseRetryableProjectConnectionError(originalError, '/project', true, {
      findRunningUnityProcessForProjectFn: jest.fn().mockRejectedValue(new Error('ps failed')),
    });

    expect(error).toBe(originalError);
  });
});

describe('shouldRetryWhenUnityProcessIsRunning', () => {
  it('returns true for retryable failures when Unity is still running', async () => {
    await expect(
      shouldRetryWhenUnityProcessIsRunning(
        new Error('UNITY_NO_RESPONSE'),
        '/project',
        true,
        {
          findRunningUnityProcessForProjectFn: jest.fn().mockResolvedValue({ pid: 1234 }),
        },
      ),
    ).resolves.toBe(true);
  });

  it('returns false for non-retryable Unity errors even when Unity is still running', async () => {
    await expect(
      shouldRetryWhenUnityProcessIsRunning(
        new Error('Unity error: compilation failed'),
        '/project',
        true,
        {
          findRunningUnityProcessForProjectFn: jest.fn().mockResolvedValue({ pid: 1234 }),
        },
      ),
    ).resolves.toBe(false);
  });
});

describe('resolveRecoveryPortOrKeepCurrent', () => {
  it('keeps the current port when recovery settings are temporarily unreadable', async () => {
    await expect(
      resolveRecoveryPortOrKeepCurrent(8711, undefined, '/project', jest.fn().mockRejectedValue(new Error('busy'))),
    ).resolves.toBe(8711);
  });

  it('re-resolves the port when recovery settings are available', async () => {
    await expect(
      resolveRecoveryPortOrKeepCurrent(
        8711,
        undefined,
        '/project',
        jest.fn().mockResolvedValue(8712),
      ),
    ).resolves.toBe(8712);
  });
});
