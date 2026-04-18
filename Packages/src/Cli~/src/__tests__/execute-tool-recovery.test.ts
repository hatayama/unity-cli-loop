interface MockResolvedUnityConnection {
  port: number;
  projectRoot: string | null;
  requestMetadata: { expectedProjectRoot: string; expectedServerSessionId: string } | null;
  shouldValidateProject: boolean;
}

const mockResolveUnityConnection = jest.fn<
  Promise<MockResolvedUnityConnection>,
  [number | undefined, string | undefined]
>();
const mockValidateProjectPath = jest.fn<string, [string]>();
const mockFindUnityProjectRoot = jest.fn<string | null, []>();
const mockExistsSync = jest.fn<boolean, [string]>();
const mockStatSync = jest.fn<{ mtimeMs: number }, [string]>();
const mockFindRunningUnityProcessForProject = jest.fn<Promise<{ pid: number } | null>, [string]>();
const mockSleep = jest.fn<Promise<void>, [number]>();
const mockSpinnerUpdate = jest.fn<void, [string]>();
const mockSpinnerStop = jest.fn<void, []>();
const mockConsoleLog = jest.spyOn(console, 'log').mockImplementation(() => {});
const mockValidateConnectedProject = jest.fn<Promise<void>, [unknown, string]>();

const constructedPorts: number[] = [];

class MockDirectUnityClient {
  public readonly port: number;

  public constructor(port: number) {
    this.port = port;
    constructedPorts.push(port);
  }

  public connect(): Promise<void> {
    if (this.port === 8711) {
      return Promise.reject(new Error('connect ECONNREFUSED 127.0.0.1:8711'));
    }

    return Promise.resolve();
  }

  public disconnect(): void {}

  public sendRequest<T>(): Promise<T> {
    return Promise.resolve({
      Logs: [],
      Tools: [],
      Ver: '1.7.3',
    } as T);
  }
}

jest.mock('../port-resolver.js', () => ({
  resolveUnityConnection: (
    explicitPort?: number,
    projectPath?: string,
  ): Promise<MockResolvedUnityConnection> => mockResolveUnityConnection(explicitPort, projectPath),
  validateProjectPath: (projectPath: string): string => mockValidateProjectPath(projectPath),
  UnityNotRunningError: class UnityNotRunningError extends Error {},
  UnityServerNotRunningError: class UnityServerNotRunningError extends Error {},
}));

jest.mock('../project-root.js', () => ({
  findUnityProjectRoot: (): string | null => mockFindUnityProjectRoot(),
}));

jest.mock('fs', () => ({
  existsSync: (path: string): boolean => mockExistsSync(path),
  statSync: (path: string): { mtimeMs: number } => mockStatSync(path),
  mkdirSync: jest.fn(),
  writeFileSync: jest.fn(),
}));

jest.mock('../unity-process.js', () => ({
  findRunningUnityProcessForProject: (projectRoot: string): Promise<{ pid: number } | null> =>
    mockFindRunningUnityProcessForProject(projectRoot),
}));

jest.mock('../compile-helpers.js', () => ({
  ensureCompileRequestId: (params: Record<string, unknown>): string =>
    (params['RequestId'] as string | undefined) ?? 'request-id',
  resolveCompileExecutionOptions: (): {
    forceRecompile: boolean;
    waitForDomainReload: boolean;
  } => ({
    forceRecompile: false,
    waitForDomainReload: false,
  }),
  sleep: (delayMs: number): Promise<void> => mockSleep(delayMs),
  waitForCompileCompletion: jest.fn(),
}));

jest.mock('../spinner.js', () => ({
  createSpinner: (): { update: (message: string) => void; stop: () => void } => ({
    update: (message: string): void => mockSpinnerUpdate(message),
    stop: (): void => mockSpinnerStop(),
  }),
}));

jest.mock('../project-validator.js', () => ({
  validateConnectedProject: (...args: [unknown, string]): Promise<void> =>
    mockValidateConnectedProject(...args),
  ProjectMismatchError: class ProjectMismatchError extends Error {},
}));

jest.mock('../direct-unity-client.js', () => ({
  DirectUnityClient: MockDirectUnityClient,
}));

import { executeToolCommand, listAvailableTools, syncTools } from '../execute-tool.js';

describe('executeToolCommand recovery', () => {
  beforeEach(() => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce({
        port: 8711,
        projectRoot: '/project',
        requestMetadata: null,
        shouldValidateProject: true,
      })
      .mockResolvedValueOnce({
        port: 8712,
        projectRoot: '/project',
        requestMetadata: null,
        shouldValidateProject: true,
      });

    mockValidateProjectPath.mockReset();
    mockValidateProjectPath.mockReturnValue('/project');

    mockFindUnityProjectRoot.mockReset();
    mockFindUnityProjectRoot.mockReturnValue('/project');

    mockExistsSync.mockReset();
    mockExistsSync.mockReturnValue(false);
    mockStatSync.mockReset();
    mockStatSync.mockReturnValue({ mtimeMs: Date.now() });

    mockFindRunningUnityProcessForProject.mockReset();
    mockFindRunningUnityProcessForProject.mockResolvedValue({ pid: 1234 });

    mockSleep.mockReset();
    mockSleep.mockResolvedValue();

    mockSpinnerUpdate.mockReset();
    mockSpinnerStop.mockReset();
    mockValidateConnectedProject.mockReset();
    mockValidateConnectedProject.mockResolvedValue();

    mockConsoleLog.mockClear();
    constructedPorts.length = 0;
  });

  afterAll(() => {
    mockConsoleLog.mockRestore();
  });

  it('re-resolves the Unity port before retrying recovery while Unity is still running', async () => {
    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });

  it('cleans up interactive feedback when server startup blocks the next retry iteration', async () => {
    let lockVisible = false;
    mockExistsSync.mockImplementation(
      (path: string) => path.endsWith('serverstarting.lock') && lockVisible,
    );
    mockSleep.mockImplementation(async (): Promise<void> => {
      lockVisible = true;
      await Promise.resolve();
    });

    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).rejects.toThrow(
      'UNITY_SERVER_STARTING',
    );

    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
  });

  it('re-resolves the Unity port for command execution when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce({
        port: 8711,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
        shouldValidateProject: false,
      })
      .mockResolvedValueOnce({
        port: 8712,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
        shouldValidateProject: false,
      });

    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });

  it('re-resolves the Unity port for list while Unity is still running', async () => {
    await expect(listAvailableTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });

  it('re-resolves the Unity port for list when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce({
        port: 8711,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
        shouldValidateProject: false,
      })
      .mockResolvedValueOnce({
        port: 8712,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
        shouldValidateProject: false,
      });

    await expect(listAvailableTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });

  it('re-resolves the Unity port for sync while Unity is still running', async () => {
    await expect(syncTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });

  it('re-resolves the Unity port for sync when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce({
        port: 8711,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
        shouldValidateProject: false,
      })
      .mockResolvedValueOnce({
        port: 8712,
        projectRoot: '/project',
        requestMetadata: { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
        shouldValidateProject: false,
      });

    await expect(syncTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });
});
