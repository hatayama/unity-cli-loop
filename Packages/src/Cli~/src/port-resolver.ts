/**
 * Unity connection resolution utility for CLI.
 * Resolves project-scoped IPC endpoints.
 */

// File paths are constructed from Unity project root detection, not from user input
/* eslint-disable security/detect-non-literal-fs-filename */

import { PRODUCT_DISPLAY_NAME } from './cli-constants';
import { readFile } from 'fs/promises';
import { existsSync } from 'fs';
import { join, resolve } from 'path';
import {
  canonicalizeProjectRoot,
  createProjectIpcEndpoint,
  type UnityConnectionEndpoint,
} from './ipc-endpoint.js';
import {
  findUnityProjectRoot,
  getUnitySettingsCandidatePaths,
  isUnityProject,
  hasUloopInstalled,
} from './project-root.js';
import type { UloopRequestMetadata } from './request-metadata.js';

export class UnityNotRunningError extends Error {
  constructor(public readonly projectRoot: string) {
    super('UNITY_NOT_RUNNING');
  }
}

export class UnityServerNotRunningError extends Error {
  constructor(public readonly projectRoot: string) {
    super('UNITY_SERVER_NOT_RUNNING');
  }
}

interface UnityMcpSettings {
  isServerRunning?: boolean;
  projectRootPath?: string;
  serverSessionId?: string;
}

export interface ResolvedUnityConnection {
  endpoint: UnityConnectionEndpoint;
  projectRoot: string | null;
  requestMetadata: UloopRequestMetadata | null;
  shouldValidateProject: boolean;
}

export function validateProjectPath(projectPath: string): string {
  const resolved = resolve(projectPath);

  if (!existsSync(resolved)) {
    throw new Error(`Path does not exist: ${resolved}`);
  }

  if (!isUnityProject(resolved)) {
    throw new Error(`Not a Unity project (Assets/ or ProjectSettings/ not found): ${resolved}`);
  }

  if (!hasUloopInstalled(resolved)) {
    throw new Error(
      `${PRODUCT_DISPLAY_NAME} is not installed in this project (UserSettings/UnityMcpSettings.json not found): ${resolved}`,
    );
  }

  return resolved;
}

function normalizeProjectRootPath(projectRoot: string): string {
  return projectRoot.replace(/\/+$/, '');
}

function createSettingsReadError(projectRoot: string): Error {
  const settingsPath = join(projectRoot, 'UserSettings/UnityMcpSettings.json');
  return new Error(
    `Could not read Unity server session from settings.\n\n` +
      `  Settings file: ${settingsPath}\n\n` +
      `Run 'uloop launch -r' to restart Unity.`,
  );
}

async function readUnitySettingsOrThrow(projectRoot: string): Promise<UnityMcpSettings> {
  for (const settingsPath of getUnitySettingsCandidatePaths(projectRoot)) {
    let content: string;
    try {
      content = await readFile(settingsPath, 'utf-8');
    } catch {
      continue;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(content);
    } catch {
      continue;
    }

    if (typeof parsed !== 'object' || parsed === null) {
      continue;
    }

    return parsed;
  }

  throw createSettingsReadError(projectRoot);
}

function tryCreateRequestMetadata(
  settings: UnityMcpSettings,
  projectRoot: string,
): UloopRequestMetadata | null {
  if (
    typeof settings.projectRootPath !== 'string' ||
    settings.projectRootPath.length === 0 ||
    typeof settings.serverSessionId !== 'string' ||
    settings.serverSessionId.length === 0
  ) {
    return null;
  }

  const normalizedProjectRoot = normalizeProjectRootPath(projectRoot);
  const normalizedSettingsProjectRoot = normalizeProjectRootPath(settings.projectRootPath);
  if (normalizedProjectRoot !== normalizedSettingsProjectRoot) {
    return null;
  }

  return {
    expectedProjectRoot: normalizedProjectRoot,
    expectedServerSessionId: settings.serverSessionId,
  };
}

export async function resolveUnityConnection(
  projectPath?: string,
): Promise<ResolvedUnityConnection> {
  let projectRoot: string | null;
  if (projectPath !== undefined) {
    projectRoot = validateProjectPath(projectPath);
  } else {
    projectRoot = findUnityProjectRoot();
    if (projectRoot === null) {
      throw new Error('Unity project not found. Use --project-path option to specify the target.');
    }
  }

  const canonicalProjectRoot = await canonicalizeProjectRoot(projectRoot);
  const settings = await readUnitySettingsOrThrow(canonicalProjectRoot);
  const endpoint = createProjectIpcEndpoint(canonicalProjectRoot);
  const requestMetadata = tryCreateRequestMetadata(settings, canonicalProjectRoot);

  return {
    endpoint,
    projectRoot: canonicalProjectRoot,
    requestMetadata,
    shouldValidateProject: requestMetadata === null,
  };
}
