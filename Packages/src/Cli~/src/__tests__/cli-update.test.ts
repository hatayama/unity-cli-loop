type SpawnArgs = [string, string[], Record<string, unknown>?];

const mockSpawn = jest.fn<unknown, SpawnArgs>();

jest.mock('child_process', () => ({
  execFile: jest.fn(),
  spawn: (...args: SpawnArgs): unknown => mockSpawn(...args),
}));

jest.mock(
  'launch-unity',
  () => ({
    orchestrateLaunch: jest.fn(),
  }),
  { virtual: true },
);

import { getInstalledVersion, getUpdatePackageSpec, updateCli } from '../cli.js';

type CloseHandler = (code: number | null) => void;
type ErrorHandler = (error: Error) => void;
type DataHandler = (chunk: Buffer) => void;

interface MockChildProcess {
  stdout: {
    on: jest.Mock<void, [string, DataHandler]>;
  };
  on: jest.Mock<void, [string, CloseHandler | ErrorHandler]>;
  emitStdout: (chunk: string) => void;
  emitClose: (code: number | null) => void;
  emitError: (error: Error) => void;
}

function createMockChildProcess(): MockChildProcess {
  let closeHandler: CloseHandler | undefined;
  let errorHandler: ErrorHandler | undefined;
  let dataHandler: DataHandler | undefined;

  return {
    stdout: {
      on: jest.fn((event: string, handler: DataHandler) => {
        if (event === 'data') {
          dataHandler = handler;
        }
      }),
    },
    on: jest.fn((event: string, handler: CloseHandler | ErrorHandler) => {
      if (event === 'close') {
        closeHandler = handler as CloseHandler;
      }

      if (event === 'error') {
        errorHandler = handler as ErrorHandler;
      }
    }),
    emitStdout: (chunk: string): void => {
      dataHandler?.(Buffer.from(chunk));
    },
    emitClose: (code: number | null): void => {
      closeHandler?.(code);
    },
    emitError: (error: Error): void => {
      errorHandler?.(error);
    },
  };
}

describe('CLI update npm invocation', () => {
  const expectedNpmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm';

  beforeEach(() => {
    mockSpawn.mockReset();
    jest.spyOn(console, 'log').mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('gets installed version without enabling shell mode', () => {
    const child = createMockChildProcess();
    const callback = jest.fn();
    mockSpawn.mockReturnValue(child);

    getInstalledVersion(callback);

    expect(mockSpawn).toHaveBeenCalledWith(expectedNpmCommand, [
      'list',
      '-g',
      'uloop-cli',
      '--json',
    ]);
    expect(mockSpawn.mock.calls[0]).toHaveLength(2);

    child.emitStdout(JSON.stringify({ dependencies: { 'uloop-cli': { version: '1.8.0' } } }));
    child.emitClose(0);

    expect(callback).toHaveBeenCalledWith('1.8.0');
  });

  it('updates the CLI without enabling shell mode', () => {
    const updateChild = createMockChildProcess();
    const listChild = createMockChildProcess();
    mockSpawn.mockReturnValueOnce(updateChild).mockReturnValueOnce(listChild);

    updateCli();

    expect(mockSpawn).toHaveBeenNthCalledWith(
      1,
      expectedNpmCommand,
      ['install', '-g', 'uloop-cli@beta'],
      { stdio: 'inherit' },
    );
    const installOptions = mockSpawn.mock.calls[0]?.[2];
    expect(installOptions?.['shell']).toBeUndefined();

    updateChild.emitClose(0);

    expect(mockSpawn).toHaveBeenNthCalledWith(2, expectedNpmCommand, [
      'list',
      '-g',
      'uloop-cli',
      '--json',
    ]);
    expect(mockSpawn.mock.calls[1]).toHaveLength(2);

    listChild.emitStdout(JSON.stringify({ dependencies: { 'uloop-cli': { version: '1.7.1' } } }));
    listChild.emitClose(0);
  });

  it('uses the latest tag for stable update package specs', () => {
    expect(getUpdatePackageSpec('3.0.0')).toBe('uloop-cli@latest');
  });

  it('uses the prerelease channel tag for prerelease update package specs', () => {
    expect(getUpdatePackageSpec('3.0.0-beta.2')).toBe('uloop-cli@beta');
    expect(getUpdatePackageSpec('3.0.0-rc.1')).toBe('uloop-cli@rc');
  });
});
