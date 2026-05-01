/**
 * CLI command for focusing Unity Editor window.
 * Uses OS-level commands via launch-unity library.
 * Works even when Unity is busy (compiling, domain reload).
 */

import { type Command } from 'commander';
import { findRunningUnityProcess, focusUnityProcess } from 'launch-unity';
import { existsSync } from 'fs';
import { resolve } from 'path';
import { getUnityProjectStatus, isUnityProject } from '../project-root.js';

interface FocusWindowCommandOptions {
  projectPath?: string;
}

type FocusWindowOutput = {
  log(message: string): void;
  error(message: string): void;
};

export function registerFocusWindowCommand(program: Command, helpGroup?: string): void {
  const cmd = program
    .command('focus-window')
    .description('Bring Unity Editor window to front using OS-level commands')
    .option('--project-path <path>', 'Unity project path');

  if (helpGroup !== undefined) {
    cmd.helpGroup(helpGroup);
  }

  cmd.action(async (options: FocusWindowCommandOptions) => {
    const exitCode = await runFocusWindowCommand(options);
    if (exitCode !== 0) {
      process.exit(exitCode);
    }
  });
}

export async function runFocusWindowCommand(
  options: FocusWindowCommandOptions,
  output: FocusWindowOutput = console,
): Promise<number> {
  const projectRootResult = resolveFocusProjectRoot(options.projectPath);
  if (projectRootResult.error !== null) {
    writeFocusWindowError(output, projectRootResult.error);
    return 1;
  }

  const runningProcess = await findRunningUnityProcess(projectRootResult.projectRoot);
  if (!runningProcess) {
    writeFocusWindowError(output, 'No running Unity process found for this project');
    return 1;
  }

  try {
    await focusUnityProcess(runningProcess.pid);
    output.log(
      JSON.stringify({
        Success: true,
        Message: `Unity Editor window focused (PID: ${runningProcess.pid})`,
      }),
    );
    return 0;
  } catch (error) {
    writeFocusWindowError(
      output,
      `Failed to focus Unity window: ${error instanceof Error ? error.message : String(error)}`,
    );
    return 1;
  }
}

type FocusProjectRootResult =
  | { projectRoot: string; error: null }
  | { projectRoot: null; error: string };

function resolveFocusProjectRoot(projectPath: string | undefined): FocusProjectRootResult {
  if (projectPath !== undefined) {
    const resolvedProjectPath = resolve(projectPath);
    // User-provided project paths must be validated before matching a running Unity process.
    // eslint-disable-next-line security/detect-non-literal-fs-filename
    if (!existsSync(resolvedProjectPath)) {
      return { projectRoot: null, error: `Path does not exist: ${resolvedProjectPath}` };
    }

    if (!isUnityProject(resolvedProjectPath)) {
      return {
        projectRoot: null,
        error: `Not a Unity project (Assets/ or ProjectSettings/ not found): ${resolvedProjectPath}`,
      };
    }

    return { projectRoot: resolvedProjectPath, error: null };
  }

  const projectStatus = getUnityProjectStatus();
  if (!projectStatus.found || projectStatus.path === null) {
    return { projectRoot: null, error: 'Unity project not found' };
  }

  return { projectRoot: projectStatus.path, error: null };
}

function writeFocusWindowError(output: FocusWindowOutput, message: string): void {
  output.error(
    JSON.stringify({
      Success: false,
      Message: message,
    }),
  );
}
