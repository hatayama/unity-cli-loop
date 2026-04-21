/**
 * Terminal spinner for showing loading state during async operations.
 */

// Array index access is safe here (modulo operation keeps it in bounds)
/* eslint-disable security/detect-object-injection */

const SPINNER_FRAMES = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'] as const;
const FRAME_INTERVAL_MS = 80;

interface Spinner {
  update(message: string): void;
  stop(): void;
}

function resolveSpinnerStream(): NodeJS.WriteStream | null {
  if (process.stderr.isTTY) {
    return process.stderr;
  }

  if (process.stdout.isTTY) {
    return process.stdout;
  }

  return null;
}

/**
 * Create a terminal spinner that displays a rotating animation with a message.
 * Returns a Spinner object with update() and stop() methods.
 */
export function createSpinner(initialMessage: string): Spinner {
  const outputStream = resolveSpinnerStream();
  if (outputStream === null) {
    return {
      update: (): void => {},
      stop: (): void => {},
    };
  }

  let frameIndex = 0;
  let currentMessage = initialMessage;

  const render = (): void => {
    const frame = SPINNER_FRAMES[frameIndex];
    outputStream.write(`\r\x1b[K${frame} ${currentMessage}`);
    frameIndex = (frameIndex + 1) % SPINNER_FRAMES.length;
  };

  render();
  const intervalId = setInterval(render, FRAME_INTERVAL_MS);

  return {
    update(message: string): void {
      currentMessage = message;
      render();
    },
    stop(): void {
      clearInterval(intervalId);
      outputStream.write('\r\x1b[K');
    },
  };
}
