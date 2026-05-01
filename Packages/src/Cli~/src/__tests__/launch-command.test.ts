jest.mock(
  'launch-unity',
  () => ({
    orchestrateLaunch: jest.fn(),
  }),
  { virtual: true },
);

jest.mock('../launch-readiness.js', () => ({
  waitForDynamicCodeReadyAfterLaunch: jest.fn(),
  waitForLaunchReadyAfterLaunch: jest.fn(),
}));

jest.mock('../execute-tool.js', () => ({
  prewarmDynamicCodeAfterLaunch: jest.fn(),
}));

const mockSpinnerUpdate = jest.fn<void, [string]>();
const mockSpinnerStop = jest.fn<void, []>();
const mockCreateSpinner = jest.fn<
  { update: (message: string) => void; stop: () => void },
  [string, ('auto' | 'stdout' | 'stderr')?]
>();

jest.mock('../spinner.js', () => ({
  createSpinner: (
    message: string,
    preference?: 'auto' | 'stdout' | 'stderr',
  ): { update: (message: string) => void; stop: () => void } =>
    mockCreateSpinner(message, preference),
}));

jest.mock('../tool-settings-loader.js', () => ({
  isToolEnabled: jest.fn(),
}));

const mockBeginUnityRestartAttempt = jest.fn<void, [string]>();

jest.mock('../launch-restart-guard.js', () => ({
  beginUnityRestartAttempt: (projectPath: string): void =>
    mockBeginUnityRestartAttempt(projectPath),
}));

const mockFindUnityProjectRoot = jest.fn<string | null, []>();
const mockIsUnityProject = jest.fn<boolean, [string]>();

jest.mock('../project-root.js', () => ({
  findUnityProjectRoot: (): string | null => mockFindUnityProjectRoot(),
  isUnityProject: (projectPath: string): boolean => mockIsUnityProject(projectPath),
}));

import { Command } from 'commander';
import { orchestrateLaunch } from 'launch-unity';
import {
  waitForDynamicCodeReadyAfterLaunch,
  waitForLaunchReadyAfterLaunch,
} from '../launch-readiness.js';
import { prewarmDynamicCodeAfterLaunch } from '../execute-tool.js';
import { isToolEnabled } from '../tool-settings-loader.js';
import { registerLaunchCommand } from '../commands/launch.js';
import { createProjectIpcEndpoint } from '../ipc-endpoint.js';

describe('launch command', () => {
  const orchestrateLaunchMock = jest.mocked(orchestrateLaunch);
  const waitForDynamicCodeReadyAfterLaunchMock = jest.mocked(waitForDynamicCodeReadyAfterLaunch);
  const waitForLaunchReadyAfterLaunchMock = jest.mocked(waitForLaunchReadyAfterLaunch);
  const prewarmDynamicCodeAfterLaunchMock = jest.mocked(prewarmDynamicCodeAfterLaunch);
  const isToolEnabledMock = jest.mocked(isToolEnabled);
  let consoleLogSpy: jest.SpyInstance;

  beforeEach(() => {
    jest.clearAllMocks();
    consoleLogSpy = jest.spyOn(console, 'log').mockImplementation(() => undefined);
    mockCreateSpinner.mockReset();
    mockCreateSpinner.mockReturnValue({
      update: (message: string): void => mockSpinnerUpdate(message),
      stop: (): void => mockSpinnerStop(),
    });
    mockSpinnerUpdate.mockReset();
    mockSpinnerStop.mockReset();
    mockBeginUnityRestartAttempt.mockReset();
    mockFindUnityProjectRoot.mockReset();
    mockFindUnityProjectRoot.mockReturnValue('/project');
    mockIsUnityProject.mockReset();
    mockIsUnityProject.mockReturnValue(true);
  });

  afterEach(() => {
    consoleLogSpy.mockRestore();
  });

  it('waits for general launch readiness but skips dynamic-code warmup when execute-dynamic-code is disabled', async () => {
    orchestrateLaunchMock.mockResolvedValue({
      action: 'launched',
      projectPath: '/project',
      unityVersion: '2022.3.0f1',
    });
    isToolEnabledMock.mockReturnValue(false);
    waitForLaunchReadyAfterLaunchMock.mockResolvedValue({
      endpoint: createProjectIpcEndpoint('/project'),
      projectRoot: '/project',
      requestMetadata: {
        expectedProjectRoot: '/project',
        expectedServerSessionId: 'session-1',
      },
      shouldValidateProject: false,
    });

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project']);

    expect(mockCreateSpinner).toHaveBeenCalledWith(
      'Waiting for Unity to finish starting...',
      'stdout',
    );
    expect(mockSpinnerUpdate).not.toHaveBeenCalled();
    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
    expect(waitForLaunchReadyAfterLaunchMock).toHaveBeenCalledWith('/project');
    expect(waitForDynamicCodeReadyAfterLaunchMock).not.toHaveBeenCalled();
    expect(prewarmDynamicCodeAfterLaunchMock).not.toHaveBeenCalled();
  });

  it('warms dynamic code after launch without printing internal warmup progress', async () => {
    orchestrateLaunchMock.mockResolvedValue({
      action: 'launched',
      projectPath: '/project',
      unityVersion: '2022.3.0f1',
    });
    isToolEnabledMock.mockReturnValue(true);
    waitForDynamicCodeReadyAfterLaunchMock.mockResolvedValue({
      endpoint: createProjectIpcEndpoint('/project'),
      projectRoot: '/project',
      requestMetadata: {
        expectedProjectRoot: '/project',
        expectedServerSessionId: 'session-1',
      },
      shouldValidateProject: false,
    });

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project']);

    expect(mockCreateSpinner).toHaveBeenCalledWith(
      'Waiting for Unity to finish starting...',
      'stdout',
    );
    expect(mockSpinnerUpdate).not.toHaveBeenCalled();
    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
    expect(waitForDynamicCodeReadyAfterLaunchMock).toHaveBeenCalledWith('/project');
    expect(prewarmDynamicCodeAfterLaunchMock).toHaveBeenCalledWith({
      projectRoot: '/project',
    });
    expect(consoleLogSpy).not.toHaveBeenCalledWith('Waiting for execute-dynamic-code warmup...');
    expect(consoleLogSpy).not.toHaveBeenCalledWith('execute-dynamic-code is ready.');
  });

  it('shows startup spinner before orchestrateLaunch resolves', async () => {
    let resolveLaunch:
      | ((value: { action: 'focused'; projectPath: string; pid: number }) => void)
      | undefined;
    orchestrateLaunchMock.mockReturnValue(
      new Promise((resolve) => {
        resolveLaunch = resolve;
      }),
    );

    const program = new Command();
    registerLaunchCommand(program);

    const parsePromise: Promise<Command> = program.parseAsync([
      'node',
      'uloop',
      'launch',
      '/project',
    ]);
    await Promise.resolve();

    expect(mockCreateSpinner).toHaveBeenCalledWith(
      'Waiting for Unity to finish starting...',
      'stdout',
    );
    expect(mockSpinnerStop).not.toHaveBeenCalled();

    resolveLaunch?.({
      action: 'focused',
      projectPath: '/project',
      pid: 4321,
    });
    await parsePromise;
  });

  it('stops spinner without readiness waits when Unity is already running', async () => {
    orchestrateLaunchMock.mockResolvedValue({
      action: 'focused',
      projectPath: '/project',
      pid: 4321,
    });

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project']);

    expect(mockCreateSpinner).toHaveBeenCalledWith(
      'Waiting for Unity to finish starting...',
      'stdout',
    );
    expect(mockSpinnerUpdate).not.toHaveBeenCalled();
    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
    expect(waitForLaunchReadyAfterLaunchMock).not.toHaveBeenCalled();
    expect(waitForDynamicCodeReadyAfterLaunchMock).not.toHaveBeenCalled();
    expect(prewarmDynamicCodeAfterLaunchMock).not.toHaveBeenCalled();
  });

  it('records a restart guard before restart orchestration touches Unity', async () => {
    const observedCalls: string[] = [];
    mockBeginUnityRestartAttempt.mockImplementation((projectPath: string): void => {
      observedCalls.push(`guard:${projectPath}`);
    });
    orchestrateLaunchMock.mockImplementation(() => {
      observedCalls.push('orchestrate');
      return Promise.resolve({
        action: 'focused',
        projectPath: '/project',
        pid: 4321,
      });
    });

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project', '--restart']);

    expect(observedCalls).toEqual(['guard:/project', 'orchestrate']);
    expect(mockBeginUnityRestartAttempt).toHaveBeenCalledWith('/project');
    expect(orchestrateLaunchMock).toHaveBeenCalledWith(
      expect.objectContaining({
        projectPath: '/project',
        restart: true,
      }),
    );
  });

  it('stops before restart orchestration when the restart guard blocks', async () => {
    mockBeginUnityRestartAttempt.mockImplementation((): void => {
      throw new Error('restart blocked');
    });

    const program = new Command();
    registerLaunchCommand(program);

    await expect(
      program.parseAsync(['node', 'uloop', 'launch', '/project', '--restart']),
    ).rejects.toThrow('restart blocked');

    expect(orchestrateLaunchMock).not.toHaveBeenCalled();
    expect(mockCreateSpinner).not.toHaveBeenCalled();
  });

  it('skips the restart guard for invalid explicit project paths', async () => {
    mockIsUnityProject.mockReturnValue(false);
    orchestrateLaunchMock.mockResolvedValue({
      action: 'focused',
      projectPath: '/not-a-project',
      pid: 4321,
    });

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/not-a-project', '--restart']);

    expect(mockBeginUnityRestartAttempt).not.toHaveBeenCalled();
    expect(orchestrateLaunchMock).toHaveBeenCalledWith(
      expect.objectContaining({
        projectPath: '/not-a-project',
        restart: true,
      }),
    );
  });
});
