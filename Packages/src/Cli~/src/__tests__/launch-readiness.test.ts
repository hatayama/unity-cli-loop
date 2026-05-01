import { DirectUnityClient } from '../direct-unity-client.js';
import {
  waitForDynamicCodeReadyAfterLaunch,
  waitForLaunchReadyAfterLaunch,
} from '../launch-readiness.js';
import { createProjectIpcEndpoint } from '../ipc-endpoint.js';
import { type ResolvedUnityConnection } from '../port-resolver.js';

interface MockReadinessResponse {
  Success?: boolean;
  ErrorMessage?: string;
  Timings?: string[];
  DataPath?: string;
  Message?: string;
}

const EXPECTED_STABLE_LAUNCH_READINESS_CODE =
  'UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }';
const EXPECTED_USER_LIKE_LAUNCH_READINESS_CODE =
  'using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { Debug.unityLogger.filterLogType = previous; }';

function createMockClient(
  responses: Array<MockReadinessResponse | Error>,
  recordedMethods: string[],
  recordedParams?: Array<Record<string, unknown>>,
): { client: DirectUnityClient; disconnectSpy: jest.Mock } {
  const disconnectSpy = jest.fn();

  return {
    disconnectSpy,
    client: {
      connect: jest.fn().mockImplementation((): Promise<void> => Promise.resolve()),
      disconnect: disconnectSpy,
      isConnected: jest.fn().mockReturnValue(true),
      sendRequest: jest
        .fn()
        .mockImplementation((method: string, params?: Record<string, unknown>) => {
          recordedMethods.push(method);
          recordedParams?.push(params ?? {});
          const next = responses.shift();
          if (next instanceof Error) {
            return Promise.reject(next);
          }

          return Promise.resolve(next);
        }),
    } as unknown as DirectUnityClient,
  };
}

function createConnection(
  endpointId: number,
  overrides?: Partial<ResolvedUnityConnection>,
): ResolvedUnityConnection {
  return {
    endpoint: createProjectIpcEndpoint(`/project-${endpointId}`),
    projectRoot: '/project',
    requestMetadata: {
      expectedProjectRoot: '/project',
      expectedServerSessionId: 'session-1',
    },
    shouldValidateProject: false,
    ...overrides,
  };
}

describe('waitForDynamicCodeReadyAfterLaunch', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('retries transient execute-dynamic-code failures until success', async () => {
    const recordedMethods: string[] = [];
    const recordedParams: Array<Record<string, unknown>> = [];
    const createdClients: DirectUnityClient[] = [];
    const disconnectSpies: jest.Mock[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      { Success: false, ErrorMessage: 'COMPILATION_PROVIDER_UNAVAILABLE: warming up' },
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => {
        const mockClient = createMockClient(responses, recordedMethods, recordedParams);
        createdClients.push(mockClient.client);
        disconnectSpies.push(mockClient.disconnectSpy);
        return mockClient.client;
      },
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(recordedParams[0]).toMatchObject({
      CompileOnly: false,
      YieldToForegroundRequests: true,
    });
    expect(recordedParams[0]['Code']).toBe(EXPECTED_STABLE_LAUNCH_READINESS_CODE);
    expect(recordedParams[4]['Code']).toBe(EXPECTED_USER_LIKE_LAUNCH_READINESS_CODE);
    expect(sleepCount).toBe(4);
    expect(createdClients).toHaveLength(5);
    expect(disconnectSpies[0]).toHaveBeenCalled();
    expect(disconnectSpies[1]).toHaveBeenCalled();
    expect(disconnectSpies[2]).toHaveBeenCalled();
    expect(disconnectSpies[3]).toHaveBeenCalled();
    expect(disconnectSpies[4]).toHaveBeenCalled();
  });

  it('rethrows non-transient readiness failures', async () => {
    const recordedMethods: string[] = [];

    await expect(
      waitForDynamicCodeReadyAfterLaunch('/project', {
        resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
        createClient: () =>
          createMockClient([{ Success: false, ErrorMessage: 'Syntax error' }], recordedMethods)
            .client,
        sleepFn: jest.fn(),
        nowFn: () => 0,
      }),
    ).rejects.toThrow('execute-dynamic-code launch readiness probe failed: Syntax error');
  });

  it('does not retry permanent compilation provider unavailability', async () => {
    const recordedMethods: string[] = [];

    await expect(
      waitForDynamicCodeReadyAfterLaunch('/project', {
        resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
        createClient: () =>
          createMockClient(
            [
              {
                Success: false,
                ErrorMessage:
                  'COMPILATION_PROVIDER_UNAVAILABLE: No compilation provider is registered. Check initialization.',
              },
            ],
            recordedMethods,
          ).client,
        sleepFn: jest.fn(),
        nowFn: () => 0,
      }),
    ).rejects.toThrow(
      'execute-dynamic-code launch readiness probe failed: COMPILATION_PROVIDER_UNAVAILABLE: No compilation provider is registered. Check initialization.',
    );

    expect(recordedMethods).toEqual(['execute-dynamic-code']);
  });

  it('retries Unity JSON-RPC errors raised during startup until success', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      new Error('Unity error: Internal error (can only be called from the main thread.)'),
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('retries fast project validation session changes until success', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      new Error(
        'Unity error: Invalid params: Unity CLI Loop server session changed. Retry the command.',
      ),
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('falls back to legacy project validation when fast session metadata is missing', async () => {
    const recordedMethods: string[] = [];
    const projectRoot = process.cwd();
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        )
        .mockResolvedValue(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        ),
      createClient: () =>
        createMockClient(
          [
            { DataPath: `${projectRoot}/Assets` },
            { Success: true },
            { DataPath: `${projectRoot}/Assets` },
            { Success: true },
            { DataPath: `${projectRoot}/Assets` },
            { Success: true },
            { DataPath: `${projectRoot}/Assets` },
            { Success: true },
          ],
          recordedMethods,
        ).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(3);
  });

  it('retries legacy project mismatches until the launched project is selected', async () => {
    const recordedMethods: string[] = [];
    const projectRoot = process.cwd();
    const mismatchedProjectRoot = `${projectRoot}/..`;
    const responses: Array<MockReadinessResponse | Error> = [
      { DataPath: `${mismatchedProjectRoot}/Assets` },
      { DataPath: `${projectRoot}/Assets` },
      { Success: true },
      { DataPath: `${projectRoot}/Assets` },
      { Success: true },
      { DataPath: `${projectRoot}/Assets` },
      { Success: true },
      { DataPath: `${projectRoot}/Assets` },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        )
        .mockResolvedValue(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        ),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'get-version',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('restarts probe progress when the resolved session changes', async () => {
    const recordedMethods: string[] = [];
    const recordedParams: Array<Record<string, unknown>> = [];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(createConnection(8711))
        .mockResolvedValueOnce(
          createConnection(8712, {
            requestMetadata: {
              expectedProjectRoot: '/project',
              expectedServerSessionId: 'session-2',
            },
          }),
        )
        .mockResolvedValue(
          createConnection(8712, {
            requestMetadata: {
              expectedProjectRoot: '/project',
              expectedServerSessionId: 'session-2',
            },
          }),
        ),
      createClient: () => {
        const nextResponse = recordedMethods.length < 5 ? { Success: true } : { Success: true };
        return createMockClient([nextResponse], recordedMethods, recordedParams).client;
      },
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(recordedParams[0]['Code']).toBe(EXPECTED_STABLE_LAUNCH_READINESS_CODE);
    expect(recordedParams[1]['Code']).toBe(EXPECTED_STABLE_LAUNCH_READINESS_CODE);
    expect(recordedParams[4]['Code']).toBe(EXPECTED_USER_LIKE_LAUNCH_READINESS_CODE);
    expect(sleepCount).toBe(4);
  });

  it('restarts probe progress when fast session metadata disappears', async () => {
    const recordedMethods: string[] = [];
    const recordedParams: Array<Record<string, unknown>> = [];
    const projectRoot = process.cwd();
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(createConnection(8711))
        .mockResolvedValueOnce(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        )
        .mockResolvedValue(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        ),
      createClient: () => {
        const legacyValidationResponse =
          recordedMethods.length >= 1 && recordedMethods.length % 2 === 1
            ? [{ DataPath: `${projectRoot}/Assets` }, { Success: true }]
            : [{ Success: true }];
        return createMockClient(legacyValidationResponse, recordedMethods, recordedParams).client;
      },
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
      'get-version',
      'execute-dynamic-code',
    ]);
    expect(recordedParams[0]['Code']).toBe(EXPECTED_STABLE_LAUNCH_READINESS_CODE);
    expect(recordedParams[1]).toEqual({});
    expect(recordedParams[2]['Code']).toBe(EXPECTED_STABLE_LAUNCH_READINESS_CODE);
    expect(recordedParams[8]['Code']).toBe(EXPECTED_USER_LIKE_LAUNCH_READINESS_CODE);
    expect(sleepCount).toBe(4);
  });

  it('does not retry non-transient Unity JSON-RPC errors', async () => {
    const recordedMethods: string[] = [];

    await expect(
      waitForDynamicCodeReadyAfterLaunch('/project', {
        resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
        createClient: () =>
          createMockClient(
            [new Error('Unity error: Internal error (Object reference not set to an instance)')],
            recordedMethods,
          ).client,
        sleepFn: jest.fn(),
        nowFn: () => 0,
      }),
    ).rejects.toThrow('Unity error: Internal error (Object reference not set to an instance)');

    expect(recordedMethods).toEqual(['execute-dynamic-code']);
  });

  it('retries indeterminate payloads until success', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      {},
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('retries undefined payloads until success', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error | undefined> = [
      undefined,
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () =>
        createMockClient(responses as Array<MockReadinessResponse | Error>, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('resets probe progress after a malformed payload interrupts a successful streak', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error | undefined> = [
      { Success: true },
      undefined,
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () =>
        createMockClient(responses as Array<MockReadinessResponse | Error>, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(5);
  });

  it('resets probe progress after transient failures interrupt a successful streak', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      { Success: true },
      { Success: false, ErrorMessage: 'COMPILATION_PROVIDER_UNAVAILABLE: warming up' },
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(5);
  });

  it('keeps retrying malformed payloads until a later successful probe arrives', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error | undefined> = [
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await expect(
      waitForDynamicCodeReadyAfterLaunch('/project', {
        resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
        createClient: () => {
          return createMockClient(
            responses as Array<MockReadinessResponse | Error>,
            recordedMethods,
          ).client;
        },
        sleepFn: jest.fn().mockImplementation((): Promise<void> => {
          sleepCount++;
          return Promise.resolve();
        }),
        nowFn: (() => {
          let now = 0;
          return (): number => {
            now += 100;
            return now;
          };
        })(),
      }),
    ).resolves.toEqual(createConnection(8711));

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(8);
  });

  it('retries payload errors that indicate Unity startup is still on the loading thread', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      {
        Success: false,
        ErrorMessage:
          'An unexpected error occurred during execution UnityEngine.UnityException: get_activeScriptCompilationDefines can only be called from the main thread.',
      },
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('retries startup internal errors until later probes succeed', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      { Success: false, ErrorMessage: 'Internal error' },
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('retries startup pre-using resolver null references until later probes succeed', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      {
        Success: false,
        ErrorMessage:
          'An unexpected error occurred during execution System.NullReferenceException: Object reference not set to an instance of an object at io.github.hatayama.uLoopMCP.PreUsingResolver.Resolve',
      },
      { Success: true },
      { Success: true },
      { Success: true },
      { Success: true },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('retries successful probes until RequestTotal settles below threshold', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      {
        Success: true,
        Timings: ['[Perf] RequestTotal: 420.0ms'],
      },
      {
        Success: true,
        Timings: ['[Perf] RequestTotal: 180.0ms'],
      },
      {
        Success: true,
        Timings: ['[Perf] RequestTotal: 170.0ms'],
      },
      {
        Success: true,
        Timings: ['[Perf] RequestTotal: 165.0ms'],
      },
      {
        Success: true,
        Timings: ['[Perf] RequestTotal: 160.0ms'],
      },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(4);
  });

  it('accepts successful probes when RequestTotal timing is missing', async () => {
    const recordedMethods: string[] = [];

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () =>
        createMockClient(
          [
            { Success: true, Timings: ['[Perf] Build: 50.0ms'] },
            { Success: true, Timings: ['[Perf] Build: 55.0ms'] },
            { Success: true, Timings: ['[Perf] Build: 60.0ms'] },
            { Success: true, Timings: ['[Perf] Build: 65.0ms'] },
          ],
          recordedMethods,
        ).client,
      sleepFn: jest.fn().mockResolvedValue(undefined),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
  });

  it('returns after settle timeout even when RequestTotal stays above threshold', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      { Success: true, Timings: ['[Perf] RequestTotal: 420.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 410.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 405.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 400.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 395.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 390.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 385.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 380.0ms'] },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        const values = [
          0, 100, 1100, 2100, 11100, 11200, 21300, 21400, 31500, 31600, 41700, 41800, 51900, 52000,
          62100, 62200, 72300, 72400,
        ];
        let index = 0;
        return (): number => values[Math.min(index++, values.length - 1)];
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(7);
  });

  it('waits for busy lock files to clear after the final successful probe', async () => {
    const recordedMethods: string[] = [];
    const recordedParams: Array<Record<string, unknown>> = [];
    const isProjectBusyFn = jest
      .fn<boolean, [string]>()
      .mockReturnValueOnce(true)
      .mockReturnValueOnce(false);
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () =>
        createMockClient(
          [{ Success: true }, { Success: true }, { Success: true }, { Success: true }],
          recordedMethods,
          recordedParams,
        ).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
      isProjectBusyFn,
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(recordedParams[3]['Code']).toBe(EXPECTED_USER_LIKE_LAUNCH_READINESS_CODE);
    expect(isProjectBusyFn).toHaveBeenCalledTimes(2);
    expect(sleepCount).toBe(4);
  });

  it('resets the settle timer after transient payload failures', async () => {
    const recordedMethods: string[] = [];
    const responses: Array<MockReadinessResponse | Error> = [
      { Success: true, Timings: ['[Perf] RequestTotal: 420.0ms'] },
      { Success: false, ErrorMessage: 'Internal error' },
      { Success: true, Timings: ['[Perf] RequestTotal: 410.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 405.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 180.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 170.0ms'] },
      { Success: true, Timings: ['[Perf] RequestTotal: 160.0ms'] },
    ];
    let sleepCount = 0;

    await waitForDynamicCodeReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        const values = [0, 100, 200, 1200, 11500, 11600, 22000, 22100, 22300, 22500, 22700];
        let index = 0;
        return (): number => values[Math.min(index++, values.length - 1)];
      })(),
    });

    expect(recordedMethods).toEqual([
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
      'execute-dynamic-code',
    ]);
    expect(sleepCount).toBe(6);
  });
});

describe('waitForLaunchReadyAfterLaunch', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('waits for busy lock files to clear without probing execute-dynamic-code', async () => {
    const recordedMethods: string[] = [];
    const isProjectBusyFn = jest
      .fn<boolean, [string]>()
      .mockReturnValueOnce(true)
      .mockReturnValueOnce(false);
    let sleepCount = 0;

    await waitForLaunchReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () =>
        createMockClient([{ Message: 'ok' }, { Message: 'ok' }], recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
      isProjectBusyFn,
    });

    expect(recordedMethods).toEqual(['ping', 'ping']);
    expect(isProjectBusyFn).toHaveBeenCalledTimes(2);
    expect(sleepCount).toBe(1);
  });

  it('falls back to legacy project validation when launch readiness metadata is missing', async () => {
    const recordedMethods: string[] = [];
    const projectRoot = process.cwd();

    await waitForLaunchReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        )
        .mockResolvedValue(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        ),
      createClient: () =>
        createMockClient([{ DataPath: `${projectRoot}/Assets` }], recordedMethods).client,
      sleepFn: jest.fn(),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
      isProjectBusyFn: jest.fn().mockReturnValue(false),
    });

    expect(recordedMethods).toEqual(['get-version']);
  });

  it('retries legacy project mismatches until launch readiness finds the launched project', async () => {
    const recordedMethods: string[] = [];
    const projectRoot = process.cwd();
    const mismatchedProjectRoot = `${projectRoot}/..`;
    const responses: Array<MockReadinessResponse | Error> = [
      { DataPath: `${mismatchedProjectRoot}/Assets` },
      { DataPath: `${projectRoot}/Assets` },
    ];
    let sleepCount = 0;

    await waitForLaunchReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest
        .fn()
        .mockResolvedValueOnce(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        )
        .mockResolvedValue(
          createConnection(8711, {
            requestMetadata: null,
            shouldValidateProject: true,
            projectRoot,
          }),
        ),
      createClient: () => createMockClient(responses, recordedMethods).client,
      sleepFn: jest.fn().mockImplementation((): Promise<void> => {
        sleepCount++;
        return Promise.resolve();
      }),
      nowFn: (() => {
        let now = 0;
        return (): number => {
          now += 100;
          return now;
        };
      })(),
      isProjectBusyFn: jest.fn().mockReturnValue(false),
    });

    expect(recordedMethods).toEqual(['get-version', 'get-version']);
    expect(sleepCount).toBe(1);
  });

  it('validates launch identity with ping when fast session metadata is available', async () => {
    const recordedMethods: string[] = [];

    await waitForLaunchReadyAfterLaunch('/project', {
      resolveUnityConnectionFn: jest.fn().mockResolvedValue(createConnection(8711)),
      createClient: () => createMockClient([{ Message: 'ok' }], recordedMethods).client,
      sleepFn: jest.fn(),
      nowFn: () => 0,
      isProjectBusyFn: jest.fn().mockReturnValue(false),
    });

    expect(recordedMethods).toEqual(['ping']);
  });
});
