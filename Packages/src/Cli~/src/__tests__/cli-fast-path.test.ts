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
      '--project-path',
      '/project',
    ]);

    expect(command).toEqual({
      params: {
        Code: 'return "ok";',
        Parameters: {},
        CompileOnly: true,
      },
      globalOptions: {
        projectPath: '/project',
        port: undefined,
      },
    });
  });

  it('supports inline values and unescapes shell-escaped exclamation marks', () => {
    const command = tryParseFastExecuteDynamicCodeCommand([
      'execute-dynamic-code',
      '--code=return \\!flag;',
      '--port=8901',
    ]);

    expect(command).toEqual({
      params: {
        Code: 'return !flag;',
      },
      globalOptions: {
        projectPath: undefined,
        port: '8901',
      },
    });
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
      ['execute-dynamic-code', '--code', 'return "fast";', '--port', '8901'],
      {
        executeToolCommandFn,
        isToolEnabledFn: jest.fn().mockReturnValue(true),
        runWithErrorHandlingFn,
        printToolDisabledErrorFn: jest.fn(),
        exitFn: ((code: number): never => {
          throw new Error(`unexpected exit ${code}`);
        }) as (code: number) => never,
      },
    );

    expect(handled).toBe(true);
    expect(runWithErrorHandlingFn).toHaveBeenCalledTimes(1);
    expect(executeToolCommandFn).toHaveBeenCalledWith(
      'execute-dynamic-code',
      { Code: 'return "fast";' },
      { projectPath: undefined, port: '8901' },
    );
  });

  it('stops early when execute-dynamic-code is disabled', async () => {
    await expect(
      tryHandleFastExecuteDynamicCodeCommand(['execute-dynamic-code', '--code', 'return 1;'], {
        executeToolCommandFn: jest.fn(),
        isToolEnabledFn: jest.fn().mockReturnValue(false),
        runWithErrorHandlingFn: jest.fn(),
        printToolDisabledErrorFn: jest.fn(),
        exitFn: ((code: number): never => {
          throw new Error(`exit:${code}`);
        }) as (code: number) => never,
      }),
    ).rejects.toThrow('exit:1');
  });

  it('returns false when the fast path does not apply', async () => {
    const handled = await tryHandleFastExecuteDynamicCodeCommand(['get-logs'], {
      executeToolCommandFn: jest.fn(),
      isToolEnabledFn: jest.fn(),
      runWithErrorHandlingFn: jest.fn(),
      printToolDisabledErrorFn: jest.fn(),
      exitFn: ((code: number): never => {
        throw new Error(`unexpected exit ${code}`);
      }) as (code: number) => never,
    });

    expect(handled).toBe(false);
  });
});
