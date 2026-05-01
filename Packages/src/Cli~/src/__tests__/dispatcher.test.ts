jest.mock(
  'launch-unity',
  () => ({
    orchestrateLaunch: jest.fn(),
    findRunningUnityProcess: jest.fn(),
    focusUnityProcess: jest.fn(),
  }),
  { virtual: true },
);

import { EventEmitter } from 'events';
import { chmodSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'fs';
import { tmpdir } from 'os';
import { join } from 'path';
import { VERSION } from '../version';
import {
  PROJECT_LOCAL_CLI_IN_PROCESS_MARKER,
  loadProjectLocalCliInProcess,
  runDispatcher,
  type DispatcherChildProcess,
  type DispatcherDependencies,
} from '../dispatcher';

const LEGACY_IN_PROCESS_MARKER = 'uloop-cli-in-process-entrypoint-v1';

type SpawnCall = {
  readonly command: string;
  readonly args: readonly string[];
  readonly cwd: string;
  readonly shell: boolean | string | undefined;
};

type LoadModuleCall = {
  readonly modulePath: string;
  readonly args: readonly string[];
  readonly cwd: string;
};

type DispatcherCommandCall = {
  readonly projectPath: string | undefined;
  readonly options?: Record<string, unknown>;
  readonly cwd: string;
};

function createUnityProject(): string {
  const projectRoot = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-'));
  createUnityProjectAt(projectRoot);
  return projectRoot;
}

function createUnityProjectAt(projectRoot: string): void {
  mkdirSync(projectRoot, { recursive: true });
  mkdirSync(join(projectRoot, 'Assets'));
  mkdirSync(join(projectRoot, 'ProjectSettings'));
}

function installProjectLocalCli(
  projectRoot: string,
  contents = `#!/usr/bin/env node\n${PROJECT_LOCAL_CLI_IN_PROCESS_MARKER}\n`,
): string {
  const binDir = join(projectRoot, '.uloop', 'bin');
  mkdirSync(binDir, { recursive: true });
  const cliPath = join(binDir, 'uloop.cjs');
  writeFileSync(cliPath, contents);
  return cliPath;
}

function installWindowsProjectLocalCli(projectRoot: string): string {
  installProjectLocalCli(projectRoot);
  const cliPath = join(projectRoot, '.uloop', 'bin', 'uloop.cmd');
  writeFileSync(cliPath, '@echo off\r\nnode "%~dp0\\uloop.cjs" %*\r\n');
  return cliPath;
}

function installBundledCli(root: string): string {
  const distDir = join(root, 'dist');
  mkdirSync(distDir, { recursive: true });
  const bundledCliPath = join(distDir, 'cli.bundle.cjs');
  writeFileSync(bundledCliPath, `#!/usr/bin/env node\n${PROJECT_LOCAL_CLI_IN_PROCESS_MARKER}\n`);
  return bundledCliPath;
}

function createDependencies(
  projectRoot: string,
  args: readonly string[],
  spawnExitCode = 0,
  overrides: Partial<
    Pick<DispatcherDependencies, 'platform' | 'bundledCliPath' | 'nodePath' | 'isToolEnabledFn'>
  > = {},
): DispatcherDependencies & {
  readonly spawnCalls: SpawnCall[];
  readonly loadModuleCalls: LoadModuleCall[];
  readonly chdirCalls: string[];
  readonly launchCalls: DispatcherCommandCall[];
  readonly focusWindowCalls: DispatcherCommandCall[];
  readonly stdoutChunks: string[];
  readonly stderrChunks: string[];
} {
  const spawnCalls: SpawnCall[] = [];
  const loadModuleCalls: LoadModuleCall[] = [];
  const chdirCalls: string[] = [];
  const launchCalls: DispatcherCommandCall[] = [];
  const focusWindowCalls: DispatcherCommandCall[] = [];
  const stdoutChunks: string[] = [];
  const stderrChunks: string[] = [];
  let currentCwd = projectRoot;

  return {
    args,
    cwd: projectRoot,
    platform: overrides.platform ?? 'darwin',
    stdout: { write: (chunk: string): boolean => stdoutChunks.push(chunk) > 0 },
    stderr: { write: (chunk: string): boolean => stderrChunks.push(chunk) > 0 },
    bundledCliPath: overrides.bundledCliPath ?? join(projectRoot, 'dist', 'cli.bundle.cjs'),
    nodePath: overrides.nodePath ?? '/usr/local/bin/node',
    spawnFn: (command, forwardedArgs, options): DispatcherChildProcess => {
      spawnCalls.push({
        command,
        args: forwardedArgs,
        cwd: String(options.cwd),
        shell: options.shell,
      });

      const child = new EventEmitter();
      process.nextTick(() => child.emit('exit', spawnExitCode, null));
      return child as DispatcherChildProcess;
    },
    chdirFn: (path): void => {
      currentCwd = path;
      chdirCalls.push(path);
    },
    loadModuleFn: (modulePath, forwardedArgs): Promise<void> => {
      loadModuleCalls.push({
        modulePath,
        args: forwardedArgs,
        cwd: currentCwd,
      });
      return Promise.resolve();
    },
    launchCommandFn: (projectPath, options, context): Promise<number> => {
      launchCalls.push({
        projectPath,
        options: { ...options },
        cwd: context.cwd,
      });
      return Promise.resolve(0);
    },
    focusWindowCommandFn: (projectPath, context): Promise<number> => {
      focusWindowCalls.push({
        projectPath,
        cwd: context.cwd,
      });
      return Promise.resolve(0);
    },
    isToolEnabledFn: overrides.isToolEnabledFn ?? ((): boolean => true),
    spawnCalls,
    loadModuleCalls,
    chdirCalls,
    launchCalls,
    focusWindowCalls,
    stdoutChunks,
    stderrChunks,
  };
}

describe('dispatcher', () => {
  const createdProjects: string[] = [];

  afterEach(() => {
    for (const projectRoot of createdProjects) {
      rmSync(projectRoot, { recursive: true, force: true });
    }
    createdProjects.length = 0;
  });

  it.each([
    ['--help'],
    ['compile', '--help'],
    ['completion', '--help'],
    ['skills', 'list', '--claude'],
    ['update', '--help'],
    ['--list-commands'],
    ['--list-options', 'compile'],
  ])('runs %s through the bundled CLI without resolving a Unity project', async (...args) => {
    const root = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-no-project-'));
    createdProjects.push(root);
    const bundledCliPath = installBundledCli(root);
    const dependencies = createDependencies(root, args, 0, { bundledCliPath });

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.spawnCalls).toEqual([
      {
        command: '/usr/local/bin/node',
        args: [bundledCliPath, ...args],
        cwd: root,
        shell: undefined,
      },
    ]);
    expect(dependencies.chdirCalls).toHaveLength(0);
    expect(dependencies.stderrChunks).toEqual([]);
  });

  it('reports a missing bundled CLI before project resolution for project-independent commands', async () => {
    const root = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-missing-bundled-'));
    createdProjects.push(root);
    const bundledCliPath = join(root, 'dist', 'cli.bundle.cjs');
    const dependencies = createDependencies(root, ['--help'], 0, { bundledCliPath });

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(1);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.stderrChunks.join('')).toContain('Bundled uloop CLI was not found');
    expect(dependencies.stderrChunks.join('')).not.toContain('Could not find a Unity project');
  });

  it('does not let explicit port tool commands bypass Unity project resolution', async () => {
    const root = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-explicit-port-'));
    createdProjects.push(root);
    const bundledCliPath = installBundledCli(root);
    const dependencies = createDependencies(root, ['get-logs', '--port', '56000'], 0, {
      bundledCliPath,
    });

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(1);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.spawnCalls).toEqual([]);
    expect(dependencies.stderrChunks.join('')).toContain('Could not find a Unity project');
  });

  it('skips unreadable child directories while discovering Unity projects', async () => {
    const root = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-unreadable-dir-'));
    createdProjects.push(root);
    const unreadableDir = join(root, 'unreadable');
    const projectRoot = join(root, 'NestedUnityProject');
    mkdirSync(unreadableDir);
    createUnityProjectAt(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies(root, ['compile']);

    chmodSync(unreadableDir, 0o000);
    try {
      const exitCode = await runDispatcher(dependencies);

      expect(exitCode).toBe(0);
      expect(dependencies.loadModuleCalls).toEqual([
        {
          modulePath: cliPath,
          args: ['compile'],
          cwd: projectRoot,
        },
      ]);
      expect(dependencies.stderrChunks).toEqual([]);
    } finally {
      chmodSync(unreadableDir, 0o700);
    }
  });

  it('prefers discovered child Unity projects that already have a project-local CLI', async () => {
    const root = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-child-cli-'));
    createdProjects.push(root);
    const projectWithoutCli = join(root, 'AProjectWithoutCli');
    const projectWithCli = join(root, 'BProjectWithCli');
    createUnityProjectAt(projectWithoutCli);
    createUnityProjectAt(projectWithCli);
    const cliPath = installProjectLocalCli(projectWithCli);
    const dependencies = createDependencies(root, ['compile']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['compile'],
        cwd: projectWithCli,
      },
    ]);
    expect(dependencies.stderrChunks).toEqual([]);
  });

  it('forwards arguments to the project-local CLI selected by --project-path', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies('/tmp', [
      '--project-path',
      projectRoot,
      'get-logs',
      '--json',
    ]);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.chdirCalls).toEqual([projectRoot]);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['get-logs', '--json', '--project-path', projectRoot],
        cwd: projectRoot,
      },
    ]);
  });

  it('falls back to spawning the project-local CLI when marker probing cannot read it', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies(projectRoot, ['get-logs']);

    chmodSync(cliPath, 0o000);
    try {
      const exitCode = await runDispatcher(dependencies);

      expect(exitCode).toBe(0);
      expect(dependencies.loadModuleCalls).toHaveLength(0);
      expect(dependencies.chdirCalls).toHaveLength(0);
      expect(dependencies.spawnCalls).toEqual([
        {
          command: '/usr/local/bin/node',
          args: [cliPath, 'get-logs'],
          cwd: projectRoot,
          shell: undefined,
        },
      ]);
    } finally {
      chmodSync(cliPath, 0o700);
    }
  });

  it('normalizes non-leading project-path arguments before project-local dispatch', async () => {
    const parentRoot = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-relative-project-'));
    createdProjects.push(parentRoot);
    const projectRoot = join(parentRoot, 'UnityProject');
    mkdirSync(join(projectRoot, 'Assets'), { recursive: true });
    mkdirSync(join(projectRoot, 'ProjectSettings'), { recursive: true });
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies(parentRoot, [
      'get-logs',
      '--project-path',
      'UnityProject',
      '--json',
    ]);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.chdirCalls).toEqual([projectRoot]);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['get-logs', '--json', '--project-path', projectRoot],
        cwd: projectRoot,
      },
    ]);
  });

  it('discovers the Unity project from a nested working directory', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot);
    const nestedDir = join(projectRoot, 'Assets', 'Scripts');
    mkdirSync(nestedDir, { recursive: true });
    const dependencies = createDependencies(nestedDir, ['list']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.chdirCalls).toEqual([projectRoot]);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['list'],
        cwd: projectRoot,
      },
    ]);
  });

  it('discovers the Unity project from a parent working directory', async () => {
    const parentRoot = mkdtempSync(join(tmpdir(), 'uloop-dispatcher-parent-'));
    createdProjects.push(parentRoot);
    const projectRoot = join(parentRoot, 'UnityProject');
    mkdirSync(join(projectRoot, 'Assets'), { recursive: true });
    mkdirSync(join(projectRoot, 'ProjectSettings'), { recursive: true });
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies(parentRoot, ['compile']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.chdirCalls).toEqual([projectRoot]);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['compile'],
        cwd: projectRoot,
      },
    ]);
  });

  it('falls back to spawning the project-local CLI on Windows', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installWindowsProjectLocalCli(projectRoot);
    const dependencies = createDependencies(projectRoot, ['list'], 0, { platform: 'win32' });

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.spawnCalls).toEqual([
      {
        command: cliPath,
        args: ['list'],
        cwd: projectRoot,
        shell: true,
      },
    ]);
  });

  it('falls back to spawning an older project-local CLI without the in-process entrypoint', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot, '#!/usr/bin/env node\n');
    const dependencies = createDependencies(projectRoot, ['compile']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.spawnCalls).toEqual([
      {
        command: '/usr/local/bin/node',
        args: [cliPath, 'compile'],
        cwd: projectRoot,
        shell: undefined,
      },
    ]);
  });

  it('falls back to spawning a v1 in-process CLI that does not await dynamic commands', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(
      projectRoot,
      `#!/usr/bin/env node\n${LEGACY_IN_PROCESS_MARKER}\n`,
    );
    const dependencies = createDependencies(projectRoot, ['get-logs']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.loadModuleCalls).toHaveLength(0);
    expect(dependencies.spawnCalls).toEqual([
      {
        command: '/usr/local/bin/node',
        args: [cliPath, 'get-logs'],
        cwd: projectRoot,
        shell: undefined,
      },
    ]);
  });

  it('loads a project-local CommonJS CLI inside ESM Unity projects', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    writeFileSync(join(projectRoot, 'package.json'), JSON.stringify({ type: 'module' }));
    const resultPath = join(projectRoot, 'loader-result.json');
    const previousResultPath = process.env['ULOOP_TEST_LOADER_RESULT_PATH'];
    const cliPath = installProjectLocalCli(
      projectRoot,
      [
        '#!/usr/bin/env node',
        `const marker = '${PROJECT_LOCAL_CLI_IN_PROCESS_MARKER}';`,
        'exports.runCli = async (args) => {',
        "  require('node:fs').writeFileSync(process.env.ULOOP_TEST_LOADER_RESULT_PATH, JSON.stringify(args));",
        '};',
        '',
      ].join('\n'),
    );
    process.env['ULOOP_TEST_LOADER_RESULT_PATH'] = resultPath;

    try {
      await loadProjectLocalCliInProcess(cliPath, ['get-logs']);
    } finally {
      if (previousResultPath === undefined) {
        delete process.env['ULOOP_TEST_LOADER_RESULT_PATH'];
      } else {
        process.env['ULOOP_TEST_LOADER_RESULT_PATH'] = previousResultPath;
      }
    }

    expect(JSON.parse(readFileSync(resultPath, 'utf-8'))).toEqual(['get-logs']);
  });

  it('loads a v2 project-local CLI in-process', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const cliPath = installProjectLocalCli(projectRoot);
    const dependencies = createDependencies(projectRoot, ['get-logs']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.loadModuleCalls).toEqual([
      {
        modulePath: cliPath,
        args: ['get-logs'],
        cwd: projectRoot,
      },
    ]);
  });

  it('returns an actionable error when the project-local CLI is missing', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const dependencies = createDependencies(projectRoot, ['compile']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(1);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.stderrChunks.join('')).toContain('.uloop/bin/uloop.cjs');
  });

  it('prints the dispatcher version without requiring a Unity project', async () => {
    const dependencies = createDependencies('/tmp', ['--version']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.stdoutChunks.join('')).toBe(`${VERSION}\n`);
  });

  it('runs launch in the dispatcher without requiring a project-local CLI', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const dependencies = createDependencies('/tmp', ['launch', projectRoot, '--restart']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.launchCalls).toEqual([
      {
        projectPath: projectRoot,
        options: { restart: true },
        cwd: '/tmp',
      },
    ]);
  });

  it('allows launch to use negative max-depth values', async () => {
    const dependencies = createDependencies('/tmp', ['launch', '--max-depth', '-1']);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.stderrChunks).toEqual([]);
    expect(dependencies.launchCalls).toEqual([
      {
        projectPath: undefined,
        options: { maxDepth: '-1' },
        cwd: '/tmp',
      },
    ]);
  });

  it('runs focus-window in the dispatcher without requiring a project-local CLI', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    const dependencies = createDependencies('/tmp', [
      'focus-window',
      '--project-path',
      projectRoot,
    ]);

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(0);
    expect(dependencies.spawnCalls).toHaveLength(0);
    expect(dependencies.focusWindowCalls).toEqual([
      {
        projectPath: projectRoot,
        cwd: '/tmp',
      },
    ]);
  });

  it('honors disabled focus-window tool settings in the dispatcher', async () => {
    const projectRoot = createUnityProject();
    createdProjects.push(projectRoot);
    mkdirSync(join(projectRoot, '.uloop'), { recursive: true });
    writeFileSync(
      join(projectRoot, '.uloop', 'settings.tools.json'),
      JSON.stringify({ disabledTools: ['focus-window'] }),
    );
    const dependencies = createDependencies(
      '/tmp',
      ['focus-window', '--project-path', projectRoot],
      0,
      {
        isToolEnabledFn: (): boolean => false,
      },
    );

    const exitCode = await runDispatcher(dependencies);

    expect(exitCode).toBe(1);
    expect(dependencies.focusWindowCalls).toHaveLength(0);
    expect(dependencies.stderrChunks.join('')).toContain("Tool 'focus-window' is disabled.");
  });
});
