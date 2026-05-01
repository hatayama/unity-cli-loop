// Dispatcher output and process spawning are the public CLI behavior.
/* eslint-disable security/detect-non-literal-fs-filename */

import assert from 'node:assert';
import { spawn, type SpawnOptions } from 'child_process';
import { existsSync, readdirSync, readFileSync, type Dirent } from 'fs';
import { createRequire } from 'module';
import { basename, dirname, join, resolve } from 'path';
import { runFocusWindowCommand } from './commands/focus-window.js';
import { type LaunchCommandOptions, runLaunchCommand } from './commands/launch.js';
import { isToolEnabled } from './tool-settings-loader.js';
import { VERSION } from './version.js';

const PROJECT_LOCAL_CLI_RELATIVE_PATH = join('.uloop', 'bin', 'uloop.cjs');
const WINDOWS_PROJECT_LOCAL_CLI_RELATIVE_PATH = join('.uloop', 'bin', 'uloop.cmd');
const BUNDLED_PROJECT_CLI_FILE_NAME = 'cli.bundle.cjs';
const VERSION_ARGS = new Set(['--version', '-v']);
const HELP_ARGS = new Set(['--help', '-h']);
const DISPATCHER_COMMAND_LAUNCH = 'launch';
const DISPATCHER_COMMAND_FOCUS_WINDOW = 'focus-window';
const BUNDLED_CLI_COMMANDS = new Set(['completion', 'skills', 'update']);
const DISPATCHER_IN_PROCESS_ENV = 'ULOOP_DISPATCHER_IN_PROCESS';
const FOCUS_WINDOW_TOOL_NAME = 'focus-window';
const CHILD_SEARCH_MAX_DEPTH = 3;
const EXCLUDED_PROJECT_SEARCH_DIRS = new Set([
  'node_modules',
  '.git',
  'Temp',
  'obj',
  'Build',
  'Builds',
  'Logs',
  'Library',
]);
export const PROJECT_LOCAL_CLI_IN_PROCESS_MARKER = 'uloop-cli-in-process-entrypoint-v2';

type OutputWriter = {
  write(chunk: string): boolean;
};

export type DispatcherChildProcess = {
  on(event: 'exit', listener: (code: number | null, signal: NodeJS.Signals | null) => void): void;
  on(event: 'error', listener: (error: Error) => void): void;
};

export type DispatcherSpawnFn = (
  command: string,
  args: readonly string[],
  options: SpawnOptions & { cwd: string },
) => DispatcherChildProcess;

export type DispatcherChdirFn = (path: string) => void;
export type DispatcherLoadModuleFn = (modulePath: string, args: readonly string[]) => Promise<void>;

type ProjectLocalCliModule = {
  runCli?: (args: readonly string[]) => Promise<void>;
  default?: {
    runCli?: (args: readonly string[]) => Promise<void>;
  };
};

export type DispatcherCommandContext = {
  readonly cwd: string;
  readonly stdout: OutputWriter;
  readonly stderr: OutputWriter;
};

export type DispatcherLaunchCommandFn = (
  projectPath: string | undefined,
  options: LaunchCommandOptions,
  context: DispatcherCommandContext,
) => Promise<number>;

export type DispatcherFocusWindowCommandFn = (
  projectPath: string | undefined,
  context: DispatcherCommandContext,
) => Promise<number>;

export type DispatcherIsToolEnabledFn = (toolName: string, projectPath?: string) => boolean;

export type DispatcherDependencies = {
  readonly args: readonly string[];
  readonly cwd: string;
  readonly platform: NodeJS.Platform;
  readonly stdout: OutputWriter;
  readonly stderr: OutputWriter;
  readonly bundledCliPath: string;
  readonly nodePath: string;
  readonly spawnFn: DispatcherSpawnFn;
  readonly chdirFn: DispatcherChdirFn;
  readonly loadModuleFn: DispatcherLoadModuleFn;
  readonly launchCommandFn: DispatcherLaunchCommandFn;
  readonly focusWindowCommandFn: DispatcherFocusWindowCommandFn;
  readonly isToolEnabledFn: DispatcherIsToolEnabledFn;
};

type ProjectPathArgument =
  | { readonly value: string; readonly error: null }
  | { readonly value: null; readonly error: string | null };

type ProjectRootResolution =
  | { readonly projectRoot: string; readonly error: null }
  | { readonly projectRoot: null; readonly error: string };

function readDirectoryEntriesOrEmpty(path: string): Dirent[] {
  try {
    return readdirSync(path, { withFileTypes: true });
  } catch (error) {
    if (isSkippableDirectoryReadError(error)) {
      return [];
    }

    throw error;
  }
}

function isSkippableDirectoryReadError(error: unknown): boolean {
  if (typeof error !== 'object' || error === null || !('code' in error)) {
    return false;
  }

  const code = (error as { readonly code?: unknown }).code;
  return code === 'EACCES' || code === 'EPERM' || code === 'ENOENT';
}

function isFileReadProbeError(error: unknown): boolean {
  if (typeof error !== 'object' || error === null || !('code' in error)) {
    return false;
  }

  const code = (error as { readonly code?: unknown }).code;
  return typeof code === 'string';
}

function createDefaultDependencies(): DispatcherDependencies {
  return {
    args: process.argv.slice(2),
    cwd: process.cwd(),
    platform: process.platform,
    stdout: process.stdout,
    stderr: process.stderr,
    bundledCliPath: getDefaultBundledCliPath(),
    nodePath: process.execPath,
    spawnFn: spawn,
    chdirFn: (path): void => {
      process.chdir(path);
    },
    loadModuleFn: loadProjectLocalCliInProcess,
    launchCommandFn: runDispatcherLaunchCommand,
    focusWindowCommandFn: runDispatcherFocusWindowCommand,
    isToolEnabledFn: isToolEnabled,
  };
}

export async function loadProjectLocalCliInProcess(
  modulePath: string,
  args: readonly string[],
): Promise<void> {
  const previousInProcessValue = process.env[DISPATCHER_IN_PROCESS_ENV];
  process.env[DISPATCHER_IN_PROCESS_ENV] = '1';
  try {
    const requireProjectLocalCli = createRequire(modulePath);
    const projectLocalCliModule = requireProjectLocalCli(modulePath) as ProjectLocalCliModule;
    const runCli = projectLocalCliModule.runCli ?? projectLocalCliModule.default?.runCli;
    assert(typeof runCli === 'function', 'project-local CLI bundle must export runCli');
    await runCli(args);
  } finally {
    if (previousInProcessValue === undefined) {
      delete process.env[DISPATCHER_IN_PROCESS_ENV];
    } else {
      process.env[DISPATCHER_IN_PROCESS_ENV] = previousInProcessValue;
    }
  }
}

function getDefaultBundledCliPath(): string {
  return join(dirname(__filename), BUNDLED_PROJECT_CLI_FILE_NAME);
}

function isVersionRequest(args: readonly string[]): boolean {
  return args.length === 1 && VERSION_ARGS.has(args[0]);
}

function isDispatcherLaunchRequest(args: readonly string[]): boolean {
  return args[0] === DISPATCHER_COMMAND_LAUNCH;
}

function isDispatcherFocusWindowRequest(args: readonly string[]): boolean {
  return args[0] === DISPATCHER_COMMAND_FOCUS_WINDOW;
}

function isBundledCliRequest(args: readonly string[]): boolean {
  if (args.length === 0) {
    return true;
  }

  if (args.some((arg) => HELP_ARGS.has(arg))) {
    return true;
  }

  if (args.includes('--list-commands')) {
    return true;
  }

  if (args.some((arg) => arg === '--list-options' || arg.startsWith('--list-options='))) {
    return true;
  }

  return BUNDLED_CLI_COMMANDS.has(args[0]);
}

function findProjectPathArgument(args: readonly string[]): ProjectPathArgument {
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];

    if (arg.startsWith('--project-path=')) {
      const value = arg.slice('--project-path='.length);
      return value.length === 0
        ? { value: null, error: '--project-path requires a value.' }
        : { value, error: null };
    }

    if (arg !== '--project-path') {
      continue;
    }

    const value = args[index + 1];
    if (value === undefined || value.startsWith('-')) {
      return { value: null, error: '--project-path requires a value.' };
    }

    return { value, error: null };
  }

  return { value: null, error: null };
}

function normalizeProjectLocalCliArgs(
  args: readonly string[],
  projectRoot: string,
): readonly string[] {
  const normalizedArgs: string[] = [];
  let foundProjectPath = false;

  for (let index = 0; index < args.length; index++) {
    const arg = args[index];

    if (arg === '--project-path') {
      foundProjectPath = true;
      index++;
      continue;
    }

    if (arg.startsWith('--project-path=')) {
      foundProjectPath = true;
      continue;
    }

    normalizedArgs.push(arg);
  }

  return foundProjectPath ? [...normalizedArgs, '--project-path', projectRoot] : args;
}

function isUnityProject(projectRoot: string): boolean {
  return (
    existsSync(join(projectRoot, 'Assets')) && existsSync(join(projectRoot, 'ProjectSettings'))
  );
}

function findUnityProjectInParents(startPath: string): string | null {
  let currentPath = resolve(startPath);

  while (true) {
    if (isUnityProject(currentPath)) {
      return currentPath;
    }

    if (existsSync(join(currentPath, '.git'))) {
      return null;
    }

    const parentPath = dirname(currentPath);
    if (parentPath === currentPath) {
      return null;
    }

    currentPath = parentPath;
  }
}

function findUnityProjectsInChildren(startPath: string): string[] {
  const projects: string[] = [];

  function scan(currentPath: string, depth: number): void {
    if (depth > CHILD_SEARCH_MAX_DEPTH || !existsSync(currentPath)) {
      return;
    }

    if (isUnityProject(currentPath)) {
      projects.push(currentPath);
      return;
    }

    const entries: Dirent[] = readDirectoryEntriesOrEmpty(currentPath);
    for (const entry of entries) {
      if (!entry.isDirectory() || EXCLUDED_PROJECT_SEARCH_DIRS.has(entry.name)) {
        continue;
      }

      scan(join(currentPath, entry.name), depth + 1);
    }
  }

  scan(resolve(startPath), 0);
  return projects.sort();
}

function findUnityProjectRoot(
  startPath: string,
  platform: NodeJS.Platform = process.platform,
): string | null {
  const currentProjectRoot = resolve(startPath);
  if (isUnityProject(currentProjectRoot)) {
    return currentProjectRoot;
  }

  const childProjectRoots = findUnityProjectsInChildren(currentProjectRoot);
  if (childProjectRoots.length > 0) {
    return (
      childProjectRoots.find(
        (childProjectRoot) => findProjectLocalCliPath(childProjectRoot, platform) !== null,
      ) ?? childProjectRoots[0]
    );
  }

  return findUnityProjectInParents(currentProjectRoot);
}

function resolveProjectRoot(
  args: readonly string[],
  cwd: string,
  platform: NodeJS.Platform,
): ProjectRootResolution {
  assert(cwd.length > 0, 'cwd must not be empty');

  const projectPathArgument = findProjectPathArgument(args);
  if (projectPathArgument.error !== null) {
    return { projectRoot: null, error: projectPathArgument.error };
  }

  if (projectPathArgument.value !== null) {
    const explicitProjectRoot = resolve(cwd, projectPathArgument.value);
    if (!isUnityProject(explicitProjectRoot)) {
      return {
        projectRoot: null,
        error: `--project-path does not point to a Unity project: ${explicitProjectRoot}`,
      };
    }

    return { projectRoot: explicitProjectRoot, error: null };
  }

  const discoveredProjectRoot = findUnityProjectRoot(cwd, platform);
  if (discoveredProjectRoot === null) {
    return {
      projectRoot: null,
      error:
        'Could not find a Unity project. Run uloop from inside a Unity project or pass --project-path.',
    };
  }

  return { projectRoot: discoveredProjectRoot, error: null };
}

function getProjectLocalCliCandidatePaths(
  projectRoot: string,
  platform: NodeJS.Platform,
): string[] {
  const cliPath = join(projectRoot, PROJECT_LOCAL_CLI_RELATIVE_PATH);
  if (platform !== 'win32') {
    return [cliPath];
  }

  return [join(projectRoot, WINDOWS_PROJECT_LOCAL_CLI_RELATIVE_PATH), cliPath];
}

function findProjectLocalCliPath(projectRoot: string, platform: NodeJS.Platform): string | null {
  const candidatePaths = getProjectLocalCliCandidatePaths(projectRoot, platform);
  return candidatePaths.find((candidatePath) => existsSync(candidatePath)) ?? null;
}

function hasInProcessEntrypoint(localCliPath: string): boolean {
  try {
    return readFileSync(localCliPath, 'utf8').includes(PROJECT_LOCAL_CLI_IN_PROCESS_MARKER);
  } catch (error) {
    if (isFileReadProbeError(error)) {
      return false;
    }

    throw error;
  }
}

function canLoadProjectLocalCliInProcess(
  localCliPath: string,
  dependencies: DispatcherDependencies,
): boolean {
  return dependencies.platform !== 'win32' && hasInProcessEntrypoint(localCliPath);
}

async function runProjectLocalCli(
  localCliPath: string,
  args: readonly string[],
  projectRoot: string,
  dependencies: DispatcherDependencies,
): Promise<number> {
  if (canLoadProjectLocalCliInProcess(localCliPath, dependencies)) {
    dependencies.chdirFn(projectRoot);
    await dependencies.loadModuleFn(localCliPath, args);
    return 0;
  }

  return new Promise((resolveExitCode) => {
    const spawnOptions: SpawnOptions & { cwd: string } = {
      cwd: projectRoot,
      stdio: 'inherit',
    };
    let command = localCliPath;
    let forwardedArgs = args;

    if (dependencies.platform === 'win32' && localCliPath.endsWith('.cmd')) {
      spawnOptions.shell = true;
    } else if (localCliPath.endsWith('.cjs')) {
      command = dependencies.nodePath;
      forwardedArgs = [localCliPath, ...args];
    }

    const child = dependencies.spawnFn(command, forwardedArgs, spawnOptions);

    child.on('exit', (code) => {
      resolveExitCode(code ?? 1);
    });

    child.on('error', (error) => {
      dependencies.stderr.write(`Failed to start project-local uloop CLI: ${error.message}\n`);
      resolveExitCode(1);
    });
  });
}

async function runBundledCli(
  args: readonly string[],
  dependencies: DispatcherDependencies,
): Promise<number> {
  if (!existsSync(dependencies.bundledCliPath)) {
    dependencies.stderr.write(`Bundled uloop CLI was not found: ${dependencies.bundledCliPath}\n`);
    return 1;
  }

  return new Promise((resolveExitCode) => {
    const child = dependencies.spawnFn(
      dependencies.nodePath,
      [dependencies.bundledCliPath, ...args],
      {
        cwd: dependencies.cwd,
        stdio: 'inherit',
      },
    );

    child.on('exit', (code) => {
      resolveExitCode(code ?? 1);
    });

    child.on('error', (error) => {
      dependencies.stderr.write(`Failed to start bundled uloop CLI: ${error.message}\n`);
      resolveExitCode(1);
    });
  });
}

export async function runDispatcher(
  dependencies: DispatcherDependencies = createDefaultDependencies(),
): Promise<number> {
  if (isVersionRequest(dependencies.args)) {
    dependencies.stdout.write(`${VERSION}\n`);
    return 0;
  }

  const commandContext: DispatcherCommandContext = {
    cwd: dependencies.cwd,
    stdout: dependencies.stdout,
    stderr: dependencies.stderr,
  };
  if (isDispatcherLaunchRequest(dependencies.args)) {
    return runDispatcherLaunchRequest(
      dependencies.args.slice(1),
      commandContext,
      dependencies.launchCommandFn,
    );
  }

  if (isDispatcherFocusWindowRequest(dependencies.args)) {
    return runDispatcherFocusWindowRequest(
      dependencies.args.slice(1),
      commandContext,
      dependencies.focusWindowCommandFn,
      dependencies.isToolEnabledFn,
    );
  }

  if (isBundledCliRequest(dependencies.args)) {
    return runBundledCli(dependencies.args, dependencies);
  }

  const projectResolution = resolveProjectRoot(
    dependencies.args,
    dependencies.cwd,
    dependencies.platform,
  );
  if (projectResolution.error !== null) {
    dependencies.stderr.write(`${projectResolution.error}\n`);
    return 1;
  }

  const localCliPath = findProjectLocalCliPath(
    projectResolution.projectRoot,
    dependencies.platform,
  );
  if (localCliPath === null) {
    dependencies.stderr.write(
      `Project-local uloop CLI was not found at ${PROJECT_LOCAL_CLI_RELATIVE_PATH}.\n` +
        'Open Unity CLI Loop setup in this project and refresh the CLI installation.\n',
    );
    return 1;
  }

  return runProjectLocalCli(
    localCliPath,
    normalizeProjectLocalCliArgs(dependencies.args, projectResolution.projectRoot),
    projectResolution.projectRoot,
    dependencies,
  );
}

function shouldRunDispatcherEntryPoint(): boolean {
  if (process.env.JEST_WORKER_ID === undefined) {
    return true;
  }

  return basename(process.argv[1] ?? '') === 'dispatcher.bundle.cjs';
}

if (shouldRunDispatcherEntryPoint()) {
  void (async (): Promise<void> => {
    const exitCode = await runDispatcher();
    process.exit(exitCode);
  })();
}

type DispatcherLaunchParseResult =
  | { projectPath: string | undefined; options: LaunchCommandOptions; message: null }
  | { projectPath: null; options: null; message: string };

async function runDispatcherLaunchRequest(
  args: readonly string[],
  context: DispatcherCommandContext,
  launchCommandFn: DispatcherLaunchCommandFn,
): Promise<number> {
  if (args.length === 1 && HELP_ARGS.has(args[0])) {
    context.stdout.write(getDispatcherLaunchHelp());
    return 0;
  }

  const parsedArgs = parseDispatcherLaunchArgs(args);
  if (parsedArgs.message !== null) {
    context.stderr.write(`${parsedArgs.message}\n`);
    return 1;
  }

  return launchCommandFn(parsedArgs.projectPath, parsedArgs.options, context);
}

async function runDispatcherLaunchCommand(
  projectPath: string | undefined,
  options: LaunchCommandOptions,
): Promise<number> {
  await runLaunchCommand(projectPath, options);
  return 0;
}

function parseDispatcherLaunchArgs(args: readonly string[]): DispatcherLaunchParseResult {
  const options: LaunchCommandOptions = {};
  let projectPath: string | undefined;
  const remainingArgs = [...args];

  while (remainingArgs.length > 0) {
    const arg = remainingArgs.shift();
    assert(arg !== undefined, 'remainingArgs length must be checked before shift');

    if (arg === '-r' || arg === '--restart') {
      options.restart = true;
      continue;
    }

    if (arg === '-d' || arg === '--delete-recovery') {
      options.deleteRecovery = true;
      continue;
    }

    if (arg === '-q' || arg === '--quit') {
      options.quit = true;
      continue;
    }

    if (arg === '-a' || arg === '--add-unity-hub') {
      options.addUnityHub = true;
      continue;
    }

    if (arg === '-f' || arg === '--favorite') {
      options.favorite = true;
      continue;
    }

    if (arg === '-p' || arg === '--platform') {
      const valueResult = takeRequiredOptionValue(arg, remainingArgs);
      if (valueResult.error !== null) {
        return { projectPath: null, options: null, message: valueResult.error };
      }

      options.platform = valueResult.value;
      continue;
    }

    if (arg.startsWith('--platform=')) {
      const value = arg.slice('--platform='.length);
      if (value.length === 0) {
        return { projectPath: null, options: null, message: '--platform requires a value.' };
      }

      options.platform = value;
      continue;
    }

    if (arg === '--max-depth') {
      const valueResult = takeRequiredIntegerOptionValue('--max-depth', remainingArgs);
      if (valueResult.error !== null) {
        return { projectPath: null, options: null, message: valueResult.error };
      }

      options.maxDepth = valueResult.value;
      continue;
    }

    if (arg.startsWith('--max-depth=')) {
      const value = arg.slice('--max-depth='.length);
      if (value.length === 0) {
        return { projectPath: null, options: null, message: '--max-depth requires a value.' };
      }

      options.maxDepth = value;
      continue;
    }

    if (arg === '--project-path') {
      const valueResult = takeRequiredOptionValue('--project-path', remainingArgs);
      if (valueResult.error !== null) {
        return { projectPath: null, options: null, message: valueResult.error };
      }

      projectPath = valueResult.value;
      continue;
    }

    if (arg.startsWith('--project-path=')) {
      const value = arg.slice('--project-path='.length);
      if (value.length === 0) {
        return { projectPath: null, options: null, message: '--project-path requires a value.' };
      }

      projectPath = value;
      continue;
    }

    if (arg.startsWith('-')) {
      return { projectPath: null, options: null, message: `Unknown launch option: ${arg}` };
    }

    if (projectPath !== undefined) {
      return {
        projectPath: null,
        options: null,
        message: `Unexpected extra launch argument: ${arg}`,
      };
    }

    projectPath = arg;
  }

  return { projectPath, options, message: null };
}

function getDispatcherLaunchHelp(): string {
  return [
    'Usage: uloop launch [options] [project-path]',
    '',
    'Open a Unity project with the matching Editor version installed by Unity Hub.',
    '',
    'Options:',
    '  -r, --restart              Kill running Unity and restart',
    '  -d, --delete-recovery      Delete Assets/_Recovery before launch',
    '  -q, --quit                 Gracefully quit running Unity',
    '  -p, --platform <platform>  Build target (e.g., Android, iOS)',
    '  --max-depth <n>            Search depth when project-path is omitted',
    '  -a, --add-unity-hub        Add to Unity Hub (does not launch)',
    '  -f, --favorite             Add to Unity Hub as favorite (does not launch)',
    '  --project-path <path>      Unity project path',
    '  -h, --help                 Display help for command',
    '',
  ].join('\n');
}

async function runDispatcherFocusWindowRequest(
  args: readonly string[],
  context: DispatcherCommandContext,
  focusWindowCommandFn: DispatcherFocusWindowCommandFn,
  isToolEnabledFn: DispatcherIsToolEnabledFn,
): Promise<number> {
  if (args.length === 1 && HELP_ARGS.has(args[0])) {
    context.stdout.write(getDispatcherFocusWindowHelp());
    return 0;
  }

  const parsedArgs = parseDispatcherFocusWindowArgs(args);
  if (parsedArgs.error !== null) {
    context.stderr.write(`${parsedArgs.error}\n`);
    return 1;
  }

  const settingsProjectRoot = resolveFocusWindowSettingsProjectRoot(
    parsedArgs.projectPath,
    context.cwd,
  );
  if (
    settingsProjectRoot !== undefined &&
    !isToolEnabledFn(FOCUS_WINDOW_TOOL_NAME, settingsProjectRoot)
  ) {
    writeToolDisabledError(FOCUS_WINDOW_TOOL_NAME, context.stderr);
    return 1;
  }

  return focusWindowCommandFn(parsedArgs.projectPath, context);
}

function resolveFocusWindowSettingsProjectRoot(
  projectPath: string | undefined,
  cwd: string,
): string | undefined {
  if (projectPath !== undefined) {
    const resolvedProjectPath = resolve(cwd, projectPath);
    return isUnityProject(resolvedProjectPath) ? resolvedProjectPath : undefined;
  }

  return findUnityProjectRoot(cwd) ?? undefined;
}

function writeToolDisabledError(toolName: string, stderr: OutputWriter): void {
  stderr.write(`Tool '${toolName}' is disabled.\n`);
  stderr.write('You can enable it in Unity: Window > Unity CLI Loop > Settings > Tool Settings\n');
}

async function runDispatcherFocusWindowCommand(
  projectPath: string | undefined,
  context: DispatcherCommandContext,
): Promise<number> {
  return runFocusWindowCommand(
    { projectPath },
    {
      log: (message: string): void => {
        context.stdout.write(`${message}\n`);
      },
      error: (message: string): void => {
        context.stderr.write(`${message}\n`);
      },
    },
  );
}

type DispatcherFocusWindowParseResult =
  | { projectPath: string | undefined; error: null }
  | { projectPath: null; error: string };

type RequiredOptionValueResult = { value: string; error: null } | { value: null; error: string };

function takeRequiredOptionValue(
  optionName: string,
  remainingArgs: string[],
): RequiredOptionValueResult {
  const value = remainingArgs.shift();
  if (value === undefined || value.startsWith('-')) {
    return { value: null, error: `${optionName} requires a value.` };
  }

  return { value, error: null };
}

function takeRequiredIntegerOptionValue(
  optionName: string,
  remainingArgs: string[],
): RequiredOptionValueResult {
  const value = remainingArgs.shift();
  if (value === undefined || (value.startsWith('-') && !/^-?\d+$/.test(value))) {
    return { value: null, error: `${optionName} requires a value.` };
  }

  return { value, error: null };
}

function parseDispatcherFocusWindowArgs(args: readonly string[]): DispatcherFocusWindowParseResult {
  let projectPath: string | undefined;
  const remainingArgs = [...args];

  while (remainingArgs.length > 0) {
    const arg = remainingArgs.shift();
    assert(arg !== undefined, 'remainingArgs length must be checked before shift');

    if (arg === '--project-path') {
      const valueResult = takeRequiredOptionValue('--project-path', remainingArgs);
      if (valueResult.error !== null) {
        return { projectPath: null, error: valueResult.error };
      }

      projectPath = valueResult.value;
      continue;
    }

    if (arg.startsWith('--project-path=')) {
      const value = arg.slice('--project-path='.length);
      if (value.length === 0) {
        return { projectPath: null, error: '--project-path requires a value.' };
      }

      projectPath = value;
      continue;
    }

    if (arg.startsWith('-')) {
      return { projectPath: null, error: `Unknown focus-window option: ${arg}` };
    }

    if (projectPath !== undefined) {
      return {
        projectPath: null,
        error: `Unexpected extra focus-window argument: ${arg}`,
      };
    }

    projectPath = arg;
  }

  return { projectPath, error: null };
}

function getDispatcherFocusWindowHelp(): string {
  return [
    'Usage: uloop focus-window [options] [project-path]',
    '',
    'Bring Unity Editor window to front using OS-level commands.',
    '',
    'Options:',
    '  --project-path <path>  Unity project path',
    '  -h, --help            Display help for command',
    '',
  ].join('\n');
}
