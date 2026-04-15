/**
 * CLI entry point for uloop command.
 * Provides direct Unity communication without MCP server.
 * Commands are dynamically registered from tools.json cache.
 */

// CLI tools output to console by design, file paths are constructed from trusted sources (project root detection),
// and object keys come from tool definitions which are internal trusted data
/* eslint-disable no-console, security/detect-non-literal-fs-filename, security/detect-object-injection */

import { PRODUCT_DISPLAY_NAME, MENU_PATH_SERVER } from './cli-constants';
import { existsSync, readFileSync, writeFileSync, mkdirSync, unlinkSync } from 'fs';
import { join, basename, dirname } from 'path';
import { homedir } from 'os';
import { spawn } from 'child_process';
import { Command, Option } from 'commander';
import {
  executeToolCommand,
  listAvailableTools,
  GlobalOptions,
  syncTools,
  isVersionOlder,
} from './execute-tool.js';
import {
  loadToolsCache,
  hasCacheFile,
  getDefaultTools,
  getDefaultToolNames,
  ToolDefinition,
  ToolProperty,
  getCachedServerVersion,
} from './tool-cache.js';
import { pascalToKebabCase } from './arg-parser.js';
import { registerSkillsCommand } from './skills/skills-command.js';
import { registerLaunchCommand } from './commands/launch.js';
import { registerFocusWindowCommand } from './commands/focus-window.js';
import { VERSION } from './version.js';
import { findUnityProjectRoot } from './project-root.js';
import {
  validateProjectPath,
  UnityNotRunningError,
  UnityServerNotRunningError,
} from './port-resolver.js';
import { ProjectMismatchError } from './project-validator.js';
import { filterEnabledTools, isToolEnabled } from './tool-settings-loader.js';
import { getProjectResolutionErrorLines } from './cli-project-error.js';

interface CliOptions extends GlobalOptions {
  [key: string]: unknown;
}

const FOCUS_WINDOW_COMMAND = 'focus-window' as const;
const LAUNCH_COMMAND = 'launch' as const;
const UPDATE_COMMAND = 'update' as const;

const HELP_GROUP_BUILTIN_TOOLS = 'Built-in Tools:' as const;
const HELP_GROUP_THIRD_PARTY_TOOLS = 'Third-party Tools:' as const;
const HELP_GROUP_CLI_COMMANDS = 'CLI Commands:' as const;

const HELP_GROUP_ORDER = [
  HELP_GROUP_CLI_COMMANDS,
  HELP_GROUP_BUILTIN_TOOLS,
  HELP_GROUP_THIRD_PARTY_TOOLS,
] as const;

// commander.js built-in flags that exit immediately without needing Unity
const NO_SYNC_FLAGS = ['-v', '--version', '-h', '--help'] as const;

const BUILTIN_COMMANDS = [
  'list',
  'sync',
  'completion',
  UPDATE_COMMAND,
  'fix',
  'skills',
  LAUNCH_COMMAND,
  FOCUS_WINDOW_COMMAND,
] as const;

/**
 * Register a tool as a CLI command dynamically.
 */
function registerToolCommand(program: Command, tool: ToolDefinition, helpGroup: string): void {
  // Skip if already registered as a built-in command
  if (BUILTIN_COMMANDS.includes(tool.name as (typeof BUILTIN_COMMANDS)[number])) {
    return;
  }
  const firstLine: string = tool.description.split('\n')[0];
  const cmd = program.command(tool.name).description(firstLine).helpGroup(helpGroup);

  // Add options from inputSchema.properties
  const properties = tool.inputSchema.properties;
  for (const [propName, propInfo] of Object.entries(properties)) {
    const optionStr = generateOptionString(propName, propInfo);
    const description = buildOptionDescription(propInfo);
    const defaultValue = propInfo.default;
    if (defaultValue !== undefined && defaultValue !== null) {
      // Convert default values to strings for consistent CLI handling
      const defaultStr = convertDefaultToString(defaultValue);
      cmd.option(optionStr, description, defaultStr);
    } else {
      cmd.option(optionStr, description);
    }
  }

  cmd.addOption(createHiddenPortOption());
  cmd.option('--project-path <path>', 'Unity project path');

  cmd.action(async (options: CliOptions) => {
    const params = buildParams(options, properties);

    // Unescape \! to ! for execute-dynamic-code
    // Some shells (e.g., Claude Code's bash wrapper) escape ! as \!
    if (tool.name === 'execute-dynamic-code' && params['Code']) {
      const code = params['Code'] as string;
      params['Code'] = code.replace(/\\!/g, '!');
    }

    await runWithErrorHandling(() =>
      executeToolCommand(tool.name, params, extractGlobalOptions(options)),
    );
  });
}

/**
 * Convert default value to string for CLI option registration.
 */
function convertDefaultToString(value: unknown): string {
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'boolean' || typeof value === 'number') {
    return String(value);
  }
  return JSON.stringify(value);
}

/**
 * Generate commander.js option string from property info.
 * All types use value format (--option <value>) for consistency with MCP.
 */
function generateOptionString(propName: string, propInfo: ToolProperty): string {
  const kebabName = pascalToKebabCase(propName);
  void propInfo; // All types now use value format
  return `--${kebabName} <value>`;
}

/**
 * Build option description with enum values if present.
 */
function buildOptionDescription(propInfo: ToolProperty): string {
  let desc = propInfo.description || '';
  if (propInfo.enum && propInfo.enum.length > 0) {
    desc += ` (${propInfo.enum.join(', ')})`;
  }
  return desc;
}

/**
 * Build parameters from CLI options.
 */
function buildParams(
  options: Record<string, unknown>,
  properties: Record<string, ToolProperty>,
): Record<string, unknown> {
  const params: Record<string, unknown> = {};

  for (const propName of Object.keys(properties)) {
    const camelName = propName.charAt(0).toLowerCase() + propName.slice(1);
    const value = options[camelName];

    if (value !== undefined) {
      const propInfo = properties[propName];
      params[propName] = convertValue(value, propInfo);
    }
  }

  return params;
}

/**
 * Convert CLI value to appropriate type based on property info.
 */
function convertValue(value: unknown, propInfo: ToolProperty): unknown {
  const lowerType = propInfo.type.toLowerCase();

  if (lowerType === 'boolean' && typeof value === 'string') {
    const lower = value.toLowerCase();
    if (lower === 'true') {
      return true;
    }
    if (lower === 'false') {
      return false;
    }
    throw new Error(`Invalid boolean value: ${value}. Use 'true' or 'false'.`);
  }

  if (lowerType === 'array' && typeof value === 'string') {
    // Handle JSON array format (e.g., "[]" or "[\"item1\",\"item2\"]")
    if (value.startsWith('[') && value.endsWith(']')) {
      try {
        const parsed: unknown = JSON.parse(value);
        if (Array.isArray(parsed)) {
          return parsed;
        }
      } catch {
        // Fall through to comma-separated handling
      }
    }
    // Handle comma-separated format (e.g., "item1,item2")
    return value.split(',').map((s) => s.trim());
  }

  if (lowerType === 'integer' && typeof value === 'string') {
    const parsed = parseInt(value, 10);
    if (isNaN(parsed)) {
      throw new Error(`Invalid integer value: ${value}`);
    }
    return parsed;
  }

  if (lowerType === 'number' && typeof value === 'string') {
    const parsed = parseFloat(value);
    if (isNaN(parsed)) {
      throw new Error(`Invalid number value: ${value}`);
    }
    return parsed;
  }

  if (lowerType === 'object') {
    if (typeof value === 'string') {
      const trimmed = value.trim();
      if (!trimmed.startsWith('{') || !trimmed.endsWith('}')) {
        throw new Error(`Invalid object value: ${value}. Use JSON object syntax.`);
      }
      try {
        const parsed: unknown = JSON.parse(trimmed);
        if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
          return parsed;
        }
      } catch {
        // fall through to error below
      }
      throw new Error(`Invalid object value: ${value}. Use JSON object syntax.`);
    }
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      return value;
    }
    throw new Error(`Invalid object value: ${String(value)}. Use JSON object syntax.`);
  }

  return value;
}

function getToolHelpGroup(toolName: string, defaultToolNames: ReadonlySet<string>): string {
  return defaultToolNames.has(toolName) ? HELP_GROUP_BUILTIN_TOOLS : HELP_GROUP_THIRD_PARTY_TOOLS;
}

// Option instances are mutated by commander, so each command needs its own
function createHiddenPortOption(): Option {
  return new Option('-p, --port <port>', 'Unity TCP port').hideHelp();
}

function createProgram(): Command {
  const program = new Command();

  program
    .name('uloop')
    .description('Unity MCP CLI - Direct communication with Unity Editor')
    .version(VERSION, '-v, --version', 'Output the version number')
    .showHelpAfterError('(run with -h for available options)')
    .configureHelp({
      sortSubcommands: true,
      groupItems<T extends Command | Option>(
        unsortedItems: T[],
        visibleItems: T[],
        getGroup: (item: T) => string,
      ): Map<string, T[]> {
        const groupMap = new Map<string, T[]>();
        for (const item of unsortedItems) {
          const group: string = getGroup(item);
          if (!groupMap.has(group)) {
            groupMap.set(group, []);
          }
        }
        for (const item of visibleItems) {
          const group: string = getGroup(item);
          let groupedItems: T[] | undefined = groupMap.get(group);
          if (groupedItems === undefined) {
            groupedItems = [];
            groupMap.set(group, groupedItems);
          }

          groupedItems.push(item);
        }

        const ordered = new Map<string, T[]>();
        for (const key of HELP_GROUP_ORDER) {
          const items: T[] | undefined = groupMap.get(key);
          if (items !== undefined) {
            ordered.set(key, items);
            groupMap.delete(key);
          }
        }

        for (const [key, value] of groupMap) {
          ordered.set(key, value);
        }

        return ordered;
      },
    });

  program.option('--list-commands', 'List all command names (for shell completion)');
  program.option('--list-options <cmd>', 'List options for a command (for shell completion)');
  program.commandsGroup(HELP_GROUP_CLI_COMMANDS);
  program.helpCommand(true);

  program
    .command('list')
    .description('List all available tools from Unity')
    .addOption(createHiddenPortOption())
    .option('--project-path <path>', 'Unity project path')
    .action(async (options: CliOptions) => {
      await runWithErrorHandling(() => listAvailableTools(extractGlobalOptions(options)));
    });

  program
    .command('sync')
    .description('Sync tool definitions from Unity to local cache')
    .addOption(createHiddenPortOption())
    .option('--project-path <path>', 'Unity project path')
    .action(async (options: CliOptions) => {
      await runWithErrorHandling(() => syncTools(extractGlobalOptions(options)));
    });

  program
    .command('completion')
    .description('Setup shell completion')
    .option('--install', 'Install completion to shell config file')
    .option('--shell <type>', 'Shell type: bash, zsh, or powershell')
    .action((options: { install?: boolean; shell?: string }) => {
      handleCompletion(options.install ?? false, options.shell);
    });

  program
    .command('update')
    .description('Update uloop CLI to the latest version')
    .action(() => {
      updateCli();
    });

  program
    .command('fix')
    .description('Clean up stale lock files that may prevent CLI from connecting')
    .option('--project-path <path>', 'Unity project path')
    .action(async (options: { projectPath?: string }) => {
      await runWithErrorHandling(() => {
        cleanupLockFiles(options.projectPath);
        return Promise.resolve();
      });
    });

  registerSkillsCommand(program);
  registerLaunchCommand(program);
  return program;
}

interface FastExecuteDynamicCodeCommand {
  params: Record<string, unknown>;
  globalOptions: GlobalOptions;
}

interface FastExecuteDynamicCodeDependencies {
  executeToolCommandFn: typeof executeToolCommand;
  isToolEnabledFn: typeof isToolEnabled;
  runWithErrorHandlingFn: typeof runWithErrorHandling;
  printToolDisabledErrorFn: typeof printToolDisabledError;
  exitFn: (code: number) => never;
}

const defaultFastExecuteDynamicCodeDependencies: FastExecuteDynamicCodeDependencies = {
  executeToolCommandFn: executeToolCommand,
  isToolEnabledFn: isToolEnabled,
  runWithErrorHandlingFn: runWithErrorHandling,
  printToolDisabledErrorFn: printToolDisabledError,
  exitFn: (code: number): never => process.exit(code),
};

const EXECUTE_DYNAMIC_CODE_PROPERTIES: Record<string, ToolProperty> =
  getDefaultTools().tools.find((tool: ToolDefinition) => tool.name === 'execute-dynamic-code')
    ?.inputSchema.properties ?? {};

const FAST_EXECUTE_DYNAMIC_CODE_OPTIONS = new Map<string, string>([
  ['--code', 'code'],
  ['--parameters', 'parameters'],
  ['--compile-only', 'compileOnly'],
  ['--project-path', 'projectPath'],
  ['--port', 'port'],
  ['-p', 'port'],
]);

function parseFastOptionValue(arg: string): [string, string] | null {
  const separatorIndex = arg.indexOf('=');
  if (separatorIndex === -1) {
    return null;
  }

  const optionName = arg.slice(0, separatorIndex);
  const optionValue = arg.slice(separatorIndex + 1);
  if (!FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.has(optionName)) {
    return null;
  }

  return [optionName, optionValue];
}

export function tryParseFastExecuteDynamicCodeCommand(
  args: readonly string[],
): FastExecuteDynamicCodeCommand | null {
  if (args[0] !== 'execute-dynamic-code') {
    return null;
  }

  if (args.includes('-h') || args.includes('--help')) {
    return null;
  }

  const options: Record<string, unknown> = {};

  for (let i = 1; i < args.length; i++) {
    const arg: string = args[i];
    if (!arg.startsWith('-')) {
      return null;
    }

    const inlineOption = parseFastOptionValue(arg);
    if (inlineOption !== null) {
      const [optionName, optionValue] = inlineOption;
      const optionKey = FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.get(optionName);
      if (optionKey === undefined) {
        return null;
      }

      options[optionKey] = optionValue;
      continue;
    }

    const optionKey = FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.get(arg);
    if (optionKey === undefined) {
      return null;
    }

    const optionValue = args[i + 1];
    if (optionValue === undefined) {
      return null;
    }

    options[optionKey] = optionValue;
    i++;
  }

  if (typeof options['code'] !== 'string') {
    return null;
  }

  const params = buildParams(options, EXECUTE_DYNAMIC_CODE_PROPERTIES);
  const code = params['Code'];
  if (typeof code === 'string') {
    params['Code'] = code.replace(/\\!/g, '!');
  }

  return {
    params,
    globalOptions: extractGlobalOptions(options),
  };
}

export async function tryHandleFastExecuteDynamicCodeCommand(
  args: readonly string[],
  dependencies: FastExecuteDynamicCodeDependencies = defaultFastExecuteDynamicCodeDependencies,
): Promise<boolean> {
  const command = tryParseFastExecuteDynamicCodeCommand(args);
  if (command === null) {
    return false;
  }

  if (!dependencies.isToolEnabledFn('execute-dynamic-code', command.globalOptions.projectPath)) {
    dependencies.printToolDisabledErrorFn('execute-dynamic-code');
    dependencies.exitFn(1);
  }

  await dependencies.runWithErrorHandlingFn(() =>
    dependencies.executeToolCommandFn(
      'execute-dynamic-code',
      command.params,
      command.globalOptions,
    ),
  );

  return true;
}

function extractGlobalOptions(options: Record<string, unknown>): GlobalOptions {
  return {
    port: options['port'] as string | undefined,
    projectPath: options['projectPath'] as string | undefined,
  };
}

function isConnectionError(message: string): boolean {
  return message.includes('ECONNREFUSED') || message.includes('EADDRNOTAVAIL');
}

function printToolDisabledError(cmdName: string): void {
  console.error(`\x1b[33mTool '${cmdName}' is disabled.\x1b[0m`);
  console.error('You can enable it in Unity: Window > uLoop > Tool Settings');
}

function printConnectionError(): void {
  console.error('\x1b[31mError: Cannot connect to Unity.\x1b[0m');
  console.error(`Make sure Unity Editor is open and ${PRODUCT_DISPLAY_NAME} server is running.`);
  console.error(`You can start the server from: ${MENU_PATH_SERVER}`);
  console.error('');
  console.error('[For AI] Please report the above to the user.');
}

/**
 * Print version mismatch diagnostic information when errors occur.
 * Only prints if versions are mismatched.
 */
function printVersionMismatchDiagnostic(): void {
  const serverVersion = getCachedServerVersion();
  if (serverVersion === undefined) {
    return;
  }

  if (serverVersion === VERSION) {
    return;
  }

  const isCliOlder = isVersionOlder(VERSION, serverVersion);
  const action = isCliOlder ? 'Update' : 'Downgrade';
  const updateCommand = `npm install -g uloop-cli@${serverVersion}`;

  console.error('');
  console.error('\x1b[33m━━━ Version Mismatch Diagnostic ━━━\x1b[0m');
  console.error('');
  console.error(`  CLI version:     ${VERSION}`);
  console.error(`  Unity package:   ${serverVersion}`);
  console.error('  Version mismatch may cause communication errors.');
  console.error('');
  console.error(`  To fix: ${action} CLI to match Unity package version`);
  console.error(`    ${updateCommand}`);
  console.error('');
  console.error('[For AI] Please ask the user if they would like to run this command.');
  console.error('\x1b[33m━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x1b[0m');
}

async function runWithErrorHandling(fn: () => Promise<void>): Promise<void> {
  try {
    await fn();
  } catch (error) {
    if (error instanceof UnityNotRunningError || error instanceof UnityServerNotRunningError) {
      for (const line of getProjectResolutionErrorLines(error)) {
        console.error(line.startsWith('Error: ') ? `\x1b[31m${line}\x1b[0m` : line);
      }
      process.exit(1);
    }

    if (error instanceof ProjectMismatchError) {
      for (const line of getProjectResolutionErrorLines(error)) {
        console.error(line.startsWith('Error: ') ? `\x1b[31m${line}\x1b[0m` : line);
      }
      process.exit(1);
    }

    const message = error instanceof Error ? error.message : String(error);

    // Unity busy states have clear causes - no version diagnostic needed
    if (message === 'UNITY_COMPILING') {
      console.error('\x1b[33m⏳ Unity is compiling scripts.\x1b[0m');
      console.error('Please wait for compilation to finish and try again.');
      console.error('');
      console.error('If the issue persists, run: uloop fix');
      process.exit(1);
    }

    if (message === 'UNITY_DOMAIN_RELOAD') {
      console.error('\x1b[33m⏳ Unity is reloading (Domain Reload in progress).\x1b[0m');
      console.error('Please wait a moment and try again.');
      console.error('');
      console.error('If the issue persists, run: uloop fix');
      process.exit(1);
    }

    if (message === 'UNITY_SERVER_STARTING') {
      console.error('\x1b[33m⏳ Unity server is starting.\x1b[0m');
      console.error('Please wait a moment and try again.');
      console.error('');
      console.error('If the issue persists, run: uloop fix');
      process.exit(1);
    }

    // Errors that may be caused by version mismatch - show diagnostic
    if (message === 'UNITY_NO_RESPONSE') {
      console.error('\x1b[33m⏳ Unity is busy (no response received).\x1b[0m');
      console.error('Unity may be compiling, reloading, or starting. Please wait and try again.');
      printVersionMismatchDiagnostic();
      process.exit(1);
    }

    if (isConnectionError(message)) {
      printConnectionError();
      printVersionMismatchDiagnostic();
      process.exit(1);
    }

    // Timeout errors
    if (message.includes('Request timed out')) {
      console.error(`\x1b[31mError: ${message}\x1b[0m`);
      printVersionMismatchDiagnostic();
      process.exit(1);
    }

    console.error(`\x1b[31mError: ${message}\x1b[0m`);
    process.exit(1);
  }
}

/**
 * Detect shell type from environment.
 */
function detectShell(): 'bash' | 'zsh' | 'powershell' | null {
  // Check $SHELL first (works for bash/zsh including MINGW64)
  const shell = process.env['SHELL'] || '';
  const shellName = basename(shell).replace(/\.exe$/i, ''); // Remove .exe for Windows
  if (shellName === 'zsh') {
    return 'zsh';
  }
  if (shellName === 'bash') {
    return 'bash';
  }

  // Check for PowerShell (only if $SHELL is not set)
  if (process.env['PSModulePath']) {
    return 'powershell';
  }

  return null;
}

/**
 * Get shell config file path.
 */
function getShellConfigPath(shell: 'bash' | 'zsh' | 'powershell'): string {
  const home = homedir();
  if (shell === 'zsh') {
    return join(home, '.zshrc');
  }
  if (shell === 'powershell') {
    // PowerShell profile path
    return join(home, 'Documents', 'WindowsPowerShell', 'Microsoft.PowerShell_profile.ps1');
  }
  return join(home, '.bashrc');
}

/**
 * Get completion script for a shell.
 */
function getCompletionScript(shell: 'bash' | 'zsh' | 'powershell'): string {
  if (shell === 'bash') {
    return `# uloop bash completion
_uloop_completions() {
  local cur="\${COMP_WORDS[COMP_CWORD]}"
  local cmd="\${COMP_WORDS[1]}"

  if [[ \${COMP_CWORD} -eq 1 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-commands 2>/dev/null)" -- "\${cur}"))
  elif [[ \${COMP_CWORD} -ge 2 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-options \${cmd} 2>/dev/null)" -- "\${cur}"))
  fi
}
complete -F _uloop_completions uloop`;
  }

  if (shell === 'powershell') {
    return `# uloop PowerShell completion
Register-ArgumentCompleter -Native -CommandName uloop -ScriptBlock {
  param($wordToComplete, $commandAst, $cursorPosition)
  $commands = $commandAst.CommandElements
  if ($commands.Count -eq 1) {
    uloop --list-commands 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  } elseif ($commands.Count -ge 2) {
    $cmd = $commands[1].ToString()
    uloop --list-options $cmd 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  }
}`;
  }

  /* eslint-disable no-useless-escape */
  return `# uloop zsh completion
_uloop() {
  local -a commands
  local -a options
  local -a used_options

  if (( CURRENT == 2 )); then
    commands=(\${(f)"$(uloop --list-commands 2>/dev/null)"})
    _describe 'command' commands
  elif (( CURRENT >= 3 )); then
    options=(\${(f)"$(uloop --list-options \${words[2]} 2>/dev/null)"})
    used_options=(\${words:2})
    for opt in \${used_options}; do
      options=(\${options:#\$opt})
    done
    _describe 'option' options
  fi
}
compdef _uloop uloop`;
  /* eslint-enable no-useless-escape */
}

/**
 * Get the currently installed version of uloop-cli from npm.
 */
export function getInstalledVersion(callback: (version: string | null) => void): void {
  const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  const child = spawn(npmCommand, ['list', '-g', 'uloop-cli', '--json']);

  let stdout = '';
  child.stdout.on('data', (data: Buffer) => {
    stdout += data.toString();
  });

  child.on('close', (code) => {
    if (code !== 0) {
      callback(null);
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(stdout);
    } catch {
      callback(null);
      return;
    }

    if (typeof parsed !== 'object' || parsed === null) {
      callback(null);
      return;
    }

    const deps = (parsed as Record<string, unknown>)['dependencies'];
    if (typeof deps !== 'object' || deps === null) {
      callback(null);
      return;
    }

    const uloopCli = (deps as Record<string, unknown>)['uloop-cli'];
    if (typeof uloopCli !== 'object' || uloopCli === null) {
      callback(null);
      return;
    }

    const version = (uloopCli as Record<string, unknown>)['version'];
    if (typeof version !== 'string') {
      callback(null);
      return;
    }

    callback(version);
  });

  child.on('error', () => {
    callback(null);
  });
}

/**
 * Update uloop CLI to the latest version using npm.
 */
export function updateCli(): void {
  const previousVersion = VERSION;
  console.log('Updating uloop-cli to the latest version...');

  const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  const child = spawn(npmCommand, ['install', '-g', 'uloop-cli@latest'], {
    stdio: 'inherit',
  });

  child.on('close', (code) => {
    if (code === 0) {
      getInstalledVersion((newVersion) => {
        if (newVersion && newVersion !== previousVersion) {
          console.log(`\n✅ uloop-cli updated: v${previousVersion} -> v${newVersion}`);
        } else {
          console.log(`\n✅ Already up to date (v${previousVersion})`);
        }
      });
    } else {
      console.error(`\n❌ Update failed with exit code ${code}`);
      process.exit(1);
    }
  });

  child.on('error', (err) => {
    console.error(`❌ Failed to run npm: ${err.message}`);
    process.exit(1);
  });
}

const LOCK_FILES = ['compiling.lock', 'domainreload.lock', 'serverstarting.lock'] as const;

/**
 * Clean up stale lock files that may prevent CLI from connecting to Unity.
 */
function cleanupLockFiles(projectPath?: string): void {
  const projectRoot =
    projectPath !== undefined ? validateProjectPath(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    console.error('Could not find Unity project root.');
    process.exit(1);
  }

  const tempDir = join(projectRoot, 'Temp');
  let cleaned = 0;

  for (const lockFile of LOCK_FILES) {
    const lockPath = join(tempDir, lockFile);
    if (existsSync(lockPath)) {
      unlinkSync(lockPath);
      console.log(`Removed: ${lockFile}`);
      cleaned++;
    }
  }

  if (cleaned === 0) {
    console.log('No lock files found.');
  } else {
    console.log(`\n✅ Cleaned up ${cleaned} lock file(s).`);
  }
}

/**
 * Handle completion command.
 */
function handleCompletion(install: boolean, shellOverride?: string): void {
  let shell: 'bash' | 'zsh' | 'powershell' | null;

  if (shellOverride) {
    const normalized = shellOverride.toLowerCase();
    if (normalized === 'bash' || normalized === 'zsh' || normalized === 'powershell') {
      shell = normalized;
    } else {
      console.error(`Unknown shell: ${shellOverride}. Supported: bash, zsh, powershell`);
      process.exit(1);
    }
  } else {
    shell = detectShell();
  }

  if (!shell) {
    console.error('Could not detect shell. Use --shell option: bash, zsh, or powershell');
    process.exit(1);
  }

  const script = getCompletionScript(shell);

  if (!install) {
    console.log(script);
    return;
  }

  // Install to shell config file
  const configPath = getShellConfigPath(shell);

  // PowerShell profile directory may not exist on fresh installations
  const configDir = dirname(configPath);
  if (!existsSync(configDir)) {
    mkdirSync(configDir, { recursive: true });
  }

  // Remove existing uloop completion and add new one
  let content = '';
  if (existsSync(configPath)) {
    content = readFileSync(configPath, 'utf-8');
    // Remove existing uloop completion block using markers
    content = content.replace(
      /\n?# >>> uloop completion >>>[\s\S]*?# <<< uloop completion <<<\n?/g,
      '',
    );
  }

  // Add new completion with markers
  const startMarker = '# >>> uloop completion >>>';
  const endMarker = '# <<< uloop completion <<<';

  if (shell === 'powershell') {
    const lineToAdd = `\n${startMarker}\n${script}\n${endMarker}\n`;
    writeFileSync(configPath, content + lineToAdd, 'utf-8');
  } else {
    // Include --shell option to ensure correct shell detection
    const evalLine = `eval "$(uloop completion --shell ${shell})"`;
    const lineToAdd = `\n${startMarker}\n${evalLine}\n${endMarker}\n`;
    writeFileSync(configPath, content + lineToAdd, 'utf-8');
  }

  console.log(`Completion installed to ${configPath}`);
  if (shell === 'powershell') {
    console.log('Restart PowerShell to enable completion.');
  } else {
    console.log(`Run 'source ${configPath}' or restart your shell to enable completion.`);
  }
}

/**
 * Handle --list-commands and --list-options before parsing.
 */
function handleCompletionOptions(): boolean {
  const args = process.argv.slice(2);
  const projectPath: string | undefined = extractSyncGlobalOptions(args).projectPath;

  if (args.includes('--list-commands')) {
    const tools = loadToolsCache();
    const enabledTools: ToolDefinition[] = filterEnabledTools(tools.tools, projectPath);
    const allCommands = [
      ...BUILTIN_COMMANDS.filter(
        (cmd) => cmd !== FOCUS_WINDOW_COMMAND || isToolEnabled(cmd, projectPath),
      ),
      ...enabledTools.map((t) => t.name),
    ];
    console.log(allCommands.sort().join('\n'));
    return true;
  }

  const listOptionsIdx = args.indexOf('--list-options');
  if (listOptionsIdx !== -1 && args[listOptionsIdx + 1]) {
    const cmdName = args[listOptionsIdx + 1];
    listOptionsForCommand(cmdName, projectPath);
    return true;
  }

  return false;
}

/**
 * List options for a specific command.
 */
function listOptionsForCommand(cmdName: string, projectPath?: string): void {
  // Built-in commands have no tool-specific options
  if (BUILTIN_COMMANDS.includes(cmdName as (typeof BUILTIN_COMMANDS)[number])) {
    return;
  }

  // Tool commands - only output tool-specific options
  const tools = loadToolsCache();
  const tool: ToolDefinition | undefined = filterEnabledTools(tools.tools, projectPath).find(
    (t) => t.name === cmdName,
  );
  if (!tool) {
    return;
  }

  const options: string[] = [];
  for (const propName of Object.keys(tool.inputSchema.properties)) {
    const kebabName = pascalToKebabCase(propName);
    options.push(`--${kebabName}`);
  }

  console.log(options.join('\n'));
}

/**
 * Check if a command exists in the current program.
 */
function commandExists(cmdName: string, projectPath?: string): boolean {
  if (cmdName === FOCUS_WINDOW_COMMAND) {
    return isToolEnabled(FOCUS_WINDOW_COMMAND, projectPath);
  }
  if (BUILTIN_COMMANDS.includes(cmdName as (typeof BUILTIN_COMMANDS)[number])) {
    return true;
  }
  const tools = loadToolsCache();
  return filterEnabledTools(tools.tools, projectPath).some((t) => t.name === cmdName);
}

function shouldSkipAutoSync(cmdName: string | undefined, args: string[]): boolean {
  if (cmdName === LAUNCH_COMMAND || cmdName === UPDATE_COMMAND) {
    return true;
  }
  return args.some((arg) => (NO_SYNC_FLAGS as readonly string[]).includes(arg));
}

// Options that consume the next argument as a value
const OPTIONS_WITH_VALUE: ReadonlySet<string> = new Set(['--port', '-p', '--project-path']);

/**
 * Find the first non-option argument that is not a value of a known option.
 */
function findCommandName(args: readonly string[]): string | undefined {
  for (let i = 0; i < args.length; i++) {
    const arg: string = args[i];
    if (arg.startsWith('-')) {
      if (OPTIONS_WITH_VALUE.has(arg)) {
        i++; // skip the next arg (option value)
      }
      continue;
    }
    return arg;
  }
  return undefined;
}

function extractSyncGlobalOptions(args: string[]): GlobalOptions {
  const options: GlobalOptions = {};

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--port' || arg === '-p') {
      const nextArg = args[i + 1];
      if (nextArg !== undefined && !nextArg.startsWith('-')) {
        options.port = nextArg;
      }
      continue;
    }

    if (arg.startsWith('--port=')) {
      options.port = arg.slice('--port='.length);
      continue;
    }

    if (arg === '--project-path') {
      const nextArg = args[i + 1];
      if (nextArg !== undefined && !nextArg.startsWith('-')) {
        options.projectPath = nextArg;
      }
      continue;
    }

    if (arg.startsWith('--project-path=')) {
      options.projectPath = arg.slice('--project-path='.length);
      continue;
    }
  }

  return options;
}

/**
 * Main entry point with auto-sync for unknown commands.
 */
async function main(): Promise<void> {
  if (handleCompletionOptions()) {
    return;
  }

  const program = createProgram();
  const args = process.argv.slice(2);
  const syncGlobalOptions = extractSyncGlobalOptions(args);
  const cmdName = findCommandName(args);

  // No command name = no Unity operation; skip project detection
  const NO_PROJECT_COMMANDS = [UPDATE_COMMAND, 'completion'] as const;
  const skipProjectDetection =
    cmdName === undefined || (NO_PROJECT_COMMANDS as readonly string[]).includes(cmdName);

  if (skipProjectDetection) {
    const defaultToolNames: ReadonlySet<string> = getDefaultToolNames();
    // Only filter disabled tools for top-level help (uloop --help); subcommand help
    // (e.g. uloop completion --help) does not list dynamic tools, so scanning is unnecessary
    const isTopLevelHelp: boolean =
      cmdName === undefined && (args.includes('-h') || args.includes('--help'));
    const shouldFilter: boolean = syncGlobalOptions.projectPath !== undefined || isTopLevelHelp;
    // Use cache to include third-party tools in help output; falls back to defaults when no cache exists
    const sourceTools: ToolDefinition[] = shouldFilter
      ? loadToolsCache(syncGlobalOptions.projectPath).tools
      : getDefaultTools().tools;
    const tools: ToolDefinition[] = shouldFilter
      ? filterEnabledTools(sourceTools, syncGlobalOptions.projectPath)
      : sourceTools;
    if (!shouldFilter || isToolEnabled(FOCUS_WINDOW_COMMAND, syncGlobalOptions.projectPath)) {
      registerFocusWindowCommand(program, HELP_GROUP_BUILTIN_TOOLS);
    }
    for (const tool of tools) {
      registerToolCommand(program, tool, getToolHelpGroup(tool.name, defaultToolNames));
    }
    program.parse();
    return;
  }

  if (!shouldSkipAutoSync(cmdName, args)) {
    // Check if cache version is outdated and auto-sync if needed
    const cachedVersion = loadToolsCache().version;
    if (hasCacheFile() && cachedVersion !== VERSION) {
      console.log(
        `\x1b[33mCache outdated (${cachedVersion} → ${VERSION}). Syncing tools from Unity...\x1b[0m`,
      );
      try {
        await syncTools(syncGlobalOptions);
        console.log('\x1b[32m✓ Tools synced successfully.\x1b[0m\n');
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        if (isConnectionError(message)) {
          console.error('\x1b[33mWarning: Failed to sync tools. Using cached definitions.\x1b[0m');
          console.error("\x1b[33mRun 'uloop sync' manually when Unity is available.\x1b[0m\n");
        } else {
          console.error('\x1b[33mWarning: Failed to sync tools. Using cached definitions.\x1b[0m');
          console.error(`\x1b[33mError: ${message}\x1b[0m`);
          console.error("\x1b[33mRun 'uloop sync' manually when Unity is available.\x1b[0m\n");
        }
      }
    }
  }

  // Register tool commands from cache (after potential auto-sync)
  const toolsCache = loadToolsCache();
  const projectPath: string | undefined = syncGlobalOptions.projectPath;
  const defaultToolNames: ReadonlySet<string> = getDefaultToolNames();
  if (isToolEnabled(FOCUS_WINDOW_COMMAND, projectPath)) {
    registerFocusWindowCommand(program, HELP_GROUP_BUILTIN_TOOLS);
  }
  for (const tool of filterEnabledTools(toolsCache.tools, projectPath)) {
    registerToolCommand(program, tool, getToolHelpGroup(tool.name, defaultToolNames));
  }

  if (cmdName && !commandExists(cmdName, projectPath)) {
    if (!isToolEnabled(cmdName, projectPath)) {
      printToolDisabledError(cmdName);
      process.exit(1);
    }

    console.log(`\x1b[33mUnknown command '${cmdName}'. Syncing tools from Unity...\x1b[0m`);
    try {
      await syncTools(syncGlobalOptions);
      const newCache = loadToolsCache();
      const tool = filterEnabledTools(newCache.tools, projectPath).find((t) => t.name === cmdName);
      if (tool) {
        registerToolCommand(program, tool, getToolHelpGroup(tool.name, defaultToolNames));
        console.log(`\x1b[32m✓ Found '${cmdName}' after sync.\x1b[0m\n`);
      } else {
        console.error(`\x1b[31mError: Command '${cmdName}' not found even after sync.\x1b[0m`);
        process.exit(1);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (isConnectionError(message)) {
        printConnectionError();
      } else {
        console.error(`\x1b[31mError: Failed to sync tools: ${message}\x1b[0m`);
      }
      process.exit(1);
    }
  }

  program.parse();
}

if (process.env.JEST_WORKER_ID === undefined) {
  const args = process.argv.slice(2);
  void (async (): Promise<void> => {
    if (await tryHandleFastExecuteDynamicCodeCommand(args)) {
      return;
    }

    await main();
  })();
}
