import assert from 'node:assert';
import { createHash } from 'node:crypto';
import { realpath } from 'node:fs/promises';
import { join } from 'node:path';

// Project roots are validated before they are canonicalized.
/* eslint-disable security/detect-non-literal-fs-filename */

const IPC_HASH_BYTES_HEX_LENGTH = 16;
const IPC_UNIX_SOCKET_DIR = '/tmp/uloop';
const IPC_ENDPOINT_PREFIX = 'uLoopMCP';
const WINDOWS_PIPE_PREFIX = '\\\\.\\pipe\\uloop';

export type UnityConnectionEndpoint =
  | {
      kind: 'unix-socket';
      path: string;
    }
  | {
      kind: 'windows-pipe';
      path: string;
      pipeName: string;
    };

export async function canonicalizeProjectRoot(projectRoot: string): Promise<string> {
  assert(projectRoot.length > 0, 'projectRoot must not be empty');

  const canonicalProjectRoot = await realpath(projectRoot);
  return trimTrailingPathSeparators(canonicalProjectRoot);
}

export function createProjectIpcEndpoint(
  canonicalProjectRoot: string,
  platform: NodeJS.Platform = process.platform,
): UnityConnectionEndpoint {
  assert(canonicalProjectRoot.length > 0, 'canonicalProjectRoot must not be empty');

  const endpointName = createProjectEndpointName(canonicalProjectRoot);
  if (platform === 'win32') {
    const pipeName = `uloop-${endpointName}`;
    return {
      kind: 'windows-pipe',
      path: `${WINDOWS_PIPE_PREFIX}-${endpointName}`,
      pipeName,
    };
  }

  return {
    kind: 'unix-socket',
    path: join(IPC_UNIX_SOCKET_DIR, `${endpointName}.sock`),
  };
}

function createProjectEndpointName(canonicalProjectRoot: string): string {
  const hash = createHash('sha256')
    .update(canonicalProjectRoot, 'utf8')
    .digest('hex')
    .slice(0, IPC_HASH_BYTES_HEX_LENGTH);
  return `${IPC_ENDPOINT_PREFIX}-${hash}`;
}

function trimTrailingPathSeparators(path: string): string {
  if (/^[a-zA-Z]:[\\/]*$/.test(path)) {
    return `${path.slice(0, 2)}\\`;
  }

  const trimmedPath = path.replace(/[\\/]+$/, '');
  if (trimmedPath.length > 0) {
    return trimmedPath;
  }

  return path.startsWith('/') ? '/' : path;
}
