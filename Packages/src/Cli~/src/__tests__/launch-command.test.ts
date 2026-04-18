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

    expect(waitForDynamicCodeReadyAfterLaunchMock).toHaveBeenCalledWith('/project');
    expect(prewarmDynamicCodeAfterLaunchMock).toHaveBeenCalledWith({ projectRoot: '/project' });
    expect(consoleLogSpy).not.toHaveBeenCalledWith('Waiting for execute-dynamic-code warmup...');
    expect(consoleLogSpy).not.toHaveBeenCalledWith('execute-dynamic-code is ready.');
  });
});
