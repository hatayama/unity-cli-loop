// Test helpers use dynamic paths for temp directories and console assertions
/* eslint-disable security/detect-non-literal-fs-filename, no-console */

import { mkdirSync, writeFileSync, rmSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import {
  findUnityProjectRoot,
  hasUloopInstalled,
  resetMultipleProjectsWarning,
} from '../project-root.js';

function createUnityProject(basePath: string, name: string): string {
  const projectPath = join(basePath, name);
  mkdirSync(join(projectPath, 'Assets'), { recursive: true });
  mkdirSync(join(projectPath, 'ProjectSettings'), { recursive: true });
  mkdirSync(join(projectPath, 'UserSettings'), { recursive: true });
  writeFileSync(join(projectPath, 'UserSettings/UnityMcpSettings.json'), '{}');
  return projectPath;
}

describe('findUnityProjectRoot', () => {
  let testDir: string;

  beforeEach(() => {
    resetMultipleProjectsWarning();
    testDir = join(tmpdir(), `uloop-test-${Date.now()}-${Math.random().toString(36).slice(2)}`);
    mkdirSync(testDir, { recursive: true });
  });

  afterEach(() => {
    rmSync(testDir, { recursive: true, force: true });
  });

  it('returns null when no Unity project is found', () => {
    const result = findUnityProjectRoot(testDir);

    expect(result).toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('returns project path without warning when single project found', () => {
    const projectPath = createUnityProject(testDir, 'MyProject');

    const result = findUnityProjectRoot(testDir);

    expect(result).toBe(projectPath);
    expect(console.error).not.toHaveBeenCalled();
  });

  it('returns the current directory immediately when it is already the Unity project root', () => {
    createUnityProject(testDir, 'ChildProject');
    mkdirSync(join(testDir, 'Assets'), { recursive: true });
    mkdirSync(join(testDir, 'ProjectSettings'), { recursive: true });
    mkdirSync(join(testDir, 'UserSettings'), { recursive: true });
    writeFileSync(join(testDir, 'UserSettings/UnityMcpSettings.json'), '{}');

    const result = findUnityProjectRoot(testDir);

    expect(result).toBe(testDir);
    expect(console.error).not.toHaveBeenCalled();
  });

  it('warns once when multiple projects found', () => {
    createUnityProject(testDir, 'ProjectA');
    createUnityProject(testDir, 'ProjectB');

    findUnityProjectRoot(testDir);

    expect(console.error).toHaveBeenCalled();
    const calls = (console.error as jest.Mock).mock.calls.map((c: unknown[]) => c[0] as string);
    const warningLine = calls.find((msg: string) => msg.includes('Multiple Unity projects'));
    expect(warningLine).toBeDefined();
    const actionLine = calls.find((msg: string) => msg.includes('--project-path'));
    expect(actionLine).toBeDefined();
  });

  it('does not warn on second call (once-per-process)', () => {
    createUnityProject(testDir, 'ProjectA');
    createUnityProject(testDir, 'ProjectB');

    findUnityProjectRoot(testDir);
    (console.error as jest.Mock).mockClear();

    findUnityProjectRoot(testDir);

    expect(console.error).not.toHaveBeenCalled();
  });

  it('warns again after resetMultipleProjectsWarning()', () => {
    createUnityProject(testDir, 'ProjectA');
    createUnityProject(testDir, 'ProjectB');

    findUnityProjectRoot(testDir);
    (console.error as jest.Mock).mockClear();

    resetMultipleProjectsWarning();
    findUnityProjectRoot(testDir);

    expect(console.error).toHaveBeenCalled();
    const calls = (console.error as jest.Mock).mock.calls.map((c: unknown[]) => c[0] as string);
    const warningLine = calls.find((msg: string) => msg.includes('Multiple Unity projects'));
    expect(warningLine).toBeDefined();
  });

  it('returns first project alphabetically when multiple found', () => {
    createUnityProject(testDir, 'Zebra');
    createUnityProject(testDir, 'Alpha');

    const result = findUnityProjectRoot(testDir);

    expect(result).toBe(join(testDir, 'Alpha'));
  });
});

describe('hasUloopInstalled', () => {
  let testDir: string;

  beforeEach(() => {
    testDir = join(
      tmpdir(),
      `uloop-installed-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    );
    mkdirSync(join(testDir, 'Assets'), { recursive: true });
    mkdirSync(join(testDir, 'ProjectSettings'), { recursive: true });
    mkdirSync(join(testDir, 'UserSettings'), { recursive: true });
  });

  afterEach(() => {
    rmSync(testDir, { recursive: true, force: true });
  });

  it('returns true when only backup settings file exists', () => {
    writeFileSync(join(testDir, 'UserSettings/UnityMcpSettings.json.bak'), '{}');

    expect(hasUloopInstalled(testDir)).toBe(true);
  });

  it('returns true when only temp settings file exists', () => {
    writeFileSync(join(testDir, 'UserSettings/UnityMcpSettings.json.tmp'), '{}');

    expect(hasUloopInstalled(testDir)).toBe(true);
  });
});
