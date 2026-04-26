interface MockResolvedUnityConnection {
  port: number;
  projectRoot: string | null;
  requestMetadata: null;
  shouldValidateProject: boolean;
}

interface MockSpinner {
  update(): void;
  stop(): void;
}

const mockResolveUnityConnection = jest.fn<
  Promise<MockResolvedUnityConnection>,
  [number | undefined, string | undefined]
>();
const mockValidateProjectPath = jest.fn<string, [string]>();
const mockExistsSync = jest.fn<boolean, [string]>();
const mockCreateSpinner = jest.fn<MockSpinner, [string]>();
const mockValidateConnectedProject = jest.fn();
const mockConsoleLog = jest.spyOn(console, 'log').mockImplementation(() => {});

class MockDirectUnityClient {
  public constructor(private readonly _port: number) {}

  public connect(): Promise<void> {
    return Promise.resolve();
  }

  public disconnect(): void {}

  public sendRequest<T>(): Promise<T> {
    return Promise.resolve({
      Success: true,
      Ver: '1.7.3',
      Timings: ['[Perf] RequestTotal: 10.0ms'],
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

jest.mock('fs', () => ({
  existsSync: (path: string): boolean => mockExistsSync(path),
}));

jest.mock('../spinner.js', () => ({
  createSpinner: (message: string): { update: () => void; stop: () => void } =>
    mockCreateSpinner(message),
}));

jest.mock('../project-validator.js', () => ({
  validateConnectedProject: (...args: unknown[]): unknown => mockValidateConnectedProject(...args),
  ProjectMismatchError: class ProjectMismatchError extends Error {},
}));

jest.mock('../direct-unity-client.js', () => ({
  DirectUnityClient: MockDirectUnityClient,
}));

import { executeToolCommand } from '../execute-tool.js';

function parsePrintedJson(): Record<string, unknown> {
  const firstCall = mockConsoleLog.mock.calls[0];
  if (firstCall === undefined) {
    throw new Error('Expected CLI JSON output');
  }

  const firstArgument: unknown = firstCall[0];
  if (typeof firstArgument !== 'string') {
    throw new Error('Expected CLI output to be a JSON string');
  }

  const parsed: unknown = JSON.parse(firstArgument);
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('Expected CLI output to be a JSON object');
  }

  return parsed as Record<string, unknown>;
}

describe('executeToolCommand interactive feedback', () => {
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

    mockCreateSpinner.mockReset();
    mockCreateSpinner.mockReturnValue({
      update: (): void => {},
      stop: (): void => {},
    });

    mockValidateConnectedProject.mockReset();

    mockConsoleLog.mockClear();
  });

  afterAll(() => {
    mockConsoleLog.mockRestore();
  });

  it('skips spinner creation for execute-dynamic-code', async () => {
    await expect(
      executeToolCommand(
        'execute-dynamic-code',
        { Code: 'return 1;' },
        { projectPath: '/project' },
      ),
    ).resolves.toBeUndefined();

    expect(mockCreateSpinner).not.toHaveBeenCalled();
  });

  it('keeps spinner creation for other tools', async () => {
    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    expect(mockCreateSpinner).toHaveBeenCalledWith('Connecting to Unity...');
  });

  it('hides server version from regular tool output', async () => {
    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    const output = parsePrintedJson();

    expect(output['Success']).toBe(true);
    expect(output).not.toHaveProperty('Ver');
  });

  it('keeps server version in get-version output', async () => {
    await expect(executeToolCommand('get-version', {}, { projectPath: '/project' })).resolves.toBe(
      undefined,
    );

    const output = parsePrintedJson();

    expect(output['Success']).toBe(true);
    expect(output['Ver']).toBe('1.7.3');
  });
});
