import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import {
  resolvePortFromUnitySettings,
  validateProjectPath,
  resolveUnityConnection,
  resolveUnityPort,
} from '../port-resolver.js';

describe('resolvePortFromUnitySettings', () => {
  it('returns customPort when valid', () => {
    const port = resolvePortFromUnitySettings({
      isServerRunning: true,
      customPort: 8700,
    });

    expect(port).toBe(8700);
  });

  it('returns customPort regardless of isServerRunning flag', () => {
    const port = resolvePortFromUnitySettings({
      isServerRunning: false,
      customPort: 8711,
    });

    expect(port).toBe(8711);
  });

  it('returns null when customPort is invalid', () => {
    const port = resolvePortFromUnitySettings({
      isServerRunning: true,
      customPort: 0,
    });

    expect(port).toBeNull();
  });

  it('returns null when customPort is missing', () => {
    const port = resolvePortFromUnitySettings({
      isServerRunning: true,
    });

    expect(port).toBeNull();
  });

  it('returns null when port is not an integer', () => {
    const port = resolvePortFromUnitySettings({
      isServerRunning: true,
      customPort: 8700.1,
    });

    expect(port).toBeNull();
  });
});

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

describe('resolveUnityPort', () => {
  it('throws when both port and projectPath are specified', async () => {
    await expect(resolveUnityPort(8700, '/some/path')).rejects.toThrow(
      'Cannot specify both --port and --project-path',
    );
  });

  it('returns explicit port when only port is specified', async () => {
    const port = await resolveUnityPort(8711);
    expect(port).toBe(8711);
  });
});

describe('resolveUnityPort with project settings', () => {
  let tempProjectRoot: string;

  beforeEach(() => {
    tempProjectRoot = mkdtempSync(join(tmpdir(), 'unity-port-test-'));
    mkdirSync(join(tempProjectRoot, 'Assets'));
    mkdirSync(join(tempProjectRoot, 'ProjectSettings'));
    mkdirSync(join(tempProjectRoot, 'UserSettings'));
  });

  afterEach(() => {
    rmSync(tempProjectRoot, { recursive: true });
  });

  it('returns port when isServerRunning is false', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({ isServerRunning: false, customPort: 8700 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8700);
  });

  it('returns port when isServerRunning is true', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({ isServerRunning: true, customPort: 8711 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8711);
  });

  it('returns port when isServerRunning is undefined (old settings format)', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({ customPort: 8711 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8711);
  });

  it('throws when settings file has no valid port', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({ isServerRunning: true }),
    );

    await expect(resolveUnityPort(undefined, tempProjectRoot)).rejects.toThrow(
      'Could not read Unity server port from settings',
    );
  });

  it('throws when settings file contains invalid JSON', async () => {
    writeFileSync(join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'), 'not valid json{{{');

    await expect(resolveUnityPort(undefined, tempProjectRoot)).rejects.toThrow(
      'Could not read Unity server port from settings',
    );
  });

  it('returns port when primary settings file is missing but backup exists', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.bak'),
      JSON.stringify({ isServerRunning: true, customPort: 8722 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8722);
  });

  it('returns port when primary settings file is missing but temp exists', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.tmp'),
      JSON.stringify({ isServerRunning: true, customPort: 8723 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8723);
  });

  it('returns port from temp when both temp and backup exist', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.tmp'),
      JSON.stringify({ isServerRunning: true, customPort: 8724 }),
    );
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.bak'),
      JSON.stringify({ isServerRunning: true, customPort: 8725 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8724);
  });

  it('falls back to temp when primary settings file contains invalid JSON', async () => {
    writeFileSync(join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'), 'not valid json{{{');
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json.tmp'),
      JSON.stringify({ isServerRunning: true, customPort: 8726 }),
    );

    const port = await resolveUnityPort(undefined, tempProjectRoot);
    expect(port).toBe(8726);
  });
});

describe('resolveUnityConnection', () => {
  let tempProjectRoot: string;

  beforeEach(() => {
    tempProjectRoot = mkdtempSync(join(tmpdir(), 'unity-connection-test-'));
    mkdirSync(join(tempProjectRoot, 'Assets'));
    mkdirSync(join(tempProjectRoot, 'ProjectSettings'));
    mkdirSync(join(tempProjectRoot, 'UserSettings'));
  });

  afterEach(() => {
    rmSync(tempProjectRoot, { recursive: true });
  });

  it('uses fast request metadata when settings identity matches the resolved project', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({
        customPort: 8730,
        projectRootPath: tempProjectRoot,
        serverSessionId: 'session-123',
      }),
    );

    const connection = await resolveUnityConnection(undefined, tempProjectRoot);

    expect(connection).toEqual({
      port: 8730,
      projectRoot: tempProjectRoot,
      requestMetadata: {
        expectedProjectRoot: tempProjectRoot,
        expectedServerSessionId: 'session-123',
      },
      shouldValidateProject: false,
    });
  });

  it('falls back to legacy validation when server session identity is missing', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({
        customPort: 8731,
        projectRootPath: tempProjectRoot,
      }),
    );

    const connection = await resolveUnityConnection(undefined, tempProjectRoot);

    expect(connection.port).toBe(8731);
    expect(connection.projectRoot).toBe(tempProjectRoot);
    expect(connection.requestMetadata).toBeNull();
    expect(connection.shouldValidateProject).toBe(true);
  });

  it('falls back to legacy validation when settings project root differs from the resolved project', async () => {
    writeFileSync(
      join(tempProjectRoot, 'UserSettings/UnityMcpSettings.json'),
      JSON.stringify({
        customPort: 8732,
        projectRootPath: `${tempProjectRoot}-other`,
        serverSessionId: 'session-456',
      }),
    );

    const connection = await resolveUnityConnection(undefined, tempProjectRoot);

    expect(connection.port).toBe(8732);
    expect(connection.projectRoot).toBe(tempProjectRoot);
    expect(connection.requestMetadata).toBeNull();
    expect(connection.shouldValidateProject).toBe(true);
  });
});
