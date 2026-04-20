/**
 * CLI command for launching Unity projects.
 * Delegates to launch-unity's orchestrateLaunch() for all orchestration logic.
 */

// CLI commands output to console by design
/* eslint-disable no-console */

import { Command } from 'commander';
import { resolve } from 'path';

import { orchestrateLaunch, type OrchestrateResult } from 'launch-unity';
import { prewarmDynamicCodeAfterLaunch } from '../execute-tool.js';
import {
  waitForDynamicCodeReadyAfterLaunch,
  waitForLaunchReadyAfterLaunch,
} from '../launch-readiness.js';
import { createSpinner } from '../spinner.js';
import { isToolEnabled } from '../tool-settings-loader.js';

interface LaunchCommandOptions {
  restart?: boolean;
  quit?: boolean;
  deleteRecovery?: boolean;
  platform?: string;
  maxDepth?: string;
  addUnityHub?: boolean;
  favorite?: boolean;
}

export function registerLaunchCommand(program: Command): void {
  program
    .command('launch')
    .description(
      'Open a Unity project with the matching Editor version installed by Unity Hub.\n' +
        'Auto-detects project path and Unity version from ProjectSettings/ProjectVersion.txt.\n' +
        "Run 'uloop launch -h' for all options. Details: https://github.com/hatayama/LaunchUnityCommand",
    )
    .argument('[project-path]', 'Path to Unity project')
    .option('-r, --restart', 'Kill running Unity and restart')
    .option('-d, --delete-recovery', 'Delete Assets/_Recovery before launch')
    .option('-q, --quit', 'Gracefully quit running Unity')
    .option('-p, --platform <platform>', 'Build target (e.g., Android, iOS)')
    .option('--max-depth <n>', 'Search depth when project-path is omitted', '3')
    .option('-a, --add-unity-hub', 'Add to Unity Hub (does not launch)')
    .option('-f, --favorite', 'Add to Unity Hub as favorite (does not launch)')
    .action(async (projectPath: string | undefined, options: LaunchCommandOptions) => {
      await runLaunchCommand(projectPath, options);
    });
}

function parseMaxDepth(value: string | undefined): number {
  if (value === undefined) {
    return 3;
  }
  const parsed: number = parseInt(value, 10);
  if (Number.isNaN(parsed)) {
    console.error(`Error: Invalid --max-depth value: "${value}". Must be an integer.`);
    process.exit(1);
  }
  return parsed;
}

async function runLaunchCommand(
  projectPath: string | undefined,
  options: LaunchCommandOptions,
): Promise<void> {
  const maxDepth: number = parseMaxDepth(options.maxDepth);
  const spinner = createSpinner('Launching Unity...');

  try {
    const launchResult: OrchestrateResult = await orchestrateLaunch({
      projectPath: projectPath ? resolve(projectPath) : undefined,
      searchRoot: process.cwd(),
      searchMaxDepth: maxDepth,
      platform: options.platform,
      unityArgs: [],
      restart: options.restart === true,
      quit: options.quit === true,
      deleteRecovery: options.deleteRecovery === true,
      addUnityHub: options.addUnityHub === true,
      favoriteUnityHub: options.favorite === true,
    });

    if (launchResult.action !== 'launched' && launchResult.action !== 'killed-and-launched') {
      return;
    }

    const isDynamicCodeEnabled: boolean = isToolEnabled(
      'execute-dynamic-code',
      launchResult.projectPath,
    );
    if (!isDynamicCodeEnabled) {
      spinner.update('Waiting for Unity to finish starting...');
      await waitForLaunchReadyAfterLaunch(launchResult.projectPath);
      return;
    }

    spinner.update('Waiting for Unity to finish starting...');
    await waitForDynamicCodeReadyAfterLaunch(launchResult.projectPath);
    await prewarmDynamicCodeAfterLaunch({ projectRoot: launchResult.projectPath });
  } finally {
    spinner.stop();
  }
}
