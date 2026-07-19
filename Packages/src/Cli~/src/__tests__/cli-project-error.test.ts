import { getProjectResolutionErrorLines } from '../cli-project-error.js';
import { UnityNotRunningError, UnityServerNotRunningError } from '../port-resolver.js';
import { ProjectMismatchError } from '../project-validator.js';

describe('getProjectResolutionErrorLines', () => {
  it('returns not-running guidance for UnityNotRunningError', () => {
    const lines = getProjectResolutionErrorLines(new UnityNotRunningError('/project/root'));

    expect(lines).toEqual([
      'Error: Unity Editor for this project is not running.',
      '',
      '  Project: /project/root',
      '',
      'Start the Unity Editor for this project and try again.',
    ]);
  });

  it('returns mismatch guidance for ProjectMismatchError', () => {
    const lines = getProjectResolutionErrorLines(
      new ProjectMismatchError('/expected/project', '/connected/project'),
    );

    expect(lines).toEqual([
      'Error: Connected Unity instance belongs to a different project.',
      '',
      '  Project:      /expected/project',
      '  Connected to: /connected/project',
      '',
      'Another Unity instance was found, but it belongs to a different project.',
      'This can happen when multiple Unity Editors leave a stale server port. Restart the target Unity with: uloop launch -r /expected/project',
    ]);
  });

  it('returns server-not-running guidance for UnityServerNotRunningError', () => {
    const lines = getProjectResolutionErrorLines(new UnityServerNotRunningError('/project/root'));

    expect(lines).toEqual([
      'Error: Unity Editor is running, but Unity CLI Loop server is not.',
      '',
      '  Project: /project/root',
      '',
      'If the installed package includes the server state watchdog, wait several seconds for it to recover the server and retry.',
      'If recovery does not complete, restart Unity with: uloop launch -r /project/root',
    ]);
  });
});
