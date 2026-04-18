import { mkdirSync, rmSync, writeFileSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';

import { waitForCompileCompletion } from '../../compile/compile-domain-reload-helpers.js';

describe('waitForCompileCompletion', () => {
  function createProjectRoot(): string {
    const projectRoot = join(
      tmpdir(),
      'uloop-typescript-server-compile-helpers-test',
      `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    );
    mkdirSync(join(projectRoot, 'Temp', 'uLoopMCP', 'compile-results'), { recursive: true });
    return projectRoot;
  }

  it('does not let serverstarting.lock block a completed compile result', async () => {
    const projectRoot = createProjectRoot();
    const requestId = 'compile_test_request';
    const resultPath = join(
      projectRoot,
      'Temp',
      'uLoopMCP',
      'compile-results',
      `${requestId}.json`,
    );
    const serverStartingLockPath = join(projectRoot, 'Temp', 'serverstarting.lock');

    try {
      writeFileSync(resultPath, JSON.stringify({ Success: true }));
      writeFileSync(serverStartingLockPath, 'starting');

      const completion = await waitForCompileCompletion<Record<string, unknown>>({
        projectRoot,
        requestId,
        timeoutMs: 2000,
        pollIntervalMs: 10,
        isUnityReadyWhenIdle: (): Promise<boolean> => Promise.resolve(true),
      });

      expect(completion).toEqual({
        outcome: 'completed',
        result: { Success: true },
      });
    } finally {
      rmSync(projectRoot, { recursive: true, force: true });
    }
  });
});
