import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { canonicalizeProjectRoot, createProjectIpcEndpoint } from '../ipc-endpoint.js';

/* eslint-disable security/detect-non-literal-fs-filename */

function expectedEndpointHash(canonicalProjectRoot: string): string {
  return createHash('sha256').update(canonicalProjectRoot, 'utf8').digest('hex').slice(0, 16);
}

describe('createProjectIpcEndpoint', () => {
  it('creates Unix domain socket path from canonical project root', () => {
    const canonicalProjectRoot = '/Users/example/My Unity Project';
    const endpoint = createProjectIpcEndpoint(canonicalProjectRoot, 'darwin');
    const hash = expectedEndpointHash(canonicalProjectRoot);

    expect(endpoint).toEqual({
      kind: 'unix-socket',
      path: `/tmp/uloop/uLoopMCP-${hash}.sock`,
    });
  });

  it('creates Windows named pipe path from canonical project root', () => {
    const canonicalProjectRoot = 'C:\\Users\\example\\My Unity Project';
    const endpoint = createProjectIpcEndpoint(canonicalProjectRoot, 'win32');
    const hash = expectedEndpointHash(canonicalProjectRoot);

    expect(endpoint).toEqual({
      kind: 'windows-pipe',
      path: `\\\\.\\pipe\\uloop-uLoopMCP-${hash}`,
      pipeName: `uloop-uLoopMCP-${hash}`,
    });
  });
});

describe('canonicalizeProjectRoot', () => {
  let tempRoot: string;

  beforeEach(() => {
    tempRoot = mkdtempSync(join(tmpdir(), 'uloop-ipc-endpoint-'));
  });

  afterEach(() => {
    rmSync(tempRoot, { recursive: true, force: true });
  });

  it('resolves symlinks before endpoint hashing', async () => {
    const projectRoot = join(tempRoot, 'project');
    const symlinkPath = join(tempRoot, 'project-link');
    mkdirSync(projectRoot);
    symlinkSync(projectRoot, symlinkPath, process.platform === 'win32' ? 'junction' : 'dir');

    const canonicalProjectRoot = await canonicalizeProjectRoot(`${symlinkPath}/`);

    expect(canonicalProjectRoot).toBe(realpathSync(projectRoot));
  });

  it('preserves the POSIX filesystem root while trimming trailing separators', async () => {
    if (process.platform === 'win32') {
      return;
    }

    const canonicalProjectRoot = await canonicalizeProjectRoot('/');

    expect(canonicalProjectRoot).toBe('/');
  });
});
