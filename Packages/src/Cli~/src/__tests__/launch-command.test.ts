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
  [string]
>();

jest.mock('../spinner.js', () => ({
  createSpinner: (message: string): { update: (message: string) => void; stop: () => void } =>
    mockCreateSpinner(message),
}));

jest.mock('../tool-settings-loader.js', () => ({
  isToolEnabled: jest.fn(),
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

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project']);

    expect(mockCreateSpinner).toHaveBeenCalledWith('Launching Unity...');
    expect(mockSpinnerUpdate).toHaveBeenCalledWith('Waiting for Unity to finish starting...');
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

    const program = new Command();
    registerLaunchCommand(program);

    await program.parseAsync(['node', 'uloop', 'launch', '/project']);

    expect(mockCreateSpinner).toHaveBeenCalledWith('Launching Unity...');
    expect(mockSpinnerUpdate).toHaveBeenCalledTimes(1);
    expect(mockSpinnerUpdate).toHaveBeenCalledWith('Waiting for Unity to finish starting...');
    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
    expect(waitForDynamicCodeReadyAfterLaunchMock).toHaveBeenCalledWith('/project');
    expect(prewarmDynamicCodeAfterLaunchMock).toHaveBeenCalledWith({ projectRoot: '/project' });
    expect(consoleLogSpy).not.toHaveBeenCalledWith('Waiting for execute-dynamic-code warmup...');
    expect(consoleLogSpy).not.toHaveBeenCalledWith('execute-dynamic-code is ready.');
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

    expect(mockCreateSpinner).toHaveBeenCalledWith('Launching Unity...');
    expect(mockSpinnerUpdate).not.toHaveBeenCalled();
    expect(mockSpinnerStop).toHaveBeenCalledTimes(1);
    expect(waitForLaunchReadyAfterLaunchMock).not.toHaveBeenCalled();
    expect(waitForDynamicCodeReadyAfterLaunchMock).not.toHaveBeenCalled();
    expect(prewarmDynamicCodeAfterLaunchMock).not.toHaveBeenCalled();
  });
});
