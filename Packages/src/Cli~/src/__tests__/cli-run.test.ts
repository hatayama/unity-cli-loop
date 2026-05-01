jest.mock(
  'launch-unity',
  () => ({
    orchestrateLaunch: jest.fn(),
    findRunningUnityProcess: jest.fn(),
    focusUnityProcess: jest.fn(),
  }),
  { virtual: true },
);

const mockExecuteToolCommand = jest.fn();

jest.mock('../execute-tool.js', () => ({
  executeToolCommand: mockExecuteToolCommand,
  listAvailableTools: jest.fn(),
  syncTools: jest.fn(),
}));

import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'fs';
import { tmpdir } from 'os';
import { join } from 'path';
import { runCli } from '../cli.js';
import { VERSION } from '../version.js';

function createUnityProjectWithToolsCache(): string {
  const projectRoot = mkdtempSync(join(tmpdir(), 'uloop-cli-run-'));
  mkdirSync(join(projectRoot, 'Assets'));
  mkdirSync(join(projectRoot, 'ProjectSettings'));
  mkdirSync(join(projectRoot, '.uloop'));
  writeFileSync(
    join(projectRoot, '.uloop', 'tools.json'),
    JSON.stringify({
      version: VERSION,
      tools: [
        {
          name: 'get-logs',
          description: 'Get Unity Console logs.',
          inputSchema: {
            type: 'object',
            properties: {},
          },
        },
      ],
    }),
  );
  return projectRoot;
}

describe('runCli', () => {
  const originalCwd = process.cwd();
  const createdProjects: string[] = [];

  afterEach(() => {
    process.chdir(originalCwd);
    for (const projectRoot of createdProjects) {
      rmSync(projectRoot, { recursive: true, force: true });
    }
    createdProjects.length = 0;
    mockExecuteToolCommand.mockReset();
  });

  it('waits for async dynamic tool commands before returning', async () => {
    const projectRoot = createUnityProjectWithToolsCache();
    createdProjects.push(projectRoot);
    process.chdir(projectRoot);
    let resolveCommand: (() => void) | undefined;
    mockExecuteToolCommand.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          resolveCommand = resolve;
        }),
    );
    let completed = false;

    const runPromise = runCli(['get-logs']).then(() => {
      completed = true;
    });
    await new Promise((resolve) => setImmediate(resolve));

    expect(mockExecuteToolCommand).toHaveBeenCalledWith('get-logs', {}, {});
    expect(completed).toBe(false);
    expect(resolveCommand).toBeDefined();
    resolveCommand?.();
    await runPromise;
    expect(completed).toBe(true);
  });
});
