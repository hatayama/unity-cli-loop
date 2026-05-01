const mockResolveUnityConnection = jest.fn();
const mockValidateProjectPath = jest.fn<string, [string]>();
const mockFindUnityProjectRoot = jest.fn<string | null, []>();
const mockExistsSync = jest.fn<boolean, [string]>();
const mockStatSync = jest.fn<{ mtimeMs: number }, [string]>();
const mockFindRunningUnityProcessForProject = jest.fn<Promise<{ pid: number } | null>, [string]>();

jest.mock('../port-resolver.js', () => ({
  resolveUnityConnection: (
    ...args: unknown[]
  ): Promise<{
    endpoint: { kind: 'unix-socket'; path: string };
    projectRoot: string | null;
    requestMetadata: null;
    shouldValidateProject: boolean;
  }> =>
    mockResolveUnityConnection(...args) as Promise<{
      endpoint: { kind: 'unix-socket'; path: string };
      projectRoot: string | null;
      requestMetadata: null;
      shouldValidateProject: boolean;
    }>,
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
}));

jest.mock('../unity-process.js', () => ({
  findRunningUnityProcessForProject: (projectRoot: string): Promise<{ pid: number } | null> =>
    mockFindRunningUnityProcessForProject(projectRoot),
}));

import { executeToolCommand, listAvailableTools, syncTools } from '../execute-tool.js';

describe('busy state detection order', () => {
  beforeEach(() => {
    mockResolveUnityConnection.mockReset();
    mockResolveUnityConnection.mockRejectedValue(new Error('RESOLVE_CALLED_BEFORE_BUSY_CHECK'));

    mockValidateProjectPath.mockReset();
    mockValidateProjectPath.mockReturnValue('/project');

    mockFindUnityProjectRoot.mockReset();
    mockFindUnityProjectRoot.mockReturnValue('/project');

    mockExistsSync.mockReset();
    mockExistsSync.mockImplementation((path: string) => path.endsWith('compiling.lock'));

    mockStatSync.mockReset();
    mockStatSync.mockReturnValue({ mtimeMs: Date.now() });

    mockFindRunningUnityProcessForProject.mockReset();
    mockFindRunningUnityProcessForProject.mockResolvedValue({ pid: 1234 });
  });

  it('checks busy state before resolving connection for tool execution', async () => {
    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).rejects.toThrow(
      'UNITY_COMPILING',
    );

    expect(mockResolveUnityConnection).not.toHaveBeenCalled();
  });

  it('checks busy state before resolving connection for list', async () => {
    await expect(listAvailableTools({ projectPath: '/project' })).rejects.toThrow(
      'UNITY_COMPILING',
    );

    expect(mockResolveUnityConnection).not.toHaveBeenCalled();
  });

  it('checks busy state before resolving connection for sync', async () => {
    await expect(syncTools({ projectPath: '/project' })).rejects.toThrow('UNITY_COMPILING');

    expect(mockResolveUnityConnection).not.toHaveBeenCalled();
  });

  it('treats a fresh serverstarting.lock as busy for execute-dynamic-code before resolving the connection', async () => {
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));
    mockResolveUnityConnection.mockRejectedValue(new Error('RESOLVE_SHOULD_NOT_RUN'));

    await expect(
      executeToolCommand('execute-dynamic-code', {}, { projectPath: '/project' }),
    ).rejects.toThrow('UNITY_SERVER_STARTING');

    expect(mockResolveUnityConnection).not.toHaveBeenCalled();
  });

  it('promotes get-logs resolution failures to UNITY_SERVER_STARTING when startup lock is fresh', async () => {
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));
    mockResolveUnityConnection.mockRejectedValue(
      new Error(
        'Could not read Unity server session from settings.\n\n  Settings file: /project/UserSettings/UnityMcpSettings.json',
      ),
    );

    await expect(executeToolCommand('get-logs', {}, { projectPath: '/project' })).rejects.toThrow(
      'UNITY_SERVER_STARTING',
    );

    expect(mockResolveUnityConnection).toHaveBeenCalled();
  });

  it('promotes list resolution failures to UNITY_SERVER_STARTING when startup lock is fresh', async () => {
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));
    mockResolveUnityConnection.mockRejectedValue(
      new Error(
        'Could not read Unity server session from settings.\n\n  Settings file: /project/UserSettings/UnityMcpSettings.json',
      ),
    );

    await expect(listAvailableTools({ projectPath: '/project' })).rejects.toThrow(
      'UNITY_SERVER_STARTING',
    );

    expect(mockResolveUnityConnection).toHaveBeenCalled();
  });

  it('promotes sync resolution failures to UNITY_SERVER_STARTING when startup lock is fresh', async () => {
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));
    mockResolveUnityConnection.mockRejectedValue(
      new Error(
        'Could not read Unity server session from settings.\n\n  Settings file: /project/UserSettings/UnityMcpSettings.json',
      ),
    );

    await expect(syncTools({ projectPath: '/project' })).rejects.toThrow('UNITY_SERVER_STARTING');

    expect(mockResolveUnityConnection).toHaveBeenCalled();
  });

  it('does not treat a fresh serverstarting.lock as busy when no Unity process is running', async () => {
    mockFindRunningUnityProcessForProject.mockResolvedValue(null);
    mockResolveUnityConnection.mockRejectedValue(new Error('RESOLVE_CALLED_WITHOUT_RUNNING_UNITY'));
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));

    await expect(listAvailableTools({ projectPath: '/project' })).rejects.toThrow(
      'RESOLVE_CALLED_WITHOUT_RUNNING_UNITY',
    );

    expect(mockResolveUnityConnection).toHaveBeenCalled();
  });

  it('allows the internal post-compile warmup path to bypass only serverstarting.lock', async () => {
    process.env['ULOOP_INTERNAL_SKIP_SERVER_STARTING_BUSY_CHECK'] = '1';
    mockResolveUnityConnection.mockRejectedValue(new Error('RESOLVE_CALLED_AFTER_SKIP'));
    mockExistsSync.mockImplementation((path: string) => path.endsWith('serverstarting.lock'));

    try {
      await expect(listAvailableTools({ projectPath: '/project' })).rejects.toThrow(
        'RESOLVE_CALLED_AFTER_SKIP',
      );
    } finally {
      delete process.env['ULOOP_INTERNAL_SKIP_SERVER_STARTING_BUSY_CHECK'];
    }

    expect(mockResolveUnityConnection).toHaveBeenCalled();
  });
});
