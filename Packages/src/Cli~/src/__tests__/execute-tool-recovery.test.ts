interface MockResolvedUnityConnection {
  endpoint: { kind: 'unix-socket'; path: string };
  projectRoot: string | null;
  requestMetadata: { expectedProjectRoot: string; expectedServerSessionId: string } | null;
  shouldValidateProject: boolean;
}

const mockResolveUnityConnection = jest.fn<
  Promise<MockResolvedUnityConnection>,
  [string | undefined]
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

const constructedEndpoints: string[] = [];

class MockDirectUnityClient {
  public readonly endpointPath: string;

  public constructor(endpoint: { path: string }) {
    this.endpointPath = endpoint.path;
    constructedEndpoints.push(this.endpointPath);
  }

  public connect(): Promise<void> {
    if (this.endpointPath.endsWith('8711.sock')) {
      return Promise.reject(new Error('connect ENOENT /tmp/uloop-test-8711.sock'));
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
  resolveUnityConnection: (projectPath?: string): Promise<MockResolvedUnityConnection> =>
    mockResolveUnityConnection(projectPath),
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

function createMockConnection(
  endpointId: number,
  requestMetadata: MockResolvedUnityConnection['requestMetadata'] = null,
  shouldValidateProject = true,
): MockResolvedUnityConnection {
  return {
    endpoint: { kind: 'unix-socket', path: `/tmp/uloop-test-${endpointId}.sock` },
    projectRoot: '/project',
    requestMetadata,
    shouldValidateProject,
  };
}

describe('executeToolCommand recovery', () => {
  beforeEach(() => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce(createMockConnection(8711))
      .mockResolvedValueOnce(createMockConnection(8712));

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
    constructedEndpoints.length = 0;
  });

  afterAll(() => {
    mockConsoleLog.mockRestore();
  });

  it('re-resolves the Unity connection before retrying recovery while Unity is still running', async () => {
    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });

  it('keeps non-dynamic tools running when server startup appears during recovery', async () => {
    let lockVisible = false;
    mockExistsSync.mockImplementation(
      (path: string) => path.endsWith('serverstarting.lock') && lockVisible,
    );
    mockSleep.mockImplementation(async (): Promise<void> => {
      lockVisible = true;
      await Promise.resolve();
    });

    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
  });

  it('re-resolves the Unity connection for command execution when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce(
        createMockConnection(
          8711,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
          false,
        ),
      )
      .mockResolvedValueOnce(
        createMockConnection(
          8712,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
          false,
        ),
      );

    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });

  it('re-resolves the Unity connection for list while Unity is still running', async () => {
    await expect(listAvailableTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });

  it('re-resolves the Unity connection for list when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce(
        createMockConnection(
          8711,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
          false,
        ),
      )
      .mockResolvedValueOnce(
        createMockConnection(
          8712,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
          false,
        ),
      );

    await expect(listAvailableTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });

  it('re-resolves the Unity connection for sync while Unity is still running', async () => {
    await expect(syncTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });

  it('re-resolves the Unity connection for sync when request metadata disables project validation', async () => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection
      .mockResolvedValueOnce(
        createMockConnection(
          8711,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-a' },
          false,
        ),
      )
      .mockResolvedValueOnce(
        createMockConnection(
          8712,
          { expectedProjectRoot: '/project', expectedServerSessionId: 'session-b' },
          false,
        ),
      );

    await expect(syncTools({ projectPath: '/project' })).resolves.toBeUndefined();

    expect(mockResolveUnityConnection).toHaveBeenCalledTimes(2);
    expect(constructedEndpoints).toEqual([
      '/tmp/uloop-test-8711.sock',
      '/tmp/uloop-test-8712.sock',
    ]);
  });
});
