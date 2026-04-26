import assert from 'node:assert';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

export const UNITY_RESTART_GUARD_COOLDOWN_MS = 120000;
const UNITY_RESTART_GUARD_DIR = '.uloop';
const UNITY_RESTART_GUARD_FILE = 'launch-restart-guard.json';

interface UnityRestartGuardRecord {
  projectPath: string;
  startedAt: number;
  pid: number;
}

export interface UnityRestartGuardDependencies {
  mkdirSyncFn: typeof mkdirSync;
  readFileSyncFn: typeof readFileSync;
  writeFileSyncFn: typeof writeFileSync;
  nowFn: () => number;
  pid: number;
}

const defaultDependencies: UnityRestartGuardDependencies = {
  mkdirSyncFn: mkdirSync,
  readFileSyncFn: readFileSync,
  writeFileSyncFn: writeFileSync,
  nowFn: () => Date.now(),
  pid: process.pid,
};

export class UnityRestartCooldownError extends Error {
  public constructor(projectPath: string, remainingMilliseconds: number) {
    const remainingSeconds = Math.ceil(remainingMilliseconds / 1000);
    super(
      `Refusing to restart Unity for ${projectPath} because a restart was requested recently. ` +
        `Wait ${remainingSeconds}s before retrying, or launch without --restart.`,
    );
    this.name = 'UnityRestartCooldownError';
  }
}

export function getUnityRestartGuardFilePath(projectPath: string): string {
  assert(projectPath.length > 0, 'projectPath must not be empty');
  return join(projectPath, UNITY_RESTART_GUARD_DIR, UNITY_RESTART_GUARD_FILE);
}

export function beginUnityRestartAttempt(
  projectPath: string,
  dependencies: UnityRestartGuardDependencies = defaultDependencies,
): void {
  assert(projectPath.length > 0, 'projectPath must not be empty');
  assertUnityRestartAllowed(projectPath, dependencies);

  const guardPath = getUnityRestartGuardFilePath(projectPath);
  dependencies.mkdirSyncFn(dirname(guardPath), { recursive: true });
  const record: UnityRestartGuardRecord = {
    projectPath,
    startedAt: dependencies.nowFn(),
    pid: dependencies.pid,
  };
  dependencies.writeFileSyncFn(guardPath, `${JSON.stringify(record, null, 2)}\n`, 'utf8');
}

function assertUnityRestartAllowed(
  projectPath: string,
  dependencies: UnityRestartGuardDependencies = defaultDependencies,
): void {
  assert(projectPath.length > 0, 'projectPath must not be empty');

  const guardPath = getUnityRestartGuardFilePath(projectPath);
  const record = readGuardRecordOrNull(guardPath, dependencies);
  if (record === null) {
    return;
  }
  const elapsedMilliseconds = dependencies.nowFn() - record.startedAt;
  if (elapsedMilliseconds < 0 || elapsedMilliseconds >= UNITY_RESTART_GUARD_COOLDOWN_MS) {
    return;
  }

  throw new UnityRestartCooldownError(
    projectPath,
    UNITY_RESTART_GUARD_COOLDOWN_MS - elapsedMilliseconds,
  );
}

function readGuardRecordOrNull(
  guardPath: string,
  dependencies: UnityRestartGuardDependencies,
): UnityRestartGuardRecord | null {
  try {
    const content = dependencies.readFileSyncFn(guardPath, 'utf8');
    const parsed = JSON.parse(content) as Partial<UnityRestartGuardRecord>;
    assert(typeof parsed.projectPath === 'string', 'restart guard projectPath must be a string');
    assert(typeof parsed.startedAt === 'number', 'restart guard startedAt must be a number');
    assert(typeof parsed.pid === 'number', 'restart guard pid must be a number');
    return {
      projectPath: parsed.projectPath,
      startedAt: parsed.startedAt,
      pid: parsed.pid,
    };
  } catch {
    return null;
  }
}
