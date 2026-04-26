const mockResolveUnityConnection = jest.fn();
const mockValidateProjectPath = jest.fn<string, [string]>();
const mockExistsSync = jest.fn<boolean, [string]>();
const mockSpawnSync = jest.fn();
const mockIsToolEnabled = jest.fn<boolean, [string, string | undefined]>();
const mockConsoleLog = jest.spyOn(console, 'log').mockImplementation(() => {});

class MockDirectUnityClient {
  public constructor(private readonly _port: number) {}

  public connect(): Promise<void> {
    return Promise.resolve();
  }

  public disconnect(): void {}

  public sendRequest<T>(): Promise<T> {
    return Promise.resolve({
      Success: null,
      ErrorCount: 0,
      Message: 'Force compilation executed. Use get-logs tool to retrieve compilation messages.',
      ProjectRoot: '/project',
      Ver: '1.7.3',
    } as T);
  }
}

jest.mock('../port-resolver.js', () => ({
  resolveUnityConnection: (
    explicitPort?: number,
    projectPath?: string,
  ): Promise<{
    port: number;
    projectRoot: string | null;
    requestMetadata: null;
    shouldValidateProject: boolean;
  }> =>
    mockResolveUnityConnection(explicitPort, projectPath) as Promise<{
      port: number;
      projectRoot: string | null;
      requestMetadata: null;
      shouldValidateProject: boolean;
    }>,
  validateProjectPath: (projectPath: string): string => mockValidateProjectPath(projectPath),
  UnityNotRunningError: class UnityNotRunningError extends Error {},
  UnityServerNotRunningError: class UnityServerNotRunningError extends Error {},
}));

jest.mock('fs', () => ({
  existsSync: (path: string): boolean => mockExistsSync(path),
  statSync: jest.fn(),
}));

jest.mock('child_process', () => ({
  execFile: jest.fn(),
  spawnSync: (...args: unknown[]): unknown => mockSpawnSync(...args),
}));

jest.mock('../project-validator.js', () => ({
  validateConnectedProject: (): Promise<void> => Promise.resolve(),
  ProjectMismatchError: class ProjectMismatchError extends Error {},
}));

jest.mock('../spinner.js', () => ({
  createSpinner: (): { update: () => void; stop: () => void } => ({
    update: (): void => {},
    stop: (): void => {},
  }),
}));

jest.mock('../direct-unity-client.js', () => ({
  DirectUnityClient: MockDirectUnityClient,
}));

jest.mock('../compile-helpers.js', () => ({
  ensureCompileRequestId: (params: Record<string, unknown>): string =>
    (params['RequestId'] as string | undefined) ?? 'compile_request_id',
  resolveCompileExecutionOptions: (): {
    forceRecompile: boolean;
    waitForDomainReload: boolean;
  } => ({
    forceRecompile: true,
    waitForDomainReload: true,
  }),
  sleep: (): Promise<void> => Promise.resolve(),
  waitForCompileCompletion: (): Promise<{
    outcome: 'completed';
    result: Record<string, unknown>;
  }> =>
    Promise.resolve({
      outcome: 'completed',
      result: {
        Success: null,
        ErrorCount: 0,
        Message: 'Force compilation executed. Use get-logs tool to retrieve compilation messages.',
      },
    }),
}));

jest.mock('../tool-settings-loader.js', () => ({
  isToolEnabled: (toolName: string, projectPath?: string): boolean =>
    mockIsToolEnabled(toolName, projectPath),
}));

import { executeToolCommand } from '../execute-tool.js';

describe('executeToolCommand compile warmup', () => {
  beforeEach(() => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection.mockResolvedValue({
      port: 8711,
      projectRoot: '/project',
      requestMetadata: null,
      shouldValidateProject: true,
    });

    mockValidateProjectPath.mockReset();
    mockValidateProjectPath.mockReturnValue('/project');

    mockExistsSync.mockReset();
    mockExistsSync.mockReturnValue(false);

    mockSpawnSync.mockReset();
    mockSpawnSync.mockReturnValue({ status: 1 });

    mockIsToolEnabled.mockReset();
    mockIsToolEnabled.mockReturnValue(true);

    mockConsoleLog.mockClear();
  });

  afterAll(() => {
    mockConsoleLog.mockRestore();
  });

  it('fails compile when post-domain-reload dynamic code warmup fails', async () => {
    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).rejects.toThrow('Post-compile dynamic code prewarm failed.');

    expect(mockConsoleLog).not.toHaveBeenCalled();
  });

  it('keeps compile successful when warmup only hits transient execution contention', async () => {
    mockSpawnSync
      .mockReturnValueOnce({
        status: 0,
        stdout: JSON.stringify({
          Success: false,
          ErrorMessage: 'Another execution is already in progress',
        }),
      })
      .mockReturnValue({
        status: 0,
        stdout: JSON.stringify({ Success: true }),
      });

    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockSpawnSync).toHaveBeenCalledTimes(5);
    expect(mockConsoleLog).toHaveBeenCalledTimes(1);
  });

  it('keeps compile successful when warmup only hits compilation provider startup', async () => {
    mockSpawnSync
      .mockReturnValueOnce({
        status: 0,
        stdout: JSON.stringify({
          Success: false,
          ErrorMessage: 'COMPILATION_PROVIDER_UNAVAILABLE: dynamic compiler is still warming up',
        }),
      })
      .mockReturnValue({
        status: 0,
        stdout: JSON.stringify({ Success: true }),
      });

    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockSpawnSync).toHaveBeenCalledTimes(5);
    expect(mockConsoleLog).toHaveBeenCalledTimes(1);
  });

  it('keeps compile successful while startup prewarm needs short retries before all passes complete', async () => {
    for (let attempt = 0; attempt < 3; attempt++) {
      mockSpawnSync.mockReturnValueOnce({
        status: 0,
        stdout: JSON.stringify({
          Success: false,
          ErrorMessage: 'Another execution is already in progress',
        }),
      });
    }

    mockSpawnSync.mockReturnValue({
      status: 0,
      stdout: JSON.stringify({ Success: true }),
    });

    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockSpawnSync).toHaveBeenCalledTimes(7);
    expect(mockConsoleLog).toHaveBeenCalledTimes(1);
  });

  it('keeps compile successful when the child warmup process times out once and then succeeds', async () => {
    mockSpawnSync
      .mockReturnValueOnce({
        status: null,
        error: new Error('spawnSync ETIMEDOUT'),
      })
      .mockReturnValue({
        status: 0,
        stdout: JSON.stringify({ Success: true }),
      });

    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockSpawnSync).toHaveBeenCalledTimes(5);
    expect(mockConsoleLog).toHaveBeenCalledTimes(1);
  });

  it('keeps compile successful when execute-dynamic-code is disabled for the project', async () => {
    mockIsToolEnabled.mockReturnValue(false);

    await expect(
      executeToolCommand(
        'compile',
        {
          ForceRecompile: true,
          WaitForDomainReload: true,
        },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockSpawnSync).not.toHaveBeenCalled();
    expect(mockConsoleLog).toHaveBeenCalledTimes(1);
  });
});
