/**
 * Unit tests for tool-settings-loader.ts
 *
 * Tests pure functions for loading and filtering disabled tools.
 * Uses temporary directories to avoid affecting real project settings.
 */

import { mkdirSync, rmSync, writeFileSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';

// Mock findUnityProjectRoot before importing the module
let mockProjectRoot: string | null = null;

jest.mock('../project-root.js', () => ({
  findUnityProjectRoot: (): string | null => mockProjectRoot,
}));

// Import after mocking
import { loadDisabledTools, isToolEnabled, filterEnabledTools } from '../tool-settings-loader.js';
import type { ToolDefinition } from '../tool-cache.js';

describe('tool-settings-loader', () => {
  let testDir: string;

  beforeEach(() => {
    testDir = join(tmpdir(), `uloop-test-${Date.now()}-${Math.random().toString(36).slice(2)}`);
    mkdirSync(join(testDir, '.uloop'), { recursive: true });
    mockProjectRoot = testDir;
  });

  afterEach(() => {
    rmSync(testDir, { recursive: true, force: true });
    mockProjectRoot = null;
  });

  // ── loadDisabledTools ──────────────────────────────────────────

  describe('loadDisabledTools', () => {
    it('should return disabled tools from valid settings file', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile', 'get-logs'] }),
      );

      const result: string[] = loadDisabledTools();

      expect(result).toEqual(['compile', 'get-logs']);
    });

    it('should return empty array when file does not exist', () => {
      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });

    it('should return empty array when project root is null', () => {
      mockProjectRoot = null;

      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });

    it('should return empty array for invalid JSON', () => {
      writeFileSync(join(testDir, '.uloop', 'settings.tools.json'), '{invalid json}');

      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });

    it('should return empty array for empty file', () => {
      writeFileSync(join(testDir, '.uloop', 'settings.tools.json'), '');

      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });

    it('should return empty array when disabledTools is not an array', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: 'not-an-array' }),
      );

      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });

    it('should return empty array when disabledTools key is missing', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ other: 'data' }),
      );

      const result: string[] = loadDisabledTools();

      expect(result).toEqual([]);
    });
  });

  // ── isToolEnabled ──────────────────────────────────────────────

  describe('isToolEnabled', () => {
    it('should return false for a disabled tool', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile'] }),
      );

      expect(isToolEnabled('compile')).toBe(false);
    });

    it('should return true for an enabled tool', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile'] }),
      );

      expect(isToolEnabled('get-logs')).toBe(true);
    });

    it('should return true when no settings file exists', () => {
      expect(isToolEnabled('compile')).toBe(true);
    });

    it('should keep run-tests callable when test framework package is missing', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      expect(isToolEnabled('run-tests')).toBe(true);
    });

    it('should disable run-tests when test framework package is installed', () => {
      writePackageManifest(testDir, { 'com.unity.test-framework': '1.3.9' });
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      expect(isToolEnabled('run-tests')).toBe(false);
    });

    it('should disable run-tests when test framework package is resolved', () => {
      writePackagesLock(testDir, { 'com.unity.test-framework': { version: '1.3.9' } });
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      expect(isToolEnabled('run-tests')).toBe(false);
    });

    it('should use packages-lock when manifest cannot be read', () => {
      mkdirSync(join(testDir, 'Packages', 'manifest.json'), { recursive: true });
      writePackagesLock(testDir, { 'com.unity.test-framework': { version: '1.3.9' } });
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      expect(isToolEnabled('run-tests')).toBe(false);
    });
  });

  // ── filterEnabledTools ─────────────────────────────────────────

  describe('filterEnabledTools', () => {
    const mockTools: ToolDefinition[] = [
      {
        name: 'compile',
        description: 'Compile',
        inputSchema: { type: 'object', properties: {} },
      },
      {
        name: 'get-logs',
        description: 'Get logs',
        inputSchema: { type: 'object', properties: {} },
      },
      {
        name: 'clear-console',
        description: 'Clear',
        inputSchema: { type: 'object', properties: {} },
      },
      {
        name: 'run-tests',
        description: 'Run tests',
        inputSchema: { type: 'object', properties: {} },
      },
    ];

    it('should filter out disabled tools', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile', 'clear-console'] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result).toHaveLength(2);
      expect(result[0].name).toBe('get-logs');
      expect(result[1].name).toBe('run-tests');
    });

    it('should return all tools when none are disabled', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: [] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result).toHaveLength(4);
    });

    it('should return all tools when settings file does not exist', () => {
      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result).toHaveLength(4);
    });

    it('should keep run-tests when test framework package is missing', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result.map((tool) => tool.name)).toContain('run-tests');
    });

    it('should filter run-tests when test framework package is installed', () => {
      writePackageManifest(testDir, { 'com.unity.test-framework': '1.3.9' });
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result.map((tool) => tool.name)).not.toContain('run-tests');
    });

    it('should filter run-tests when test framework package is resolved', () => {
      writePackagesLock(testDir, { 'com.unity.test-framework': { version: '1.3.9' } });
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['run-tests'] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools);

      expect(result.map((tool) => tool.name)).not.toContain('run-tests');
    });
  });

  // ── projectPath override ────────────────────────────────────────

  describe('projectPath override', () => {
    let otherDir: string;

    beforeEach(() => {
      otherDir = join(tmpdir(), `uloop-other-${Date.now()}-${Math.random().toString(36).slice(2)}`);
      mkdirSync(join(otherDir, '.uloop'), { recursive: true });
    });

    afterEach(() => {
      rmSync(otherDir, { recursive: true, force: true });
    });

    it('should load settings from projectPath instead of cwd project root', () => {
      writeFileSync(
        join(otherDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['get-logs'] }),
      );

      const result: string[] = loadDisabledTools(otherDir);

      expect(result).toEqual(['get-logs']);
    });

    it('should ignore cwd project root when projectPath is provided', () => {
      writeFileSync(
        join(testDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile'] }),
      );
      writeFileSync(
        join(otherDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['get-logs'] }),
      );

      expect(loadDisabledTools(otherDir)).toEqual(['get-logs']);
      expect(loadDisabledTools()).toEqual(['compile']);
    });

    it('should pass projectPath through isToolEnabled', () => {
      writeFileSync(
        join(otherDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile'] }),
      );

      expect(isToolEnabled('compile', otherDir)).toBe(false);
      expect(isToolEnabled('get-logs', otherDir)).toBe(true);
    });

    it('should pass projectPath through filterEnabledTools', () => {
      const mockTools: ToolDefinition[] = [
        {
          name: 'compile',
          description: 'Compile',
          inputSchema: { type: 'object', properties: {} },
        },
        {
          name: 'get-logs',
          description: 'Get logs',
          inputSchema: { type: 'object', properties: {} },
        },
      ];
      writeFileSync(
        join(otherDir, '.uloop', 'settings.tools.json'),
        JSON.stringify({ disabledTools: ['compile'] }),
      );

      const result: ToolDefinition[] = filterEnabledTools(mockTools, otherDir);

      expect(result).toHaveLength(1);
      expect(result[0].name).toBe('get-logs');
    });

    it('should return empty array when projectPath has no settings file', () => {
      const result: string[] = loadDisabledTools(otherDir);

      expect(result).toEqual([]);
    });
  });
});

function writePackageManifest(projectRoot: string, dependencies: Record<string, string>): void {
  mkdirSync(join(projectRoot, 'Packages'), { recursive: true });
  writeFileSync(join(projectRoot, 'Packages', 'manifest.json'), JSON.stringify({ dependencies }));
}

function writePackagesLock(projectRoot: string, dependencies: Record<string, unknown>): void {
  mkdirSync(join(projectRoot, 'Packages'), { recursive: true });
  writeFileSync(
    join(projectRoot, 'Packages', 'packages-lock.json'),
    JSON.stringify({ dependencies }),
  );
}
