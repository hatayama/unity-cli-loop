/**
 * Tool cache management for CLI.
 * Handles loading/saving tool definitions from .uloop/tools.json cache.
 */

// File paths are constructed from Unity project root, not from untrusted user input
/* eslint-disable security/detect-non-literal-fs-filename */

import { existsSync, readFileSync, writeFileSync, mkdirSync } from 'fs';
import { join } from 'path';
import defaultToolsData from './default-tools.json';
import { findUnityProjectRoot } from './project-root.js';

export interface ToolProperty {
  type: string;
  description?: string;
  default?: unknown;
  enum?: string[];
  items?: { type: string };
}

interface ToolInputSchema {
  type: string;
  properties: Record<string, ToolProperty>;
  required?: string[];
}

export interface ToolDefinition {
  name: string;
  description: string;
  inputSchema: ToolInputSchema;
}

export interface ToolsCache {
  version: string;
  serverVersion?: string;
  updatedAt?: string;
  tools: ToolDefinition[];
}

const CACHE_DIR = '.uloop';
const CACHE_FILE = 'tools.json';

function getCacheDir(projectPath?: string): string {
  if (projectPath !== undefined) {
    return join(projectPath, CACHE_DIR);
  }
  const projectRoot = findUnityProjectRoot();
  if (projectRoot === null) {
    return join(process.cwd(), CACHE_DIR);
  }
  return join(projectRoot, CACHE_DIR);
}

function getCachePath(projectPath?: string): string {
  return join(getCacheDir(projectPath), CACHE_FILE);
}

/**
 * Load default tools bundled with npm package.
 */
export function getDefaultTools(): ToolsCache {
  return defaultToolsData as ToolsCache;
}

/**
 * Load tools from cache file, falling back to default tools if cache doesn't exist.
 * When projectPath is specified, reads cache from that project directory.
 */
export function loadToolsCache(projectPath?: string): ToolsCache {
  const cachePath = getCachePath(projectPath);

  if (existsSync(cachePath)) {
    try {
      const content = readFileSync(cachePath, 'utf-8');
      return JSON.parse(content) as ToolsCache;
    } catch {
      return getDefaultTools();
    }
  }

  return getDefaultTools();
}

/**
 * Save tools to cache file.
 */
export function saveToolsCache(cache: ToolsCache): void {
  const cacheDir = getCacheDir();
  const cachePath = getCachePath();

  if (!existsSync(cacheDir)) {
    mkdirSync(cacheDir, { recursive: true });
  }

  const content = JSON.stringify(cache, null, 2);
  writeFileSync(cachePath, content, 'utf-8');
}

/**
 * Check if cache file exists.
 */
export function hasCacheFile(): boolean {
  return existsSync(getCachePath());
}

/**
 * Get the cache file path for display purposes.
 */
export function getCacheFilePath(): string {
  return getCachePath();
}

/**
 * Get the set of default tool names bundled with npm package.
 * Used to distinguish built-in tools from third-party tools.
 */
export function getDefaultToolNames(): ReadonlySet<string> {
  const defaultTools: ToolsCache = getDefaultTools();
  return new Set(defaultTools.tools.map((tool: ToolDefinition) => tool.name));
}

/**
 * Get the Unity server version from cache file.
 * Returns undefined if cache doesn't exist, is corrupted, or serverVersion is missing.
 */
export function getCachedServerVersion(): string | undefined {
  const cachePath = getCachePath();

  if (!existsSync(cachePath)) {
    return undefined;
  }

  try {
    const content = readFileSync(cachePath, 'utf-8');
    const cache = JSON.parse(content) as Partial<ToolsCache>;
    return typeof cache.serverVersion === 'string' ? cache.serverVersion : undefined;
  } catch {
    return undefined;
  }
}
