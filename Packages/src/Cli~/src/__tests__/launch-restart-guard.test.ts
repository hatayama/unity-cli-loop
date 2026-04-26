import type { readFileSync, writeFileSync } from 'node:fs';

import {
  beginUnityRestartAttempt,
  getUnityRestartGuardFilePath,
  UNITY_RESTART_GUARD_COOLDOWN_MS,
  UnityRestartCooldownError,
  type UnityRestartGuardDependencies,
} from '../launch-restart-guard.js';

interface GuardRecord {
  projectPath?: string;
  startedAt?: number;
  pid?: number;
}

describe('launch restart guard', () => {
  let projectPath: string;
  let now: number;
  let files: Map<string, string>;
  let dependencies: UnityRestartGuardDependencies;

  beforeEach(() => {
    projectPath = '/UnityProject';
    now = 1000000;
    files = new Map<string, string>();
    dependencies = {
      mkdirSyncFn: (): undefined => undefined,
      readFileSyncFn: ((path: string): string => readExistingFile(path)) as typeof readFileSync,
      writeFileSyncFn: ((path: string, content: string): void => {
        files.set(path, content);
      }) as typeof writeFileSync,
      nowFn: (): number => now,
      pid: 2468,
    };
  });

  it('writes a restart guard record before relaunching Unity', () => {
    beginUnityRestartAttempt(projectPath, dependencies);

    const guardPath: string = getUnityRestartGuardFilePath(projectPath);
    const record: GuardRecord = readWrittenRecord(guardPath);

    expect(record).toEqual({
      projectPath,
      startedAt: 1000000,
      pid: 2468,
    });
  });

  it('blocks repeated Unity restart attempts during the cooldown window', () => {
    beginUnityRestartAttempt(projectPath, dependencies);
    now += 1000;

    expect(() => beginUnityRestartAttempt(projectPath, dependencies)).toThrow(
      UnityRestartCooldownError,
    );
  });

  it('allows a Unity restart attempt after the cooldown window', () => {
    beginUnityRestartAttempt(projectPath, dependencies);
    now += UNITY_RESTART_GUARD_COOLDOWN_MS;

    beginUnityRestartAttempt(projectPath, dependencies);

    const guardPath: string = getUnityRestartGuardFilePath(projectPath);
    const record: GuardRecord = readWrittenRecord(guardPath);
    expect(record.startedAt).toBe(1000000 + UNITY_RESTART_GUARD_COOLDOWN_MS);
  });

  it('allows a Unity restart attempt when the guard file disappears before read', () => {
    dependencies.readFileSyncFn = ((): string => {
      throw new Error('missing guard file');
    }) as unknown as typeof readFileSync;

    beginUnityRestartAttempt(projectPath, dependencies);

    const guardPath: string = getUnityRestartGuardFilePath(projectPath);
    const record: GuardRecord = readWrittenRecord(guardPath);
    expect(record.startedAt).toBe(1000000);
  });

  it('allows a Unity restart attempt when the guard file is corrupt', () => {
    const guardPath: string = getUnityRestartGuardFilePath(projectPath);
    files.set(guardPath, '{');

    beginUnityRestartAttempt(projectPath, dependencies);

    const record: GuardRecord = readWrittenRecord(guardPath);
    expect(record).toEqual({
      projectPath,
      startedAt: 1000000,
      pid: 2468,
    });
  });

  it('allows a Unity restart attempt when the guard record is future dated', () => {
    const guardPath: string = getUnityRestartGuardFilePath(projectPath);
    files.set(
      guardPath,
      `${JSON.stringify({
        projectPath,
        startedAt: now + 1,
        pid: 1357,
      })}\n`,
    );

    beginUnityRestartAttempt(projectPath, dependencies);

    const record: GuardRecord = readWrittenRecord(guardPath);
    expect(record).toEqual({
      projectPath,
      startedAt: 1000000,
      pid: 2468,
    });
  });

  function readExistingFile(path: string): string {
    const content: string | undefined = files.get(path);
    if (content === undefined) {
      throw new Error(`Missing file: ${path}`);
    }

    return content;
  }

  function readWrittenRecord(guardPath: string): GuardRecord {
    const content: string | undefined = files.get(guardPath);
    expect(content).toBeDefined();
    return JSON.parse(content ?? '{}') as GuardRecord;
  }
});
