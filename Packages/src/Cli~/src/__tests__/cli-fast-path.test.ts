jest.mock(
  'launch-unity',
  () => ({
    orchestrateLaunch: jest.fn(),
    findRunningUnityProcess: jest.fn(),
    focusUnityProcess: jest.fn(),
  }),
  { virtual: true },
);

import {
  tryHandleFastExecuteDynamicCodeCommand,
  tryParseFastExecuteDynamicCodeCommand,
} from '../cli.js';

describe('tryParseFastExecuteDynamicCodeCommand', () => {
  it('parses execute-dynamic-code arguments without commander', () => {
    const command = tryParseFastExecuteDynamicCodeCommand([
      'execute-dynamic-code',
      '--code',
      'return "ok";',
      '--parameters',
      '{}',
      '--compile-only',
      'true',
      '--yield-to-foreground-requests',
      'true',
      '--project-path',
      '/project',
    ]);

    expect(command).toEqual({
      params: {
        Code: 'return "ok";',
        Parameters: {},
        CompileOnly: true,
        YieldToForegroundRequests: true,
      },
      globalOptions: {
        projectPath: '/project',
      },
    });
  });

  it('rejects inline port values so TCP cannot bypass project IPC', () => {
    const command = tryParseFastExecuteDynamicCodeCommand([
      'execute-dynamic-code',
      '--code=return \\!flag;',
      '--port=8901',
    ]);

    expect(command).toBeNull();
  });

  it('falls back when help is requested', () => {
    const command = tryParseFastExecuteDynamicCodeCommand(['execute-dynamic-code', '--help']);

    expect(command).toBeNull();
  });

  it('falls back when it encounters an unknown option', () => {
    const command = tryParseFastExecuteDynamicCodeCommand([
      'execute-dynamic-code',
      '--code',
      'return 1;',
      '--unknown',
      'value',
    ]);

    expect(command).toBeNull();
  });
});

describe('tryHandleFastExecuteDynamicCodeCommand', () => {
  it('dispatches execute-dynamic-code through the fast path', async () => {
    const executeToolCommandFn = jest.fn<
      Promise<void>,
      [string, Record<string, unknown>, object]
    >();
    const runWithErrorHandlingFn = jest.fn(
      async (fn: () => Promise<void>): Promise<void> => await fn(),
    );

    const handled = await tryHandleFastExecuteDynamicCodeCommand(
      ['execute-dynamic-code', '--code', 'return "fast";', '--project-path', '/project'],
      {
        executeToolCommandFn,
        isToolEnabledFn: jest.fn().mockReturnValue(true),
        findUnityProjectRootFn: jest.fn().mockReturnValue('/resolved-project'),
        runWithErrorHandlingFn,
        printToolDisabledErrorFn: jest.fn(),
        exitFn: (code: number): never => {
          throw new Error(`unexpected exit ${code}`);
        },
      },
    );

    expect(handled).toBe(true);
    expect(runWithErrorHandlingFn).toHaveBeenCalledTimes(1);
    expect(executeToolCommandFn).toHaveBeenCalledWith(
      'execute-dynamic-code',
      { Code: 'return "fast";' },
      { projectPath: '/project' },
    );
  });

  it('reuses one resolved project root when project-path is omitted', async () => {
    const executeToolCommandFn = jest.fn<
      Promise<void>,
      [string, Record<string, unknown>, object]
    >();
    const isToolEnabledFn = jest.fn().mockReturnValue(true);
    const findUnityProjectRootFn = jest.fn().mockReturnValue('/resolved-project');

    await tryHandleFastExecuteDynamicCodeCommand(['execute-dynamic-code', '--code', 'return 1;'], {
      executeToolCommandFn,
      isToolEnabledFn,
      findUnityProjectRootFn,
      runWithErrorHandlingFn: jest.fn(async (fn: () => Promise<void>): Promise<void> => await fn()),
      printToolDisabledErrorFn: jest.fn(),
      exitFn: (code: number): never => {
        throw new Error(`unexpected exit ${code}`);
      },
    });

    expect(findUnityProjectRootFn).toHaveBeenCalledTimes(1);
    expect(isToolEnabledFn).toHaveBeenCalledWith('execute-dynamic-code', '/resolved-project');
    expect(executeToolCommandFn).toHaveBeenCalledWith(
      'execute-dynamic-code',
      { Code: 'return 1;' },
      { projectPath: '/resolved-project' },
    );
  });

  it('stops early when execute-dynamic-code is disabled', async () => {
    await expect(
      tryHandleFastExecuteDynamicCodeCommand(['execute-dynamic-code', '--code', 'return 1;'], {
        executeToolCommandFn: jest.fn(),
        isToolEnabledFn: jest.fn().mockReturnValue(false),
        findUnityProjectRootFn: jest.fn().mockReturnValue('/resolved-project'),
        runWithErrorHandlingFn: jest.fn(),
        printToolDisabledErrorFn: jest.fn(),
        exitFn: (code: number): never => {
          throw new Error(`exit:${code}`);
        },
      }),
    ).rejects.toThrow('exit:1');
  });

  it('returns false when the fast path does not apply', async () => {
    const handled = await tryHandleFastExecuteDynamicCodeCommand(['get-logs'], {
      executeToolCommandFn: jest.fn(),
      isToolEnabledFn: jest.fn(),
      findUnityProjectRootFn: jest.fn(),
      runWithErrorHandlingFn: jest.fn(),
      printToolDisabledErrorFn: jest.fn(),
      exitFn: (code: number): never => {
        throw new Error(`unexpected exit ${code}`);
      },
    });

    expect(handled).toBe(false);
  });
});
