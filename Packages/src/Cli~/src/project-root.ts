/**
 * Unity project root detection utility.
 * Searches child directories first (up to 3 levels deep), then parent directories.
 */

// Path traversal is intentional for finding Unity project root
/* eslint-disable security/detect-non-literal-fs-filename */

import { existsSync, readdirSync, Dirent } from 'fs';
import { join, dirname } from 'path';

const CHILD_SEARCH_MAX_DEPTH = 3;

const EXCLUDED_DIRS = new Set([
  'node_modules',
  '.git',
  'Temp',
  'obj',
  'Build',
  'Builds',
  'Logs',
  'Library',
]);

export function isUnityProject(dirPath: string): boolean {
  const hasAssets = existsSync(join(dirPath, 'Assets'));
  const hasProjectSettings = existsSync(join(dirPath, 'ProjectSettings'));
  return hasAssets && hasProjectSettings;
}

export function getUnitySettingsCandidatePaths(dirPath: string): string[] {
  const settingsPath = join(dirPath, 'UserSettings/UnityMcpSettings.json');
  return [settingsPath, `${settingsPath}.tmp`, `${settingsPath}.bak`];
}

export function hasUloopInstalled(dirPath: string): boolean {
  return getUnitySettingsCandidatePaths(dirPath).some((path) => existsSync(path));
}

function isUnityProjectWithUloop(dirPath: string): boolean {
  return isUnityProject(dirPath) && hasUloopInstalled(dirPath);
}

function findUnityProjectsInChildren(startPath: string, maxDepth: number): string[] {
  const projects: string[] = [];

  function scan(currentPath: string, depth: number): void {
    if (depth > maxDepth) {
      return;
    }

    if (!existsSync(currentPath)) {
      return;
    }

    if (isUnityProjectWithUloop(currentPath)) {
      projects.push(currentPath);
      return;
    }

    let entries: Dirent[];
    try {
      // eslint-disable-next-line @typescript-eslint/no-unnecessary-type-assertion -- ts-jest requires explicit Dirent[] type due to Node.js 22+ generic Dirent<T> changes
      entries = readdirSync(currentPath, { withFileTypes: true }) as Dirent[];
    } catch {
      return;
    }

    for (const entry of entries) {
      if (!entry.isDirectory()) {
        continue;
      }

      if (EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }

      const fullPath = join(currentPath, entry.name);
      scan(fullPath, depth + 1);
    }
  }

  scan(startPath, 0);
  return projects.sort();
}

function findUnityProjectInParents(startPath: string): string | null {
  let currentPath = startPath;

  while (true) {
    if (isUnityProjectWithUloop(currentPath)) {
      return currentPath;
    }

    const isGitRoot = existsSync(join(currentPath, '.git'));
    if (isGitRoot) {
      return null;
    }

    const parentPath = dirname(currentPath);
    if (parentPath === currentPath) {
      return null;
    }
    currentPath = parentPath;
  }
}

/**
 * Find Unity project root by searching child directories first, then parent directories.
 * A Unity project is identified by having both Assets/ and ProjectSettings/ directories.
 *
 * Search order:
 * 1. Child directories (up to 3 levels deep)
 * 2. Parent directories (up to root)
 *
 * If multiple Unity projects are found in child search, a warning is printed
 * and the first one (alphabetically) is used.
 *
 * Returns null if no Unity project is found.
 */
let hasWarnedMultipleProjects = false;

/** @internal Reset warning state for testing */
export function resetMultipleProjectsWarning(): void {
  hasWarnedMultipleProjects = false;
}

export function findUnityProjectRoot(startPath: string = process.cwd()): string | null {
  // Why: most CLI invocations already run from the Unity project root, so checking children first
  // burns an unnecessary recursive filesystem walk on the hottest execute-dynamic-code path.
  // Why not keep child search ahead of the current directory: that only helps when the caller is
  // outside the project, while the common in-project case pays the extra scan on every cold start.
  if (isUnityProjectWithUloop(startPath)) {
    return startPath;
  }

  const childProjects = findUnityProjectsInChildren(startPath, CHILD_SEARCH_MAX_DEPTH);

  if (childProjects.length > 0) {
    if (childProjects.length > 1 && !hasWarnedMultipleProjects) {
      hasWarnedMultipleProjects = true;
      /* eslint-disable no-console -- CLI user-facing warning output */
      console.error('\x1b[33mWarning: Multiple Unity projects found in child directories:\x1b[0m');
      for (const project of childProjects) {
        console.error(`  - ${project}`);
      }
      console.error(
        '\x1b[33mRun from a Unity project root or use --project-path to specify one.\x1b[0m',
      );
      console.error('');
      /* eslint-enable no-console */
    }
    return childProjects[0];
  }

  return findUnityProjectInParents(startPath);
}

interface UnityProjectStatus {
  found: boolean;
  path: string | null;
  hasUloop: boolean;
}

/**
 * Check Unity project status with detailed information.
 * Returns whether a Unity project exists and whether Unity CLI Loop is installed.
 */
export function getUnityProjectStatus(startPath: string = process.cwd()): UnityProjectStatus {
  const unityProjectWithUloop = findUnityProjectRoot(startPath);
  if (unityProjectWithUloop) {
    return { found: true, path: unityProjectWithUloop, hasUloop: true };
  }

  const unityProjectWithoutUloop = findUnityProjectWithoutUloop(startPath);
  if (unityProjectWithoutUloop) {
    return { found: true, path: unityProjectWithoutUloop, hasUloop: false };
  }

  return { found: false, path: null, hasUloop: false };
}

function findUnityProjectWithoutUloop(startPath: string): string | null {
  const childProject = findUnityProjectInChildrenWithoutUloop(startPath, CHILD_SEARCH_MAX_DEPTH);
  if (childProject) {
    return childProject;
  }
  return findUnityProjectInParentsWithoutUloop(startPath);
}

function findUnityProjectInChildrenWithoutUloop(
  startPath: string,
  maxDepth: number,
): string | null {
  function scan(currentPath: string, depth: number): string | null {
    if (depth > maxDepth || !existsSync(currentPath)) {
      return null;
    }

    if (isUnityProject(currentPath)) {
      return currentPath;
    }

    let entries: Dirent[];
    try {
      entries = readdirSync(currentPath, { withFileTypes: true });
    } catch {
      return null;
    }

    for (const entry of entries) {
      if (!entry.isDirectory() || EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }
      const result = scan(join(currentPath, entry.name), depth + 1);
      if (result) {
        return result;
      }
    }
    return null;
  }

  return scan(startPath, 0);
}

function findUnityProjectInParentsWithoutUloop(startPath: string): string | null {
  let currentPath = startPath;

  while (true) {
    if (isUnityProject(currentPath)) {
      return currentPath;
    }

    const isGitRoot = existsSync(join(currentPath, '.git'));
    if (isGitRoot) {
      return null;
    }

    const parentPath = dirname(currentPath);
    if (parentPath === currentPath) {
      return null;
    }
    currentPath = parentPath;
  }
}
