import { UnityNotRunningError, UnityServerNotRunningError } from './port-resolver.js';
import { ProjectMismatchError } from './project-validator.js';

export function getProjectResolutionErrorLines(
  error: UnityNotRunningError | UnityServerNotRunningError | ProjectMismatchError,
): string[] {
  if (error instanceof UnityServerNotRunningError) {
    return [
      'Error: Unity Editor is running, but Unity CLI Loop server is not.',
      '',
      `  Project: ${error.projectRoot}`,
      '',
      'If the installed package includes the server state watchdog, wait several seconds for it to recover the server and retry.',
      `If recovery does not complete, restart Unity with: uloop launch -r ${error.projectRoot}`,
    ];
  }

  if (error instanceof UnityNotRunningError) {
    return [
      'Error: Unity Editor for this project is not running.',
      '',
      `  Project: ${error.projectRoot}`,
      '',
      'Start the Unity Editor for this project and try again.',
    ];
  }

  return [
    'Error: Connected Unity instance belongs to a different project.',
    '',
    `  Project:      ${error.expectedProjectRoot}`,
    `  Connected to: ${error.connectedProjectRoot}`,
    '',
    'Another Unity instance was found, but it belongs to a different project.',
    `This can happen when multiple Unity Editors leave a stale server port. Restart the target Unity with: uloop launch -r ${error.expectedProjectRoot}`,
  ];
}
