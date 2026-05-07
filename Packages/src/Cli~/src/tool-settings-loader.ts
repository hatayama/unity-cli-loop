/**
 * Loads tool toggle settings from .uloop/settings.tools.json.
 * Used by CLI to filter out disabled tools from help and command registration.
 */

// File paths are constructed from Unity project root, not from untrusted user input
/* eslint-disable security/detect-non-literal-fs-filename */

import { existsSync, readFileSync } from 'fs';
import { join, resolve } from 'path';
import { findUnityProjectRoot } from './project-root.js';
import type { ToolDefinition } from './tool-cache.js';

const ULOOP_DIR = '.uloop';
const TOOL_SETTINGS_FILE = 'settings.tools.json';
const PACKAGES_DIR = 'Packages';
const MANIFEST_FILE = 'manifest.json';
const PACKAGES_LOCK_FILE = 'packages-lock.json';
const RUN_TESTS_TOOL_NAME = 'run-tests';
const UNITY_TEST_FRAMEWORK_DEPENDENCY_PATTERN = /"com\.unity\.test-framework"\s*:/;

interface ToolSettingsData {
  disabledTools: string[];
}

export function loadDisabledTools(projectPath?: string): string[] {
  const projectRoot: string | null = resolveProjectRoot(projectPath);
  if (projectRoot === null) {
    return [];
  }

  return loadDisabledToolsFromProjectRoot(projectRoot);
}

export function isToolEnabled(toolName: string, projectPath?: string): boolean {
  const projectRoot: string | null = resolveProjectRoot(projectPath);
  if (projectRoot === null) {
    return true;
  }

  const disabledTools: string[] = loadDisabledToolsFromProjectRoot(projectRoot);
  if (!disabledTools.includes(toolName)) {
    return true;
  }

  return shouldBypassDisabledSettingForDependencyError(toolName, projectRoot);
}

export function filterEnabledTools(
  tools: ToolDefinition[],
  projectPath?: string,
): ToolDefinition[] {
  const projectRoot: string | null = resolveProjectRoot(projectPath);
  if (projectRoot === null) {
    return tools;
  }

  const disabledTools: string[] = loadDisabledToolsFromProjectRoot(projectRoot);
  if (disabledTools.length === 0) {
    return tools;
  }

  return tools.filter((tool) => {
    if (!disabledTools.includes(tool.name)) {
      return true;
    }

    return shouldBypassDisabledSettingForDependencyError(tool.name, projectRoot);
  });
}

function resolveProjectRoot(projectPath?: string): string | null {
  return projectPath !== undefined ? resolve(projectPath) : findUnityProjectRoot();
}

function loadDisabledToolsFromProjectRoot(projectRoot: string): string[] {
  const settingsPath: string = join(projectRoot, ULOOP_DIR, TOOL_SETTINGS_FILE);

  let content: string;
  try {
    content = readFileSync(settingsPath, 'utf-8');
  } catch {
    return [];
  }

  if (!content.trim()) {
    return [];
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(content);
  } catch {
    return [];
  }

  if (typeof parsed !== 'object' || parsed === null) {
    return [];
  }

  const data = parsed as ToolSettingsData;
  if (!Array.isArray(data.disabledTools)) {
    return [];
  }

  return data.disabledTools;
}

function shouldBypassDisabledSettingForDependencyError(
  toolName: string,
  projectRoot: string,
): boolean {
  return toolName === RUN_TESTS_TOOL_NAME && !isUnityTestFrameworkInstalled(projectRoot);
}

function isUnityTestFrameworkInstalled(projectRoot: string): boolean {
  const manifestPath: string = join(projectRoot, PACKAGES_DIR, MANIFEST_FILE);
  const packagesLockPath: string = join(projectRoot, PACKAGES_DIR, PACKAGES_LOCK_FILE);

  return (
    fileContainsUnityTestFramework(manifestPath) || fileContainsUnityTestFramework(packagesLockPath)
  );
}

function fileContainsUnityTestFramework(filePath: string): boolean {
  if (!existsSync(filePath)) {
    return false;
  }

  const content: string | null = tryReadTextFile(filePath);
  if (content === null) {
    return false;
  }

  return UNITY_TEST_FRAMEWORK_DEPENDENCY_PATTERN.test(content);
}

function tryReadTextFile(filePath: string): string | null {
  try {
    return readFileSync(filePath, 'utf-8');
  } catch {
    // Files can disappear or become unreadable between existsSync and readFileSync.
    return null;
  }
}
