const mockResolveUnityPort = jest.fn<Promise<number>, [number | undefined, string | undefined]>();
const mockValidateProjectPath = jest.fn<string, [string]>();
const mockFindUnityProjectRoot = jest.fn<string | null, []>();
const mockExistsSync = jest.fn<boolean, [string]>();
const mockFindRunningUnityProcessForProject = jest.fn<
  Promise<{ pid: number } | null>,
  [string]
>();
const mockSleep = jest.fn<Promise<void>, [number]>();
const mockSpinnerUpdate = jest.fn<void, [string]>();
const mockSpinnerStop = jest.fn<void, []>();
const mockConsoleLog = jest.spyOn(console, 'log').mockImplementation(() => {});

const constructedPorts: number[] = [];

class MockDirectUnityClient {
  public readonly port: number;

  public constructor(port: number) {
    this.port = port;
    constructedPorts.push(port);
  }

  public async connect(): Promise<void> {
    if (this.port === 8711) {
      throw new Error('connect ECONNREFUSED 127.0.0.1:8711');
    }
  }

  public disconnect(): void {}

  public async sendRequest<T>(): Promise<T> {
    return {
      Logs: [],
      Ver: '1.7.3',
    } as T;
  }
}

jest.mock('../port-resolver.js', () => ({
  resolveUnityPort: (explicitPort?: number, projectPath?: string): Promise<number> =>
    mockResolveUnityPort(explicitPort, projectPath),
  validateProjectPath: (projectPath: string): string => mockValidateProjectPath(projectPath),
  UnityNotRunningError: class UnityNotRunningError extends Error {},
  UnityServerNotRunningError: class UnityServerNotRunningError extends Error {},
}));

jest.mock('../project-root.js', () => ({
  findUnityProjectRoot: (): string | null => mockFindUnityProjectRoot(),
}));

jest.mock('fs', () => ({
  existsSync: (path: string): boolean => mockExistsSync(path),
}));

jest.mock('../unity-process.js', () => ({
  findRunningUnityProcessForProject: (projectRoot: string): Promise<{ pid: number } | null> =>
    mockFindRunningUnityProcessForProject(projectRoot),
}));

jest.mock('../compile-helpers.js', () => ({
  ensureCompileRequestId: (params: Record<string, unknown>): string =>
    (params['RequestId'] as string | undefined) ?? 'request-id',
  resolveCompileExecutionOptions: (): { forceRecompile: boolean; waitForDomainReload: boolean } =>
    ({
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
  validateConnectedProject: jest.fn(),
  ProjectMismatchError: class ProjectMismatchError extends Error {},
}));

jest.mock('../direct-unity-client.js', () => ({
  DirectUnityClient: MockDirectUnityClient,
}));

import { executeToolCommand } from '../execute-tool.js';

describe('executeToolCommand recovery', () => {
  beforeEach(() => {
    mockResolveUnityPort.mockReset();
    mockResolveUnityPort.mockResolvedValueOnce(8711).mockResolvedValueOnce(8712);

    mockValidateProjectPath.mockReset();
    mockValidateProjectPath.mockReturnValue('/project');

    mockFindUnityProjectRoot.mockReset();
    mockFindUnityProjectRoot.mockReturnValue('/project');

    mockExistsSync.mockReset();
    mockExistsSync.mockReturnValue(false);

    mockFindRunningUnityProcessForProject.mockReset();
    mockFindRunningUnityProcessForProject.mockResolvedValue({ pid: 1234 });

    mockSleep.mockReset();
    mockSleep.mockResolvedValue();

    mockSpinnerUpdate.mockReset();
    mockSpinnerStop.mockReset();

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

    expect(mockResolveUnityPort).toHaveBeenCalledTimes(2);
    expect(constructedPorts).toEqual([8711, 8712]);
  });
});
