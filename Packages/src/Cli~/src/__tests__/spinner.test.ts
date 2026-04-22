import { createSpinner } from '../spinner.js';

describe('createSpinner', () => {
  let stderrWriteSpy: jest.SpiedFunction<typeof process.stderr.write>;
  let stdoutWriteSpy: jest.SpiedFunction<typeof process.stdout.write>;
  const originalStderrIsTTY = Object.getOwnPropertyDescriptor(process.stderr, 'isTTY');
  const originalStdoutIsTTY = Object.getOwnPropertyDescriptor(process.stdout, 'isTTY');

  beforeEach(() => {
    jest.useFakeTimers();
    stderrWriteSpy = jest.spyOn(process.stderr, 'write').mockImplementation(() => true);
    stdoutWriteSpy = jest.spyOn(process.stdout, 'write').mockImplementation(() => true);
  });

  afterEach(() => {
    stderrWriteSpy.mockRestore();
    stdoutWriteSpy.mockRestore();
    restoreIsTTY(process.stderr, originalStderrIsTTY);
    restoreIsTTY(process.stdout, originalStdoutIsTTY);
    jest.useRealTimers();
  });

  it('uses stderr when stderr is a TTY', () => {
    setTTYState(true, true);

    const spinner = createSpinner('Launching Unity...');

    expect(stderrWriteSpy).toHaveBeenCalledWith('\r\x1b[K⠋ Launching Unity...');
    expect(stdoutWriteSpy).not.toHaveBeenCalled();

    spinner.stop();
  });

  it('falls back to stdout when stderr is not a TTY', () => {
    setTTYState(false, true);

    const spinner = createSpinner('Launching Unity...');

    expect(stdoutWriteSpy).toHaveBeenCalledWith('\r\x1b[K⠋ Launching Unity...');
    expect(stderrWriteSpy).not.toHaveBeenCalled();

    spinner.stop();
  });

  it('uses stdout when stdout is preferred', () => {
    setTTYState(true, true);

    const spinner = createSpinner('Launching Unity...', 'stdout');

    expect(stdoutWriteSpy).toHaveBeenCalledWith('\r\x1b[K⠋ Launching Unity...');
    expect(stderrWriteSpy).not.toHaveBeenCalled();

    spinner.stop();
  });

  it('re-renders immediately when the message changes', () => {
    setTTYState(true, true);

    const spinner = createSpinner('Launching Unity...');
    spinner.update('Waiting for Unity to finish starting...');

    expect(stderrWriteSpy).toHaveBeenNthCalledWith(1, '\r\x1b[K⠋ Launching Unity...');
    expect(stderrWriteSpy).toHaveBeenNthCalledWith(
      2,
      '\r\x1b[K⠙ Waiting for Unity to finish starting...',
    );

    spinner.stop();
  });

  it('returns a no-op spinner when no tty output stream is available', () => {
    setTTYState(false, false);

    const spinner = createSpinner('Launching Unity...');
    spinner.update('Waiting for Unity to finish starting...');
    spinner.stop();

    expect(stderrWriteSpy).not.toHaveBeenCalled();
    expect(stdoutWriteSpy).not.toHaveBeenCalled();
  });

  function setTTYState(stderrIsTTY: boolean, stdoutIsTTY: boolean): void {
    Object.defineProperty(process.stderr, 'isTTY', {
      value: stderrIsTTY,
      configurable: true,
    });
    Object.defineProperty(process.stdout, 'isTTY', {
      value: stdoutIsTTY,
      configurable: true,
    });
  }
});

function restoreIsTTY(
  stream: NodeJS.WriteStream,
  descriptor: PropertyDescriptor | undefined,
): void {
  if (descriptor === undefined) {
    delete (stream as Partial<NodeJS.WriteStream>).isTTY;
    return;
  }

  Object.defineProperty(stream, 'isTTY', descriptor);
}
