import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'fs';
import { tmpdir } from 'os';
import { join } from 'path';
import { createProjectIpcEndpoint } from '../ipc-endpoint.js';
import { validateProjectPath, resolveUnityConnection } from '../port-resolver.js';

/* eslint-disable security/detect-non-literal-fs-filename */

describe('validateProjectPath', () => {
  it('throws when path does not exist', () => {
    expect(() => validateProjectPath('/nonexistent/path/to/project')).toThrow(
      'Path does not exist: /nonexistent/path/to/project',
    );
  });

  it('throws when path is not a Unity project', () => {
    expect(() => validateProjectPath(tmpdir())).toThrow('Not a Unity project');
  });
});

describe('resolveUnityConnection', () => {
  let tempProjectRoot: string;

  beforeEach(() => {
    tempProjectRoot = createTempUnityProject('unity-connection-test-');
  });

  afterEach(() => {
    rmSync(tempProjectRoot, { recursive: true });
  });

  it('uses project IPC endpoint for the normal route', async () => {
    writeSettings(tempProjectRoot, {
      isServerRunning: true,
    });

    const connection = await resolveUnityConnection(tempProjectRoot);
    const canonicalProjectRoot = realpathSync(tempProjectRoot);

    expect(connection.endpoint).toEqual(createProjectIpcEndpoint(canonicalProjectRoot));
    expect(connection.projectRoot).toBe(canonicalProjectRoot);
  });

  it('uses fast request metadata when settings identity matches the canonical project', async () => {
    const canonicalProjectRoot = realpathSync(tempProjectRoot);
    writeSettings(tempProjectRoot, {
      projectRootPath: canonicalProjectRoot,
      serverSessionId: 'session-123',
    });

    const connection = await resolveUnityConnection(tempProjectRoot);

    expect(connection).toEqual({
      endpoint: createProjectIpcEndpoint(canonicalProjectRoot),
      projectRoot: canonicalProjectRoot,
      requestMetadata: {
        expectedProjectRoot: canonicalProjectRoot,
        expectedServerSessionId: 'session-123',
      },
      shouldValidateProject: false,
    });
  });

  it('falls back to legacy validation when server session identity is missing', async () => {
    const canonicalProjectRoot = realpathSync(tempProjectRoot);
    writeSettings(tempProjectRoot, {
      projectRootPath: canonicalProjectRoot,
    });

    const connection = await resolveUnityConnection(tempProjectRoot);

    expect(connection.endpoint).toEqual(createProjectIpcEndpoint(canonicalProjectRoot));
    expect(connection.projectRoot).toBe(canonicalProjectRoot);
    expect(connection.requestMetadata).toBeNull();
    expect(connection.shouldValidateProject).toBe(true);
  });

  it('falls back to legacy validation when settings project root differs from the resolved project', async () => {
    const canonicalProjectRoot = realpathSync(tempProjectRoot);
    writeSettings(tempProjectRoot, {
      projectRootPath: `${canonicalProjectRoot}-other`,
      serverSessionId: 'session-456',
    });

    const connection = await resolveUnityConnection(tempProjectRoot);

    expect(connection.endpoint).toEqual(createProjectIpcEndpoint(canonicalProjectRoot));
    expect(connection.projectRoot).toBe(canonicalProjectRoot);
    expect(connection.requestMetadata).toBeNull();
    expect(connection.shouldValidateProject).toBe(true);
  });

  it('falls back to temp settings when primary settings file contains invalid JSON', async () => {
    const canonicalProjectRoot = realpathSync(tempProjectRoot);
    writeFileSync(join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'), 'not valid json{{{');
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.tmp'),
      JSON.stringify({
        projectRootPath: canonicalProjectRoot,
        serverSessionId: 'session-from-temp',
      }),
    );

    const connection = await resolveUnityConnection(tempProjectRoot);

    expect(connection.endpoint).toEqual(createProjectIpcEndpoint(canonicalProjectRoot));
    expect(connection.requestMetadata).toEqual({
      expectedProjectRoot: canonicalProjectRoot,
      expectedServerSessionId: 'session-from-temp',
    });
  });
});

function createTempUnityProject(prefix: string): string {
  const tempProjectRoot = mkdtempSync(join(tmpdir(), prefix));
  mkdirSync(join(tempProjectRoot, 'Assets'));
  mkdirSync(join(tempProjectRoot, 'ProjectSettings'));
  mkdirSync(join(tempProjectRoot, 'UserSettings'));
  return tempProjectRoot;
}

function writeSettings(tempProjectRoot: string, settings: Record<string, unknown>): void {
  writeFileSync(
    join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
    JSON.stringify(settings),
  );
}
