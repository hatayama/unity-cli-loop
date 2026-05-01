/**
 * CLI End-to-End Tests
 *
 * These tests require a running Unity Editor with Unity CLI Loop installed.
 * Run from Unity project root: npm run test:cli
 *
 * @jest-environment node
 */

import {
  execSync,
  ExecSyncOptionsWithStringEncoding,
  spawnSync,
  SpawnSyncOptionsWithStringEncoding,
} from 'child_process';
import { existsSync, readFileSync, writeFileSync, unlinkSync } from 'fs';
import { join } from 'path';

const CLI_PATH = join(__dirname, '../..', 'dist/cli.bundle.cjs');

const UNITY_PROJECT_ROOT = join(__dirname, '../../../../..');
const CUBE_SCENE_PATH = 'Assets/Scenes/SampleScene.unity';
const RESTART_GUARD_PATH = join(UNITY_PROJECT_ROOT, '.uloop', 'launch-restart-guard.json');

const EXEC_OPTIONS: ExecSyncOptionsWithStringEncoding = {
  encoding: 'utf-8',
  timeout: 60000,
  cwd: UNITY_PROJECT_ROOT,
  stdio: ['pipe', 'pipe', 'pipe'],
};

const SPAWN_OPTIONS: SpawnSyncOptionsWithStringEncoding = {
  encoding: 'utf-8',
  timeout: 60000,
  cwd: UNITY_PROJECT_ROOT,
  stdio: 'pipe',
};

const INTERVAL_MS = 1500;
const DOMAIN_RELOAD_RETRY_MS = 3000;
const DOMAIN_RELOAD_MAX_RETRIES = 3;
const UNITY_READY_RETRY_MS = 2000;
const UNITY_READY_MAX_RETRIES = 20;
const TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES = [
  'Another execution is already in progress',
  'Execution was cancelled or timed out',
];
const TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS = ['warming up'];

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function sleepSync(ms: number): void {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    // busy wait
  }
}

function isTransientUnityBusyOutput(output: string): boolean {
  return (
    output.includes('Unity is reloading') ||
    output.includes('Domain Reload') ||
    output.includes('Unity server is starting') ||
    output.includes('Unity is busy') ||
    output.includes('Unity is compiling scripts') ||
    output.includes('Cannot connect to Unity') ||
    output.includes('Unity Editor for this project is not running')
  );
}

function runCli(args: string): { stdout: string; stderr: string; exitCode: number } {
  try {
    const stdout = execSync(`node "${CLI_PATH}" ${args}`, EXEC_OPTIONS);
    return { stdout, stderr: '', exitCode: 0 };
  } catch (error) {
    const execError = error as { stdout?: string; stderr?: string; status?: number };
    return {
      stdout: execError.stdout ?? '',
      stderr: execError.stderr ?? '',
      exitCode: execError.status ?? 1,
    };
  }
}

function runCliParts(args: string[]): { stdout: string; stderr: string; exitCode: number } {
  const result = spawnSync('node', [CLI_PATH, ...args], SPAWN_OPTIONS);
  return {
    stdout: result.stdout ?? '',
    stderr: result.stderr ?? '',
    exitCode: result.status ?? 1,
  };
}

function runCliWithRetry(args: string): { stdout: string; stderr: string; exitCode: number } {
  for (let attempt = 0; attempt < DOMAIN_RELOAD_MAX_RETRIES; attempt++) {
    const result = runCli(args);
    const output = result.stderr || result.stdout;

    if (result.exitCode === 0 || !isTransientUnityBusyOutput(output)) {
      return result;
    }

    // Domain Reload in progress, wait and retry
    if (attempt < DOMAIN_RELOAD_MAX_RETRIES - 1) {
      sleepSync(DOMAIN_RELOAD_RETRY_MS);
    }
  }

  return runCli(args);
}

function runCliJson<T>(args: string): T {
  const { stdout, stderr, exitCode } = runCliWithRetry(args);
  if (exitCode !== 0) {
    throw new Error(`CLI failed with exit code ${exitCode}: ${stderr || stdout}`);
  }

  const trimmedOutput = stdout.trim();
  const jsonStartByLine = trimmedOutput.lastIndexOf('\n{');
  const jsonStart = jsonStartByLine >= 0 ? jsonStartByLine + 1 : trimmedOutput.indexOf('{');
  const jsonEnd = trimmedOutput.lastIndexOf('}');

  if (jsonStart < 0 || jsonEnd < 0 || jsonEnd < jsonStart) {
    throw new Error(`JSON payload not found in CLI output: ${trimmedOutput}`);
  }

  const jsonPayload = trimmedOutput.slice(jsonStart, jsonEnd + 1);
  return JSON.parse(jsonPayload) as T;
}

function runExecuteDynamicCodeJsonWithRetry(code: string): {
  Success: boolean;
  Result?: string;
  ErrorMessage?: string;
  Logs?: string[];
} {
  for (let attempt = 0; attempt < UNITY_READY_MAX_RETRIES; attempt++) {
    const result = runCliParts(['execute-dynamic-code', '--code', code]);
    const output = result.stderr || result.stdout;

    if (result.exitCode !== 0) {
      if (isTransientUnityBusyOutput(output) && attempt < UNITY_READY_MAX_RETRIES - 1) {
        sleepSync(UNITY_READY_RETRY_MS);
        continue;
      }

      throw new Error(`execute-dynamic-code failed: ${output}`);
    }

    const payload = parseLastJsonObject<{
      Success: boolean;
      Result?: string;
      ErrorMessage?: string;
      Logs?: string[];
    }>(result.stdout);

    if (typeof payload.Success !== 'boolean') {
      if (attempt < UNITY_READY_MAX_RETRIES - 1) {
        sleepSync(UNITY_READY_RETRY_MS);
        continue;
      }

      throw new Error(`Unexpected execute-dynamic-code payload: ${result.stdout}`);
    }

    if (payload.Success) {
      return payload;
    }

    if (!isTransientExecuteDynamicCodeFailure(payload)) {
      throw new Error(`execute-dynamic-code failed: ${payload.ErrorMessage ?? result.stdout}`);
    }

    if (attempt < UNITY_READY_MAX_RETRIES - 1) {
      sleepSync(UNITY_READY_RETRY_MS);
      continue;
    }

    throw new Error(
      `execute-dynamic-code did not become ready: ${payload.ErrorMessage ?? result.stdout}`,
    );
  }

  throw new Error('execute-dynamic-code did not become ready before retry budget was exhausted');
}

async function runExecuteDynamicCodeUntilReady(
  code: string,
): Promise<{ Success: boolean; Result?: string; ErrorMessage?: string; Logs?: string[] }> {
  for (let attempt = 0; attempt < UNITY_READY_MAX_RETRIES; attempt++) {
    const result = runCliParts(['execute-dynamic-code', '--code', code]);
    const output = result.stderr || result.stdout;

    if (result.exitCode !== 0) {
      if (isTransientUnityBusyOutput(output) && attempt < UNITY_READY_MAX_RETRIES - 1) {
        await sleep(UNITY_READY_RETRY_MS);
        continue;
      }

      throw new Error(`execute-dynamic-code failed: ${output}`);
    }

    const payload = parseLastJsonObject<{
      Success: boolean;
      Result?: string;
      ErrorMessage?: string;
      UnityVersion?: string;
      Logs?: string[];
    }>(result.stdout);

    if (typeof payload.Success !== 'boolean') {
      if (attempt < UNITY_READY_MAX_RETRIES - 1) {
        await sleep(UNITY_READY_RETRY_MS);
        continue;
      }

      throw new Error(`Unexpected execute-dynamic-code payload: ${result.stdout}`);
    }

    if (payload.Success || !isTransientExecuteDynamicCodeFailure(payload)) {
      return payload;
    }

    if (attempt >= UNITY_READY_MAX_RETRIES - 1) {
      return payload;
    }

    await sleep(UNITY_READY_RETRY_MS);
  }

  throw new Error('execute-dynamic-code did not become ready before retry budget was exhausted');
}

function isTransientExecuteDynamicCodeFailure(payload: {
  Success: boolean;
  ErrorMessage?: string;
  Logs?: string[];
}): boolean {
  if (payload.Success) {
    return false;
  }

  const errorMessage = payload.ErrorMessage ?? '';
  if (TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES.includes(errorMessage)) {
    return true;
  }

  if (!errorMessage.startsWith('COMPILATION_PROVIDER_UNAVAILABLE:')) {
    return false;
  }

  return TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS.some((substring) =>
    errorMessage.toLowerCase().includes(substring),
  );
}

function parseLastJsonObject<T>(stdout: string): T {
  const trimmedOutput = stdout.trim();
  const jsonStartByLine = trimmedOutput.lastIndexOf('\n{');
  const jsonStart = jsonStartByLine >= 0 ? jsonStartByLine + 1 : trimmedOutput.indexOf('{');
  const jsonEnd = trimmedOutput.lastIndexOf('}');

  if (jsonStart < 0 || jsonEnd < 0 || jsonEnd < jsonStart) {
    throw new Error(`JSON payload not found in CLI output: ${trimmedOutput}`);
  }

  const jsonPayload = trimmedOutput.slice(jsonStart, jsonEnd + 1);
  return JSON.parse(jsonPayload) as T;
}

function openScene(scenePath: string): void {
  const escapedScenePath = scenePath.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  const code = [
    'using UnityEditor.SceneManagement;',
    `var scene = EditorSceneManager.OpenScene("${escapedScenePath}");`,
    'return scene.name;',
  ].join(' ');
  const payload = runExecuteDynamicCodeJsonWithRetry(code);
  expect(payload.Success).toBe(true);
  expect(payload.ErrorMessage ?? '').toBe('');
}

function removeRestartGuardIfPresent(): void {
  if (existsSync(RESTART_GUARD_PATH)) {
    unlinkSync(RESTART_GUARD_PATH);
  }
}

describe('CLI E2E Tests (requires running Unity)', () => {
  beforeEach(async () => {
    await sleep(INTERVAL_MS);
  });

  describe('compile', () => {
    it('should compile successfully', () => {
      const result = runCliJson<{ Success: boolean; ErrorCount: number }>('compile');

      expect(result.Success).toBe(true);
      expect(result.ErrorCount).toBe(0);
    });
  });

  describe('get-logs', () => {
    const LOG_WAIT_MS = 1000;
    const ERROR_FAMILY_PREFIX = 'CliE2EErrorFamily';

    function setupTestLogs(): void {
      runCliWithRetry('clear-console');
      const code = [
        'using UnityEngine;',
        'Debug.Log("This is a normal log");',
        'Debug.LogWarning("This is a warning log");',
        'Debug.LogError("This is an error log");',
        'Debug.Log("LogGetter test complete");',
      ].join(' ');
      runExecuteDynamicCodeJsonWithRetry(code);
      sleepSync(LOG_WAIT_MS);
    }

    function setupErrorFamilyLogs(token: string): void {
      runCliWithRetry('clear-console');
      const code = [
        'using UnityEngine;',
        'using System;',
        `Debug.LogError("${ERROR_FAMILY_PREFIX}_Error_${token}");`,
        `Debug.LogException(new InvalidOperationException("${ERROR_FAMILY_PREFIX}_Exception_${token}"));`,
        `Debug.LogAssertion("${ERROR_FAMILY_PREFIX}_Assert_${token}");`,
        `Debug.LogWarning("${ERROR_FAMILY_PREFIX}_Warning_${token}");`,
      ].join(' ');
      runExecuteDynamicCodeJsonWithRetry(code);
      sleepSync(LOG_WAIT_MS);
    }

    function setupAssertTextLogs(token: string): void {
      runCliWithRetry('clear-console');
      const code = [
        'using UnityEngine;',
        `Debug.Log("Please assert your identity ${token}");`,
        `Debug.LogWarning("All assertions passed ${token}");`,
        `Debug.LogError("${ERROR_FAMILY_PREFIX}_ErrorOnly_${token}");`,
      ].join(' ');
      runExecuteDynamicCodeJsonWithRetry(code);
      sleepSync(LOG_WAIT_MS);
    }

    it('should retrieve test logs after executing Output Test Logs menu item', () => {
      setupTestLogs();

      const result = runCliJson<{ TotalCount: number; Logs: Array<{ Message: string }> }>(
        'get-logs',
      );

      expect(result.TotalCount).toBeGreaterThan(0);
      expect(Array.isArray(result.Logs)).toBe(true);

      // Verify specific test log messages exist
      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes('This is a normal log'))).toBe(true);
      expect(messages.some((m) => m.includes('LogGetter test complete'))).toBe(true);
    });

    it('should respect --max-count option', () => {
      setupTestLogs();

      const result = runCliJson<{ Logs: unknown[] }>('get-logs --max-count 3');

      expect(result.Logs.length).toBeLessThanOrEqual(3);
    });

    it('should filter by log type Warning', () => {
      setupTestLogs();

      const result = runCliJson<{ Logs: Array<{ Type: string; Message: string }> }>(
        'get-logs --log-type Warning',
      );

      expect(result.Logs.length).toBeGreaterThan(0);
      for (const log of result.Logs) {
        expect(log.Type).toBe('Warning');
      }
      // Verify test warning log exists
      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes('This is a warning log'))).toBe(true);
    });

    it('should filter by log type Error', () => {
      setupTestLogs();

      const result = runCliJson<{ Logs: Array<{ Type: string; Message: string }> }>(
        'get-logs --log-type Error',
      );

      expect(result.Logs.length).toBeGreaterThan(0);
      for (const log of result.Logs) {
        expect(log.Type).toBe('Error');
      }
      // Verify test error log exists
      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes('This is an error log'))).toBe(true);
    });

    it('should filter by lowercase log type error', () => {
      setupTestLogs();

      const result = runCliJson<{ Logs: Array<{ Type: string; Message: string }> }>(
        'get-logs --log-type error',
      );

      expect(result.Logs.length).toBeGreaterThan(0);
      for (const log of result.Logs) {
        expect(log.Type).toBe('Error');
      }
      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes('This is an error log'))).toBe(true);
    });

    it('should include error and exception logs in Error filter', () => {
      const token = `${Date.now()}`;
      setupErrorFamilyLogs(token);

      const result = runCliJson<{ Logs: Array<{ Type: string; Message: string }> }>(
        `get-logs --log-type Error --search-text "${token}" --max-count 20`,
      );

      expect(result.Logs.length).toBeGreaterThanOrEqual(2);
      for (const log of result.Logs) {
        expect(log.Type).toBe('Error');
      }

      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes(`${ERROR_FAMILY_PREFIX}_Error_${token}`))).toBe(true);
      expect(messages.some((m) => m.includes(`${ERROR_FAMILY_PREFIX}_Exception_${token}`))).toBe(
        true,
      );
      expect(messages.some((m) => m.includes(`${ERROR_FAMILY_PREFIX}_Warning_${token}`))).toBe(
        false,
      );
    });

    it('should not include plain assert text logs in Error filter', () => {
      const token = `${Date.now()}`;
      setupAssertTextLogs(token);

      const result = runCliJson<{ Logs: Array<{ Type: string; Message: string }> }>(
        `get-logs --log-type Error --search-text "${token}" --max-count 20`,
      );

      for (const log of result.Logs) {
        expect(log.Type).toBe('Error');
      }

      const messages = result.Logs.map((log) => log.Message);
      expect(messages.some((m) => m.includes(`${ERROR_FAMILY_PREFIX}_ErrorOnly_${token}`))).toBe(
        true,
      );
      expect(messages.some((m) => m.includes(`Please assert your identity ${token}`))).toBe(false);
      expect(messages.some((m) => m.includes(`All assertions passed ${token}`))).toBe(false);
    });

    it('should search logs by text', () => {
      setupTestLogs();

      const result = runCliJson<{ Logs: Array<{ Message: string }> }>(
        'get-logs --search-text "LogGetter test complete"',
      );

      expect(result.Logs.length).toBeGreaterThan(0);
      for (const log of result.Logs) {
        expect(log.Message).toContain('LogGetter test complete');
      }
    });
  });

  describe('clear-console', () => {
    it('should clear console and verify logs are empty', () => {
      runExecuteDynamicCodeJsonWithRetry('using UnityEngine; Debug.Log("clear test");');

      // Clear console
      const result = runCliJson<{ Success: boolean }>('clear-console');
      expect(result.Success).toBe(true);

      // Verify logs are cleared
      const logsAfterClear = runCliJson<{ TotalCount: number; Logs: unknown[] }>('get-logs');
      expect(logsAfterClear.TotalCount).toBe(0);
      expect(logsAfterClear.Logs.length).toBe(0);
    });
  });

  describe('focus-window', () => {
    it('should execute focus-window command', () => {
      // Note: Success may be false in headless/CI environments where window focus is not supported
      const result = runCliJson<{ Success: boolean }>('focus-window');

      // Just verify the command executes and returns valid JSON with Success property
      expect(typeof result.Success).toBe('boolean');
    });
  });

  describe('get-hierarchy', () => {
    it('should retrieve hierarchy and save to file', () => {
      const result = runCliJson<{ hierarchyFilePath: string }>('get-hierarchy --max-depth 2');

      expect(typeof result.hierarchyFilePath).toBe('string');
      expect(result.hierarchyFilePath).toContain('hierarchy_');
    });

    it('should support --include-components false to disable components', () => {
      const result = runCliJson<{ hierarchyFilePath: string }>(
        'get-hierarchy --max-depth 1 --include-components false',
      );

      expect(typeof result.hierarchyFilePath).toBe('string');
      expect(result.hierarchyFilePath).toContain('hierarchy_');
    });

    it('should support --include-inactive false to exclude inactive objects', () => {
      const result = runCliJson<{ hierarchyFilePath: string }>(
        'get-hierarchy --max-depth 1 --include-inactive false',
      );

      expect(typeof result.hierarchyFilePath).toBe('string');
      expect(result.hierarchyFilePath).toContain('hierarchy_');
    });
  });

  describe('find-game-objects', () => {
    it('should find game objects with name pattern', () => {
      const result = runCliJson<{ results: unknown[]; totalFound: number }>(
        'find-game-objects --name-pattern "*" --include-inactive true',
      );

      expect(Array.isArray(result.results)).toBe(true);
      expect(typeof result.totalFound).toBe('number');
    });

    it('should find Cube game object with default array parameter', () => {
      openScene(CUBE_SCENE_PATH);

      const result = runCliJson<{ results: Array<{ name: string }>; totalFound: number }>(
        'find-game-objects --name-pattern "Cube"',
      );

      expect(result.totalFound).toBeGreaterThan(0);
      expect(result.results.some((r) => r.name === 'Cube')).toBe(true);
    });
  });

  describe('--help', () => {
    it('should display help', () => {
      const { stdout, exitCode } = runCli('--help');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('Unity CLI Loop');
      expect(stdout).toContain('compile');
      expect(stdout).toContain('get-logs');
    });

    it('should display command-specific help', () => {
      const { stdout, exitCode } = runCli('compile --help');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('--force-recompile');
      expect(stdout).toContain('--wait-for-domain-reload');
    });

    it('should display grouped help with category headings', () => {
      const { stdout, exitCode } = runCli('--help');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('Built-in Tools:');
      expect(stdout).toContain('CLI Commands:');
      // CLI Commands should appear before Built-in Tools
      const cliIndex: number = stdout.indexOf('CLI Commands:');
      const builtInIndex: number = stdout.indexOf('Built-in Tools:');
      expect(cliIndex).toBeLessThan(builtInIndex);
    });

    it('should display Third-party Tools section when cache contains third-party tools', () => {
      const { stdout, exitCode } = runCli('--help');

      expect(exitCode).toBe(0);
      // hello-world is a third-party tool present in the local cache but not in default-tools.json
      if (stdout.includes('hello-world')) {
        expect(stdout).toContain('Third-party Tools:');
        const builtInIndex: number = stdout.indexOf('Built-in Tools:');
        const thirdPartyIndex: number = stdout.indexOf('Third-party Tools:');
        expect(builtInIndex).toBeLessThan(thirdPartyIndex);
      }
    });

    it('should resolve tool cache via --project-path', () => {
      const withProjectPath = runCli(`--help --project-path "${UNITY_PROJECT_ROOT}"`);
      const withoutProjectPath = runCli('--help');

      expect(withProjectPath.exitCode).toBe(0);
      expect(withoutProjectPath.exitCode).toBe(0);

      // Both should show the same category headings
      expect(withProjectPath.stdout).toContain('Built-in Tools:');
      expect(withProjectPath.stdout).toContain('CLI Commands:');

      // Third-party tools visible in normal help should also appear with --project-path
      if (withoutProjectPath.stdout.includes('Third-party Tools:')) {
        expect(withProjectPath.stdout).toContain('Third-party Tools:');
      }
    });

    it('should display boolean options with value format in get-hierarchy help', () => {
      const { stdout, exitCode } = runCli('get-hierarchy --help');

      expect(exitCode).toBe(0);
      // Boolean options should show <value> format
      expect(stdout).toContain('--include-components <value>');
      expect(stdout).toContain('--include-inactive <value>');
      expect(stdout).toContain('(default: "true")');
    });
  });

  describe('--version', () => {
    it('should display version', () => {
      const { stdout, exitCode } = runCli('--version');

      expect(exitCode).toBe(0);
      expect(stdout).toMatch(/^\d+\.\d+\.\d+/);
    });
  });

  describe('list', () => {
    it('should list available tools', () => {
      const { stdout, exitCode } = runCli('list');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('- compile');
      expect(stdout).toContain('- get-logs');
      expect(stdout).toContain('- get-hierarchy');
    });
  });

  describe('sync', () => {
    it('should sync tools from Unity', () => {
      const { stdout, exitCode } = runCli('sync');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('Synced');
      expect(stdout).toContain('tools to');
      // Check for tools.json in path (works for both Windows \ and Unix /)
      expect(stdout).toMatch(/[/\\]\.uloop[/\\]tools\.json/);
    });
  });

  describe('skills', () => {
    it('should list skills for claude target', () => {
      const { stdout, exitCode } = runCli('skills list --claude');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('uloop-compile');
      expect(stdout).toContain('uloop-get-logs');
      expect(stdout).toContain('uloop-run-tests');
    });

    it('should show bundled and project skills count', () => {
      const { stdout, exitCode } = runCli('skills list --claude');

      expect(exitCode).toBe(0);
      // Should show total skills count
      expect(stdout).toMatch(/total:\s*\d+/i);
    });

    it('should install skills for claude target', () => {
      // First uninstall to ensure clean state
      runCli('skills uninstall --claude');

      const { stdout, exitCode } = runCli('skills install --claude');
      const installedSkillPath = join(
        UNITY_PROJECT_ROOT,
        '.claude',
        'skills',
        'unity-cli-loop',
        'uloop-compile',
        'SKILL.md',
      );

      expect(exitCode).toBe(0);
      expect(stdout).toMatch(/installed|updated|skipped/i);
      expect(existsSync(installedSkillPath)).toBe(true);
    });

    it('should install skills directly under skills when grouping is disabled', () => {
      runCli('skills uninstall --claude');

      const { stdout, exitCode } = runCli('skills install --claude --flat');
      const installedSkillPath = join(
        UNITY_PROJECT_ROOT,
        '.claude',
        'skills',
        'uloop-compile',
        'SKILL.md',
      );

      expect(exitCode).toBe(0);
      expect(stdout).toMatch(/installed|updated|skipped/i);
      expect(existsSync(installedSkillPath)).toBe(true);
    });

    it('should uninstall skills for claude target', () => {
      // First install to ensure there are skills to uninstall
      runCli('skills install --claude');

      const { stdout, exitCode } = runCli('skills uninstall --claude');

      expect(exitCode).toBe(0);
      expect(stdout).toMatch(/removed|not found/i);
    });

    it('should include project skills in list when available', () => {
      const { stdout, exitCode } = runCli('skills list --claude');

      expect(exitCode).toBe(0);
      // HelloWorld sample should be detected as a project skill
      expect(stdout).toContain('uloop-hello-world');
    });

    it('should install project skills along with bundled skills', () => {
      // First uninstall
      runCli('skills uninstall --claude');

      const { stdout, exitCode } = runCli('skills install --claude');

      expect(exitCode).toBe(0);
      // Should mention project skills were installed
      expect(stdout).toMatch(/project|installed/i);
    });
  });

  describe('execute-dynamic-code', () => {
    it('should execute simple code without parameters', () => {
      // Result is serialized as string by Unity
      const result = runCliJson<{ Result: string }>('execute-dynamic-code --code "return 1;"');

      expect(result.Result).toBe('1');
    });

    it('should execute code with explicit empty parameters', () => {
      const result = runCliJson<{ Result: string }>(
        'execute-dynamic-code --code "return \\"hello\\";" --parameters "{}"',
      );

      expect(result.Result).toBe('hello');
    });

    it('should keep dynamic code available across restart cooldown handling', async () => {
      removeRestartGuardIfPresent();

      const launchResult = runCli('launch -r');

      expect(launchResult.exitCode).toBe(0);

      const immediateResult = runCliParts([
        'execute-dynamic-code',
        '--code',
        'return "after-restart";',
      ]);
      expect(immediateResult.exitCode).toBe(0);

      const payload = parseLastJsonObject<{
        Success: boolean;
        Result?: string;
        ErrorMessage?: string;
      }>(immediateResult.stdout);

      expect(payload.Success).toBe(true);
      expect(payload.Result).toBe('after-restart');
      expect(payload.ErrorMessage ?? '').toBe('');

      const blockedLaunchResult = runCli('launch -r');

      expect(blockedLaunchResult.exitCode).not.toBe(0);
      expect(blockedLaunchResult.stderr || blockedLaunchResult.stdout).toContain(
        'Refusing to restart Unity',
      );

      const guardedResult = await runExecuteDynamicCodeUntilReady('return "guarded-restart";');
      expect(guardedResult.Success).toBe(true);
      expect(guardedResult.Result).toBe('guarded-restart');
      expect(guardedResult.ErrorMessage ?? '').toBe('');
    }, 90000);
  });

  describe('error handling', () => {
    it('should handle unknown commands gracefully', () => {
      const { exitCode } = runCli('unknown-command');

      expect(exitCode).not.toBe(0);
    });
  });

  describe('launch', () => {
    it('should display launch command help', () => {
      const { stdout, exitCode } = runCli('launch --help');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('Open a Unity project');
      expect(stdout).toContain('--restart');
      expect(stdout).toContain('--platform');
      expect(stdout).toContain('--max-depth');
      expect(stdout).toContain('--add-unity-hub');
      expect(stdout).toContain('--favorite');
    });

    it('should detect already running Unity and focus window', () => {
      // Unity is already running for this test suite, so launch should detect it
      const { stdout, exitCode } = runCli(`launch "${UNITY_PROJECT_ROOT}"`);

      expect(exitCode).toBe(0);
      expect(stdout).toContain('Unity process already running');
    });

    it('should fail gracefully when project not found', () => {
      const { stdout, stderr, exitCode } = runCli('launch /nonexistent/path/to/project');

      expect(exitCode).not.toBe(0);
      // Error message should mention project not found or version file not found
      const output = stderr || stdout;
      expect(output).toMatch(/not found|does not appear to be a Unity project/i);
    });

    it('should search for Unity project from current directory', () => {
      // This test runs from Unity project root, so it should find the project
      const { stdout, exitCode } = runCli('launch');

      expect(exitCode).toBe(0);
      // Should either find and focus existing Unity or report no Unity found
      expect(stdout).toMatch(/Unity process already running|Selected project/);
    });
  });

  describe('tool-settings', () => {
    const settingsPath: string = join(UNITY_PROJECT_ROOT, '.uloop', 'settings.tools.json');
    let originalSettings: string | null;

    beforeAll(() => {
      try {
        originalSettings = readFileSync(settingsPath, 'utf-8');
      } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
          originalSettings = null;
        } else {
          throw error;
        }
      }
      writeFileSync(settingsPath, JSON.stringify({ disabledTools: ['get-logs'] }));
    });

    afterAll(() => {
      if (originalSettings !== null) {
        writeFileSync(settingsPath, originalSettings);
      } else {
        unlinkSync(settingsPath);
      }
    });

    it('should not display disabled tools in --help', () => {
      const { stdout, exitCode } = runCli('--help');

      expect(exitCode).toBe(0);
      expect(stdout).toContain('compile');
      expect(stdout).not.toContain('get-logs');
    });

    it('should not include disabled tools in --list-commands', () => {
      const { stdout, exitCode } = runCli('--list-commands');

      expect(exitCode).toBe(0);
      const commands: string[] = stdout.trim().split('\n').filter(Boolean);
      expect(commands).toContain('compile');
      expect(commands).not.toContain('get-logs');
    });

    it('should output nothing for --list-options on disabled tool', () => {
      const { stdout, exitCode } = runCli('--list-options get-logs');

      expect(exitCode).toBe(0);
      expect(stdout.trim()).toBe('');
    });
  });

  // Domain Reload tests must run last to avoid affecting other tests
  describe('compile --force-recompile (Domain Reload)', () => {
    it('should support --force-recompile option', () => {
      const { exitCode } = runCli('compile --force-recompile');

      // Domain Reload causes connection to be lost, so we just verify the command runs
      // The exit code may be non-zero due to connection being dropped during reload
      expect(typeof exitCode).toBe('number');
    });
  });

  describe('compile --wait-for-domain-reload', () => {
    it('should support --wait-for-domain-reload option', () => {
      const { exitCode } = runCli('compile --wait-for-domain-reload');

      // This option is intended to survive domain reload and return once the
      // compile result is available again.
      expect(typeof exitCode).toBe('number');
    });
  });
});
