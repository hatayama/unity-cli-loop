import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';

interface ScriptRunResult {
  code: number | null;
  signal: NodeJS.Signals | null;
  stdout: string;
  stderr: string;
  durationMs: number;
}

function createFakeUloopCommand(tempDir: string): string {
  const uloopPath = join(tempDir, 'uloop');
  writeFileSync(
    uloopPath,
    [
      '#!/bin/sh',
      'if [ "$1" = "get-logs" ]; then',
      '    sleep 10',
      '    exit 1',
      'fi',
      'printf \'{"Success":true}\\n\'',
    ].join('\n'),
    'utf8',
  );
  chmodSync(uloopPath, 0o755);
  return uloopPath;
}

function runStressScript(pathEntries: string[]): Promise<ScriptRunResult> {
  return new Promise((resolvePromise) => {
    const startMs = Date.now();
    const scriptPath = resolve(process.cwd(), '../../../scripts/uloop-compile-get-logs-stress.sh');
    const child = spawn(scriptPath, [], {
      cwd: process.cwd(),
      env: {
        ...process.env,
        PATH: `${pathEntries.join(':')}:${process.env.PATH ?? ''}`,
        ULOOP_STRESS_WAIT_FOR_READY_SECONDS: '1',
        ULOOP_STRESS_INTERVAL_SECONDS: '1',
        ULOOP_STRESS_MAX_ROUNDS: '1',
      },
    });

    let stdout = '';
    let stderr = '';

    child.stdout.on('data', (chunk: Buffer) => {
      stdout += chunk.toString();
    });
    child.stderr.on('data', (chunk: Buffer) => {
      stderr += chunk.toString();
    });

    child.on('error', (error: Error) => {
      stderr += error.message;
      resolvePromise({
        code: null,
        signal: null,
        stdout,
        stderr,
        durationMs: Date.now() - startMs,
      });
    });

    child.on('close', (code, signal) => {
      resolvePromise({
        code,
        signal,
        stdout,
        stderr,
        durationMs: Date.now() - startMs,
      });
    });
  });
}

describe('uloop compile/get-logs stress script', () => {
  let tempDir: string;
  const scriptPath = resolve(process.cwd(), '../../../scripts/uloop-compile-get-logs-stress.sh');

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'uloop-stress-test-'));
    createFakeUloopCommand(tempDir);
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('fails within the configured ready timeout when a readiness probe hangs', async () => {
    const result = await runStressScript([tempDir]);

    expect(result.signal).toBeNull();
    expect(result.code).toBe(1);
    expect(result.stdout).toContain('bootstrap failed');
    expect(result.stdout).toContain('ready timeout after 1s');
    expect(result.durationMs).toBeLessThan(4000);
  });

  it('guards timeout marker creation with a liveness check before killing the child', () => {
    const script = readFileSync(scriptPath, 'utf8');

    expect(script).toMatch(
      /if kill -0 "\$CURRENT_CHILD_PID" 2>\/dev\/null; then\s+: > "\$timeout_marker"\s+kill -TERM "\$CURRENT_CHILD_PID" 2>\/dev\/null \|\| :/m,
    );
  });
});
