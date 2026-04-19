/**
 * Claude Code and other AI tools require skills to be in specific directories.
 * This module bridges the gap between bundled/project skills and target tool
 * configurations, handling path resolution and file synchronization.
 */

// File paths are constructed from home directory and skill names, not from untrusted user input
/* eslint-disable security/detect-non-literal-fs-filename */

import assert from 'node:assert';
import { PRODUCT_DISPLAY_NAME } from '../cli-constants';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  writeFileSync,
  rmSync,
  readdirSync,
  renameSync,
} from 'fs';
import { join, dirname, resolve, isAbsolute, sep } from 'path';
import { homedir } from 'os';
import { TargetConfig } from './target-config.js';
import { findUnityProjectRoot, getUnityProjectStatus } from '../project-root.js';
import { DEPRECATED_SKILLS } from './deprecated-skills.js';
import { loadDisabledTools } from '../tool-settings-loader.js';

type SkillStatus = 'installed' | 'not_installed' | 'outdated';

interface SkillInfo {
  name: string;
  status: SkillStatus;
  path?: string;
  source?: 'bundled' | 'project';
}

interface SkillDefinition {
  name: string;
  toolName?: string;
  dirName: string;
  content: string;
  sourcePath: string;
  additionalFiles?: Record<string, Buffer>;
  sourceType: 'package' | 'cli-only' | 'project';
}

const EXCLUDED_DIRS = new Set([
  'node_modules',
  '.git',
  'Temp',
  'obj',
  'Build',
  'Builds',
  'Logs',
  'Skill',
]);
const EXCLUDED_FILES = new Set(['.meta', '.DS_Store', '.gitkeep']);
class SkillsPathConstants {
  public static readonly PACKAGES_DIR = 'Packages';
  public static readonly SRC_DIR = 'src';
  public static readonly SKILLS_DIR = 'skills';
  public static readonly EDITOR_DIR = 'Editor';
  public static readonly API_DIR = 'Api';
  public static readonly MCP_TOOLS_DIR = 'McpTools';
  public static readonly SKILL_DIR = 'Skill';
  public static readonly LIBRARY_DIR = 'Library';
  public static readonly PACKAGE_CACHE_DIR = 'PackageCache';
  public static readonly ASSETS_DIR = 'Assets';
  public static readonly MANIFEST_FILE = 'manifest.json';
  public static readonly SKILL_FILE = 'SKILL.md';
  public static readonly MANAGED_SKILLS_DIR = 'unity-cli-loop';
  public static readonly CLI_ONLY_DIR = 'skill-definitions';
  public static readonly CLI_ONLY_SUBDIR = 'cli-only';
  public static readonly DIST_PARENT_DIR = '..';
  public static readonly FILE_PROTOCOL = 'file:';
  public static readonly PATH_PROTOCOL = 'path:';
  public static readonly PACKAGE_NAME = 'io.github.hatayama.uloopmcp';
  public static readonly PACKAGE_NAME_ALIAS = 'io.github.hatayama.uLoopMCP';
  public static readonly PACKAGE_NAMES = [
    SkillsPathConstants.PACKAGE_NAME,
    SkillsPathConstants.PACKAGE_NAME_ALIAS,
  ];
}

function getGlobalSkillsRoot(target: TargetConfig): string {
  return join(homedir(), target.projectDir, 'skills');
}

function getProjectSkillsRoot(target: TargetConfig): string {
  const status = getUnityProjectStatus();
  if (!status.found) {
    throw new Error(
      'Not inside a Unity project. Run this command from within a Unity project directory.',
    );
  }
  if (!status.hasUloop) {
    throw new Error(
      `${PRODUCT_DISPLAY_NAME} is not installed in this Unity project (${status.path}).\n` +
        `Please install ${PRODUCT_DISPLAY_NAME} package first, then run this command again.`,
    );
  }
  return join(status.path as string, target.projectDir, 'skills');
}

function getSkillsBaseDir(target: TargetConfig, global: boolean): string {
  return global ? getGlobalSkillsRoot(target) : getProjectSkillsRoot(target);
}

/** @internal Resolve the managed install namespace under a target skills root. */
export function getManagedSkillsDir(baseDir: string): string {
  return join(baseDir, SkillsPathConstants.MANAGED_SKILLS_DIR);
}

function isSafeSkillPathComponent(skillDirName: string): boolean {
  if (skillDirName.length === 0) {
    return false;
  }

  if (skillDirName === '.' || skillDirName === '..') {
    return false;
  }

  if (isAbsolute(skillDirName)) {
    return false;
  }

  return !skillDirName.includes('/') && !skillDirName.includes('\\') && !skillDirName.includes(sep);
}

function assertSafeSkillPathComponent(skillDirName: string): void {
  assert(
    isSafeSkillPathComponent(skillDirName),
    'skillDirName must be a single safe path component',
  );
}

function getLegacySkillDir(baseDir: string, skillDirName: string): string {
  assertSafeSkillPathComponent(skillDirName);
  return join(baseDir, skillDirName);
}

function getManagedSkillDir(baseDir: string, skillDirName: string): string {
  assertSafeSkillPathComponent(skillDirName);
  return join(getManagedSkillsDir(baseDir), skillDirName);
}

function getLegacySkillPath(skillDirName: string, target: TargetConfig, global: boolean): string {
  return join(getSkillsBaseDir(target, global), skillDirName, target.skillFileName);
}

function getManagedSkillPath(skillDirName: string, target: TargetConfig, global: boolean): string {
  return join(
    getManagedSkillDir(getSkillsBaseDir(target, global), skillDirName),
    target.skillFileName,
  );
}

function getPreferredSkillPath(
  skillDirName: string,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): string {
  return groupManagedSkills
    ? getManagedSkillPath(skillDirName, target, global)
    : getLegacySkillPath(skillDirName, target, global);
}

function getFallbackSkillPath(
  skillDirName: string,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): string {
  return groupManagedSkills
    ? getLegacySkillPath(skillDirName, target, global)
    : getManagedSkillPath(skillDirName, target, global);
}

function getInstalledSkillPath(
  skillDirName: string,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean = true,
  includeFallback: boolean = false,
): string | null {
  const candidatePaths = [getPreferredSkillPath(skillDirName, target, global, groupManagedSkills)];
  if (includeFallback) {
    candidatePaths.push(getFallbackSkillPath(skillDirName, target, global, groupManagedSkills));
  }

  for (const candidatePath of candidatePaths) {
    if (existsSync(candidatePath)) {
      return candidatePath;
    }
  }

  return null;
}

function isSkillInstalled(
  skill: SkillDefinition,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): boolean {
  return getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills) !== null;
}

function isSkillOutdated(
  skill: SkillDefinition,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): boolean {
  const skillPath = getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills);
  if (skillPath === null) {
    return false;
  }

  const skillDir = dirname(skillPath);
  const installedContent = readFileSync(skillPath, 'utf-8');
  if (installedContent !== skill.content) {
    return true;
  }

  if ('additionalFiles' in skill && skill.additionalFiles) {
    const additionalFiles: Record<string, Buffer> = skill.additionalFiles;
    for (const [relativePath, expectedContent] of Object.entries(additionalFiles)) {
      const filePath = join(skillDir, relativePath);
      if (!existsSync(filePath)) {
        return true;
      }
      const installedFileContent = readFileSync(filePath);
      if (!installedFileContent.equals(expectedContent)) {
        return true;
      }
    }
  }

  const installedFiles = collectSkillFolderFiles(skillDir);
  const expectedFileCount =
    1 + ('additionalFiles' in skill && skill.additionalFiles ? Object.keys(skill.additionalFiles).length : 0);
  const installedFileCount = 1 + (installedFiles ? Object.keys(installedFiles).length : 0);
  if (installedFileCount !== expectedFileCount) {
    return true;
  }

  return false;
}

function getSkillStatus(
  skill: SkillDefinition,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): SkillStatus {
  if (!isSkillInstalled(skill, target, global, groupManagedSkills)) {
    return 'not_installed';
  }
  if (isSkillOutdated(skill, target, global, groupManagedSkills)) {
    return 'outdated';
  }
  return 'installed';
}

/** @internal Move managed skills from the legacy flat layout into the namespaced install root. */
export function migrateLegacyManagedSkills(
  baseDir: string,
  managedSkillDirNames: readonly string[],
): number {
  const managedRoot = getManagedSkillsDir(baseDir);
  let moved = 0;

  for (const skillDirName of new Set(managedSkillDirNames)) {
    const legacySkillDir = getLegacySkillDir(baseDir, skillDirName);
    if (!existsSync(legacySkillDir)) {
      continue;
    }

    const managedSkillDir = getManagedSkillDir(baseDir, skillDirName);
    if (existsSync(managedSkillDir)) {
      continue;
    }

    mkdirSync(managedRoot, { recursive: true });
    renameSync(legacySkillDir, managedSkillDir);
    moved++;
  }

  return moved;
}

/** @internal Remove stale deprecated skills from both legacy and namespaced install roots. */
export function removeDeprecatedSkillDirs(baseDir: string): number {
  let removed = 0;

  for (const deprecatedName of DEPRECATED_SKILLS) {
    const candidateDirs = [
      getLegacySkillDir(baseDir, deprecatedName),
      getManagedSkillDir(baseDir, deprecatedName),
    ];

    for (const candidateDir of candidateDirs) {
      if (!existsSync(candidateDir)) {
        continue;
      }

      rmSync(candidateDir, { recursive: true, force: true });
      removed++;
    }
  }

  return removed;
}

export function parseFrontmatter(content: string): Record<string, string | boolean> {
  const frontmatterMatch = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!frontmatterMatch) {
    return {};
  }

  const frontmatterMap = new Map<string, string | boolean>();
  const lines = frontmatterMatch[1].split(/\r?\n/);

  for (const line of lines) {
    const colonIndex = line.indexOf(':');
    if (colonIndex === -1) {
      continue;
    }

    const key = line.slice(0, colonIndex).trim();
    const rawValue = line.slice(colonIndex + 1).trim();

    let parsedValue: string | boolean = rawValue;
    if (rawValue === 'true') {
      parsedValue = true;
    } else if (rawValue === 'false') {
      parsedValue = false;
    }

    frontmatterMap.set(key, parsedValue);
  }

  return Object.fromEntries(frontmatterMap);
}

const warnedLegacyPaths = new Set<string>();

function warnLegacySkillStructure(toolPath: string, legacySkillMdPath: string): void {
  if (warnedLegacyPaths.has(legacySkillMdPath)) {
    return;
  }
  warnedLegacyPaths.add(legacySkillMdPath);

  /* eslint-disable no-console -- CLI user-facing warning output */
  console.error('\x1b[33m' + '='.repeat(70) + '\x1b[0m');
  console.error('\x1b[33mWarning: Legacy skill structure detected\x1b[0m');
  console.error(`  Path: ${legacySkillMdPath}`);
  console.error('');
  console.error('  The skill structure has changed. Please migrate to the new format:');
  console.error('    1. Create a "Skill" folder in the tool directory');
  console.error('    2. Move SKILL.md and any additional files/folders into Skill/');
  console.error('');
  console.error('  Expected structure:');
  console.error('    ToolName/');
  console.error('      └── Skill/');
  console.error('            ├── SKILL.md');
  console.error('            └── (any additional files or directories)');
  console.error('\x1b[33m' + '='.repeat(70) + '\x1b[0m');
  console.error('');
  /* eslint-enable no-console */
}

function scanEditorFolderForSkills(
  editorPath: string,
  skills: SkillDefinition[],
  sourceType: SkillDefinition['sourceType'],
  warnLegacy: boolean = true,
): void {
  if (!existsSync(editorPath)) {
    return;
  }

  const entries = readdirSync(editorPath, { withFileTypes: true });

  for (const entry of entries) {
    if (EXCLUDED_DIRS.has(entry.name)) {
      continue;
    }

    const fullPath = join(editorPath, entry.name);

    if (entry.isDirectory()) {
      const skillDir = join(fullPath, SkillsPathConstants.SKILL_DIR);
      const skillMdPath = join(skillDir, SkillsPathConstants.SKILL_FILE);

      const legacySkillMdPath = join(fullPath, SkillsPathConstants.SKILL_FILE);
      if (warnLegacy && !existsSync(skillMdPath) && existsSync(legacySkillMdPath)) {
        warnLegacySkillStructure(fullPath, legacySkillMdPath);
      }

      if (existsSync(skillMdPath)) {
        const content = readFileSync(skillMdPath, 'utf-8');
        const frontmatter = parseFrontmatter(content);

        if (frontmatter.internal === true) {
          continue;
        }

        const name = typeof frontmatter.name === 'string' ? frontmatter.name : entry.name;
        if (!isSafeSkillPathComponent(name)) {
          continue;
        }

        const toolName =
          typeof frontmatter.toolName === 'string' ? frontmatter.toolName : undefined;
        const additionalFiles = collectSkillFolderFiles(skillDir);

        skills.push({
          name,
          toolName,
          dirName: name,
          content,
          sourcePath: skillMdPath,
          additionalFiles,
          sourceType,
        });
      }

      scanEditorFolderForSkills(fullPath, skills, sourceType, warnLegacy);
    }
  }
}

function findEditorFolders(basePath: string, maxDepth: number = 2): string[] {
  const editorFolders: string[] = [];

  function scan(currentPath: string, depth: number): void {
    if (depth > maxDepth || !existsSync(currentPath)) {
      return;
    }

    const entries = readdirSync(currentPath, { withFileTypes: true });

    for (const entry of entries) {
      if (!entry.isDirectory() || EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }

      const fullPath = join(currentPath, entry.name);

      if (entry.name === 'Editor') {
        editorFolders.push(fullPath);
      } else {
        scan(fullPath, depth + 1);
      }
    }
  }

  scan(basePath, 0);
  return editorFolders;
}

function collectProjectSkills(excludedRoots: string[] = []): SkillDefinition[] {
  const projectRoot = findUnityProjectRoot();
  if (!projectRoot) {
    return [];
  }
  const skills: SkillDefinition[] = [];
  const seenNames = new Set<string>();

  const searchPaths = getProjectSkillSearchRoots(projectRoot);

  for (const searchPath of searchPaths) {
    if (!existsSync(searchPath)) {
      continue;
    }

    const editorFolders = findEditorFolders(searchPath, 3);

    for (const editorFolder of editorFolders) {
      scanEditorFolderForSkills(editorFolder, skills, 'project');
    }
  }

  const uniqueSkills: SkillDefinition[] = [];
  for (const skill of skills) {
    if (isUnderExcludedRoots(skill.sourcePath, excludedRoots)) {
      continue;
    }
    if (!seenNames.has(skill.name)) {
      seenNames.add(skill.name);
      uniqueSkills.push(skill);
    }
  }

  return uniqueSkills;
}

export function getProjectSkillSearchRoots(projectRoot: string): string[] {
  const searchRoots: string[] = [];
  const seenRoots = new Set<string>();

  const addSearchRoot = (root: string | null): void => {
    if (!root) {
      return;
    }

    const normalizedRoot = resolve(root);
    if (seenRoots.has(normalizedRoot)) {
      return;
    }

    seenRoots.add(normalizedRoot);
    searchRoots.push(normalizedRoot);
  };

  addSearchRoot(join(projectRoot, SkillsPathConstants.ASSETS_DIR));
  addSearchRoot(resolvePackageRoot(projectRoot));

  for (const packageRoot of enumerateDirectProjectPackageRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }

  for (const packageRoot of resolveManifestLocalPackageRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }

  for (const packageRoot of resolveDependencyPackageCacheRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }

  return searchRoots;
}

function enumerateDirectProjectPackageRoots(projectRoot: string): string[] {
  const packagesRoot = join(projectRoot, SkillsPathConstants.PACKAGES_DIR);
  if (!existsSync(packagesRoot)) {
    return [];
  }

  const packageRoots: string[] = [];
  const entries = readdirSync(packagesRoot, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }

    packageRoots.push(resolveSkillSearchRootCandidate(join(packagesRoot, entry.name)));
  }

  return packageRoots;
}

function resolveManifestLocalPackageRoots(projectRoot: string): string[] {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }

  const packageRoots: string[] = [];
  for (const dependencyValue of Object.values(dependencies)) {
    const localPath = resolveLocalDependencyPath(dependencyValue, projectRoot);
    if (!localPath) {
      continue;
    }

    packageRoots.push(resolveSkillSearchRootCandidate(localPath));
  }

  return packageRoots;
}

function resolveDependencyPackageCacheRoots(projectRoot: string): string[] {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }

  const dependencyNames = new Set(Object.keys(dependencies).map((name) => name.toLowerCase()));
  if (dependencyNames.size === 0) {
    return [];
  }

  const packageCacheDir = join(
    projectRoot,
    SkillsPathConstants.LIBRARY_DIR,
    SkillsPathConstants.PACKAGE_CACHE_DIR,
  );
  if (!existsSync(packageCacheDir)) {
    return [];
  }

  const packageRoots: string[] = [];
  const entries = readdirSync(packageCacheDir, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }

    const separatorIndex = entry.name.indexOf('@');
    const dependencyName = separatorIndex === -1 ? entry.name : entry.name.slice(0, separatorIndex);
    if (!dependencyNames.has(dependencyName.toLowerCase())) {
      continue;
    }

    packageRoots.push(resolveSkillSearchRootCandidate(join(packageCacheDir, entry.name)));
  }

  return packageRoots;
}

function resolveSkillSearchRootCandidate(candidate: string): string {
  const nestedRoot = join(candidate, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR);
  if (existsSync(nestedRoot)) {
    return nestedRoot;
  }

  return candidate;
}

export function getAllSkillStatuses(
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean = true,
): SkillInfo[] {
  const allSkills = collectAllSkills();
  return allSkills.map((skill) => ({
    name: skill.name,
    status: getSkillStatus(skill, target, global, groupManagedSkills),
    path:
      getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills, true) ?? undefined,
    source: skill.sourceType === 'project' ? 'project' : 'bundled',
  }));
}

export function getPreferredSkillDir(
  baseDir: string,
  skillDirName: string,
  groupManagedSkills: boolean,
): string {
  assertSafeSkillPathComponent(skillDirName);
  return groupManagedSkills
    ? getManagedSkillDir(baseDir, skillDirName)
    : getLegacySkillDir(baseDir, skillDirName);
}

function installSkill(
  skill: SkillDefinition,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): void {
  const baseDir = getSkillsBaseDir(target, global);
  const skillDir = getPreferredSkillDir(baseDir, skill.dirName, groupManagedSkills);
  syncInstalledSkillDirectory(skillDir, target.skillFileName, skill.content, skill.additionalFiles);

  const alternateSkillDir = getPreferredSkillDir(baseDir, skill.dirName, !groupManagedSkills);
  if (alternateSkillDir !== skillDir && existsSync(alternateSkillDir)) {
    rmSync(alternateSkillDir, { recursive: true, force: true });
  }
}

export function syncInstalledSkillDirectory(
  skillDir: string,
  skillFileName: string,
  skillContent: string,
  additionalFiles?: Record<string, Buffer>,
): void {
  mkdirSync(dirname(skillDir), { recursive: true });

  const tempSkillDir = mkdtempSync(`${skillDir}.tmp-`);
  const skillPath = join(tempSkillDir, skillFileName);
  let replaced = false;

  try {
    writeFileSync(skillPath, skillContent, 'utf-8');

    if (additionalFiles) {
      for (const [relativePath, content] of Object.entries(additionalFiles)) {
        const fullPath = join(tempSkillDir, relativePath);
        mkdirSync(dirname(fullPath), { recursive: true });
        writeFileSync(fullPath, content);
      }
    }

    rmSync(skillDir, { recursive: true, force: true });
    renameSync(tempSkillDir, skillDir);
    replaced = true;
  } finally {
    if (!replaced) {
      rmSync(tempSkillDir, { recursive: true, force: true });
    }
  }
}

function uninstallSkill(
  skill: SkillDefinition,
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean,
): boolean {
  const baseDir = getSkillsBaseDir(target, global);
  const candidateDirs = [getPreferredSkillDir(baseDir, skill.dirName, groupManagedSkills)];
  let removed = false;

  for (const candidateDir of candidateDirs) {
    if (!existsSync(candidateDir)) {
      continue;
    }

    rmSync(candidateDir, { recursive: true, force: true });
    removed = true;
  }

  return removed;
}

function uninstallSkillFromAllLayouts(skill: SkillDefinition, target: TargetConfig, global: boolean): boolean {
  const baseDir = getSkillsBaseDir(target, global);
  const candidateDirs = [
    getManagedSkillDir(baseDir, skill.dirName),
    getLegacySkillDir(baseDir, skill.dirName),
  ];
  let removed = false;

  for (const candidateDir of candidateDirs) {
    if (!existsSync(candidateDir)) {
      continue;
    }

    rmSync(candidateDir, { recursive: true, force: true });
    removed = true;
  }

  return removed;
}

interface InstallResult {
  installed: number;
  updated: number;
  skipped: number;
  bundledCount: number;
  projectCount: number;
  deprecatedRemoved: number;
}

export function installAllSkills(
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean = true,
): InstallResult {
  const result: InstallResult = {
    installed: 0,
    updated: 0,
    skipped: 0,
    bundledCount: 0,
    projectCount: 0,
    deprecatedRemoved: 0,
  };

  const allSkills = collectAllSkills();
  const baseDir = getSkillsBaseDir(target, global);
  result.deprecatedRemoved = removeDeprecatedSkillDirs(baseDir);
  if (groupManagedSkills) {
    migrateLegacyManagedSkills(
      baseDir,
      allSkills.map((skill) => skill.dirName),
    );
  }

  // Global installs ignore project-local tool settings
  const disabledTools: string[] = global ? [] : loadDisabledTools();
  const projectSkills = allSkills.filter((skill) => skill.sourceType === 'project');
  const nonProjectSkills = allSkills.filter((skill) => skill.sourceType !== 'project');

  for (const skill of allSkills) {
    if (isSkillDisabledByToolSettings(skill, disabledTools)) {
      uninstallSkillFromAllLayouts(skill, target, global);
      continue;
    }

    const status = getSkillStatus(skill, target, global, groupManagedSkills);

    if (status === 'not_installed') {
      installSkill(skill, target, global, groupManagedSkills);
      result.installed++;
    } else if (status === 'outdated') {
      installSkill(skill, target, global, groupManagedSkills);
      result.updated++;
    } else {
      result.skipped++;
    }
  }
  result.bundledCount = nonProjectSkills.length;
  result.projectCount = projectSkills.length;

  return result;
}

function isSkillDisabledByToolSettings(skill: SkillDefinition, disabledTools: string[]): boolean {
  if (disabledTools.length === 0) {
    return false;
  }

  const toolName: string | null =
    skill.toolName ?? (skill.name.startsWith('uloop-') ? skill.name.slice('uloop-'.length) : null);
  if (toolName === null) {
    return false;
  }
  return disabledTools.includes(toolName);
}

interface UninstallResult {
  removed: number;
  notFound: number;
}

export function uninstallAllSkills(
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean = true,
): UninstallResult {
  const result: UninstallResult = { removed: 0, notFound: 0 };

  const baseDir = getSkillsBaseDir(target, global);
  result.removed += removeDeprecatedSkillDirs(baseDir);

  const allSkills = collectAllSkills();
  for (const skill of allSkills) {
    if (uninstallSkill(skill, target, global, groupManagedSkills)) {
      result.removed++;
    } else {
      result.notFound++;
    }
  }

  return result;
}

export function getInstallDir(
  target: TargetConfig,
  global: boolean,
  groupManagedSkills: boolean = true,
): string {
  const baseDir = getSkillsBaseDir(target, global);
  return groupManagedSkills ? getManagedSkillsDir(baseDir) : baseDir;
}

export function getTotalSkillCount(): number {
  return collectAllSkills().length;
}

function collectAllSkills(): SkillDefinition[] {
  const projectRoot = findUnityProjectRoot();
  const packageRoot = projectRoot ? resolvePackageRoot(projectRoot) : null;
  const packageSkills = packageRoot ? collectPackageSkillsFromRoot(packageRoot) : [];
  const cliOnlySkills = collectCliOnlySkills();
  const projectSkills = collectProjectSkills(packageRoot ? [packageRoot] : []);

  return dedupeSkillsByName([packageSkills, cliOnlySkills, projectSkills]);
}

function collectPackageSkillsFromRoot(packageRoot: string): SkillDefinition[] {
  const mcpToolsRoot = join(
    packageRoot,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR,
  );
  if (!existsSync(mcpToolsRoot)) {
    return [];
  }
  const skills: SkillDefinition[] = [];
  scanEditorFolderForSkills(mcpToolsRoot, skills, 'package');
  return skills;
}

function collectCliOnlySkills(): SkillDefinition[] {
  const cliOnlyRoot = resolve(
    __dirname,
    SkillsPathConstants.DIST_PARENT_DIR,
    SkillsPathConstants.SRC_DIR,
    SkillsPathConstants.SKILLS_DIR,
    SkillsPathConstants.CLI_ONLY_DIR,
    SkillsPathConstants.CLI_ONLY_SUBDIR,
  );
  if (!existsSync(cliOnlyRoot)) {
    return [];
  }
  const skills: SkillDefinition[] = [];
  scanEditorFolderForSkills(cliOnlyRoot, skills, 'cli-only', false);
  return skills;
}

function isExcludedFile(fileName: string): boolean {
  if (EXCLUDED_FILES.has(fileName)) {
    return true;
  }
  for (const pattern of EXCLUDED_FILES) {
    if (fileName.endsWith(pattern)) {
      return true;
    }
  }
  return false;
}

function collectSkillFolderFilesRecursive(
  baseDir: string,
  currentDir: string,
  additionalFiles: Record<string, Buffer>,
): void {
  const entries = readdirSync(currentDir, { withFileTypes: true });
  for (const entry of entries) {
    if (isExcludedFile(entry.name)) {
      continue;
    }
    const fullPath = join(currentDir, entry.name);
    const relativePath = fullPath.slice(baseDir.length + 1);

    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }
      collectSkillFolderFilesRecursive(baseDir, fullPath, additionalFiles);
    } else if (entry.isFile()) {
      if (entry.name === SkillsPathConstants.SKILL_FILE) {
        continue;
      }
      // eslint-disable-next-line security/detect-object-injection -- Paths are controlled by package files, not user input.
      additionalFiles[relativePath] = readFileSync(fullPath);
    }
  }
}

function collectSkillFolderFiles(skillDir: string): Record<string, Buffer> | undefined {
  if (!existsSync(skillDir)) {
    return undefined;
  }
  const additionalFiles: Record<string, Buffer> = {};
  collectSkillFolderFilesRecursive(skillDir, skillDir, additionalFiles);
  return Object.keys(additionalFiles).length > 0 ? additionalFiles : undefined;
}

function dedupeSkillsByName(skillGroups: SkillDefinition[][]): SkillDefinition[] {
  const seenNames = new Set<string>();
  const merged: SkillDefinition[] = [];
  for (const group of skillGroups) {
    for (const skill of group) {
      if (seenNames.has(skill.name)) {
        continue;
      }
      seenNames.add(skill.name);
      merged.push(skill);
    }
  }
  return merged;
}

function resolvePackageRoot(projectRoot: string): string | null {
  const candidates: string[] = [];
  candidates.push(join(projectRoot, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR));

  const manifestPaths = resolveManifestPackagePaths(projectRoot);
  for (const manifestPath of manifestPaths) {
    candidates.push(manifestPath);
  }

  for (const packageName of SkillsPathConstants.PACKAGE_NAMES) {
    candidates.push(join(projectRoot, SkillsPathConstants.PACKAGES_DIR, packageName));
  }

  const directRoot = resolveFirstPackageRoot(candidates);
  if (directRoot) {
    return directRoot;
  }

  return resolvePackageCacheRoot(projectRoot);
}

function resolveManifestPackagePaths(projectRoot: string): string[] {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }

  const resolvedPaths: string[] = [];
  for (const [dependencyName, dependencyValue] of Object.entries(dependencies)) {
    if (!isTargetPackageName(dependencyName)) {
      continue;
    }
    const localPath = resolveLocalDependencyPath(dependencyValue, projectRoot);
    if (localPath) {
      resolvedPaths.push(localPath);
    }
  }
  return resolvedPaths;
}

function readManifestDependencies(projectRoot: string): Record<string, string> | null {
  const manifestPath = join(
    projectRoot,
    SkillsPathConstants.PACKAGES_DIR,
    SkillsPathConstants.MANIFEST_FILE,
  );
  if (!existsSync(manifestPath)) {
    return null;
  }
  const manifestContent = readFileSync(manifestPath, 'utf-8');
  let manifestJson: { dependencies?: Record<string, string> };
  try {
    manifestJson = JSON.parse(manifestContent) as { dependencies?: Record<string, string> };
  } catch (error) {
    // Manifest is user-editable; fail-soft to keep skill installation usable.
    // eslint-disable-next-line no-console -- Warning is required; silent failure would hide manifest issues.
    console.warn('Failed to parse manifest.json; skipping manifest-based path resolution.', error);
    return null;
  }
  const dependencies = manifestJson.dependencies;
  if (!dependencies) {
    return null;
  }
  return dependencies;
}

function resolveLocalDependencyPath(dependencyValue: string, projectRoot: string): string | null {
  if (dependencyValue.startsWith(SkillsPathConstants.FILE_PROTOCOL)) {
    const rawPath = dependencyValue.slice(SkillsPathConstants.FILE_PROTOCOL.length);
    return resolveDependencyPath(rawPath, projectRoot);
  }
  if (dependencyValue.startsWith(SkillsPathConstants.PATH_PROTOCOL)) {
    const rawPath = dependencyValue.slice(SkillsPathConstants.PATH_PROTOCOL.length);
    return resolveDependencyPath(rawPath, projectRoot);
  }
  return null;
}

function resolveDependencyPath(rawPath: string, projectRoot: string): string | null {
  const trimmed = rawPath.trim();
  if (!trimmed) {
    return null;
  }
  let normalizedPath = trimmed;
  if (normalizedPath.startsWith('//')) {
    normalizedPath = normalizedPath.slice(2);
  }
  if (isAbsolute(normalizedPath)) {
    return normalizedPath;
  }
  return resolve(projectRoot, normalizedPath);
}

function resolveFirstPackageRoot(candidates: string[]): string | null {
  for (const candidate of candidates) {
    const resolvedRoot = resolvePackageRootCandidate(candidate);
    if (resolvedRoot) {
      return resolvedRoot;
    }
  }
  return null;
}

function resolvePackageCacheRoot(projectRoot: string): string | null {
  const packageCacheDir = join(
    projectRoot,
    SkillsPathConstants.LIBRARY_DIR,
    SkillsPathConstants.PACKAGE_CACHE_DIR,
  );
  if (!existsSync(packageCacheDir)) {
    return null;
  }
  const entries = readdirSync(packageCacheDir, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    if (!isTargetPackageCacheDir(entry.name)) {
      continue;
    }
    const candidate = join(packageCacheDir, entry.name);
    const resolvedRoot = resolvePackageRootCandidate(candidate);
    if (resolvedRoot) {
      return resolvedRoot;
    }
  }
  return null;
}

function resolvePackageRootCandidate(candidate: string): string | null {
  if (!existsSync(candidate)) {
    return null;
  }
  const directToolsPath = join(
    candidate,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR,
  );
  if (existsSync(directToolsPath)) {
    return candidate;
  }

  const nestedRoot = join(candidate, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR);
  const nestedToolsPath = join(
    nestedRoot,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR,
  );
  if (existsSync(nestedToolsPath)) {
    return nestedRoot;
  }
  return null;
}

function isTargetPackageName(name: string): boolean {
  const normalized = name.toLowerCase();
  return SkillsPathConstants.PACKAGE_NAMES.some(
    (packageName) => packageName.toLowerCase() === normalized,
  );
}

function isTargetPackageCacheDir(dirName: string): boolean {
  const normalized = dirName.toLowerCase();
  return SkillsPathConstants.PACKAGE_NAMES.some((packageName) =>
    normalized.startsWith(`${packageName.toLowerCase()}@`),
  );
}

function isUnderExcludedRoots(targetPath: string, excludedRoots: string[]): boolean {
  for (const root of excludedRoots) {
    if (isPathUnder(targetPath, root)) {
      return true;
    }
  }
  return false;
}

function isPathUnder(childPath: string, parentPath: string): boolean {
  const resolvedChild = resolve(childPath);
  const resolvedParent = resolve(parentPath);
  if (resolvedChild === resolvedParent) {
    return true;
  }
  return resolvedChild.startsWith(resolvedParent + sep);
}
