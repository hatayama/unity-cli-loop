import { BaseTool } from './base-tool.js';
import { ToolContext, ToolResponse } from '../types/tool-types.js';
import { PARAMETER_SCHEMA } from '../constants.js';
import {
  COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS,
  ensureCompileRequestId,
  getCompileBooleanArg,
  waitForCompileCompletion,
} from '../compile/compile-domain-reload-helpers.js';
import {
  isDynamicCodeWarmupEnabledForProject,
  prewarmDynamicCodeAfterCompile,
  shouldPrewarmDynamicCodeAfterCompile,
} from './dynamic-code-post-compile-warmup.js';
import { resolveCompileTargetProjectRoot } from './compile-target-project-root.js';
import { VibeLogger } from '../utils/vibe-logger.js';

// Type definitions for Unity parameter schema
interface UnityParameterInfo {
  [key: string]: unknown;
}

interface UnityParameterSchema {
  [key: string]: unknown;
}

interface JsonSchemaProperty {
  type: string;
  description?: string;
  default?: unknown;
  enum?: string[];
  items?: { type: string };
}

interface InputSchema {
  type: string;
  properties: Record<string, JsonSchemaProperty>;
  required?: string[];
  additionalProperties?: boolean;
}

interface CompileExecutionContext {
  shouldWaitForDomainReload: boolean;
  requestId?: string;
}

/**
 * Dynamically generated tool for Unity tools
 *
 * Design document reference: Packages/src/TypeScriptServer~/ARCHITECTURE.md
 *
 * Related classes:
 * - UnityMcpServer: Instantiates and uses this tool
 * - UnityClient: Used to execute the actual tool in Unity
 * - BaseTool: Base class providing common tool functionality
 */
export class DynamicUnityCommandTool extends BaseTool {
  private static readonly COMPILE_WAIT_TIMEOUT_MS = 90000;
  private static readonly COMPILE_WAIT_POLL_INTERVAL_MS = 100;

  public readonly name: string;
  public readonly description: string;
  public readonly inputSchema: InputSchema;
  private readonly toolName: string;

  constructor(
    context: ToolContext,
    toolName: string,
    description: string,
    parameterSchema?: UnityParameterSchema,
  ) {
    super(context);
    this.toolName = toolName;
    this.name = toolName;
    this.description = description;
    this.inputSchema = this.generateInputSchema(parameterSchema);
  }

  private generateInputSchema(parameterSchema?: UnityParameterSchema): InputSchema {
    if (this.hasNoParameters(parameterSchema)) {
      // For tools without parameters, return minimal schema without dummy parameters
      return {
        type: 'object',
        properties: {},
        additionalProperties: false,
      };
    }

    const properties: Record<string, JsonSchemaProperty> = {};
    const required: string[] = [];

    // Convert Unity parameter schema to JSON Schema format using constants
    if (!parameterSchema) {
      throw new Error('Parameter schema is undefined');
    }
    const propertiesObj = parameterSchema[PARAMETER_SCHEMA.PROPERTIES_PROPERTY] as Record<
      string,
      UnityParameterInfo
    >;
    for (const [propName, propInfo] of Object.entries(propertiesObj)) {
      const info = propInfo;

      const typeValue = info[PARAMETER_SCHEMA.TYPE_PROPERTY];
      const descriptionValue = info[PARAMETER_SCHEMA.DESCRIPTION_PROPERTY];

      const property: JsonSchemaProperty = {
        type: this.convertType(typeof typeValue === 'string' ? typeValue : 'string'),
        description:
          typeof descriptionValue === 'string' ? descriptionValue : `Parameter: ${propName}`,
      };

      // Add default value if provided
      const defaultValue = info[PARAMETER_SCHEMA.DEFAULT_VALUE_PROPERTY];
      if (defaultValue !== undefined && defaultValue !== null) {
        property.default = defaultValue;
      }

      // Add enum values if provided using constants
      const enumValues = info[PARAMETER_SCHEMA.ENUM_PROPERTY];
      if (enumValues && Array.isArray(enumValues) && enumValues.length > 0) {
        property.enum = enumValues as string[];
      }

      // Handle array type
      if (
        info[PARAMETER_SCHEMA.TYPE_PROPERTY] === 'array' &&
        defaultValue &&
        Array.isArray(defaultValue)
      ) {
        property.items = {
          type: 'string',
        };
        property.default = defaultValue;
      }

      // SECURITY: propName is validated input from Unity's tool schema - safe for object property assignment
      Object.defineProperty(properties, propName, {
        value: property,
        writable: true,
        enumerable: true,
        configurable: true,
      });
    }

    // Add required parameters using constants
    if (!parameterSchema) {
      throw new Error('Parameter schema is undefined');
    }
    const requiredParams = parameterSchema[PARAMETER_SCHEMA.REQUIRED_PROPERTY];
    if (requiredParams && Array.isArray(requiredParams)) {
      required.push(...(requiredParams as string[]));
    }

    const schema = {
      type: 'object',
      properties: properties,
      required: required.length > 0 ? required : undefined,
    };

    return schema;
  }

  private convertType(unityType: string): string {
    switch (unityType?.toLowerCase()) {
      case 'string':
        return 'string';
      case 'number':
      case 'int':
      case 'float':
      case 'double':
        return 'number';
      case 'boolean':
      case 'bool':
        return 'boolean';
      case 'array':
        return 'array';
      default:
        return 'string'; // Default fallback
    }
  }

  private hasNoParameters(parameterSchema?: UnityParameterSchema): boolean {
    if (!parameterSchema) {
      return true;
    }

    const properties = parameterSchema[PARAMETER_SCHEMA.PROPERTIES_PROPERTY];
    if (!properties || typeof properties !== 'object') {
      return true;
    }

    return Object.keys(properties).length === 0;
  }

  validateArgs(args: unknown): Record<string, unknown> {
    // If no real parameters are defined, return empty object
    if (!this.inputSchema.properties || Object.keys(this.inputSchema.properties).length === 0) {
      return {};
    }

    return (args as Record<string, unknown>) || {};
  }

  async execute(args: Record<string, unknown>): Promise<ToolResponse> {
    try {
      // Validate and use the provided arguments
      const actualArgs: Record<string, unknown> = this.validateArgs(args);
      const compileContext = this.prepareCompileExecutionContext(actualArgs);

      // Record connection state before executeTool() to distinguish:
      //  - wasConnected=true  → sendRequest() was called → recovery possible
      //  - wasConnected=false → guidance string returned without dispatch → no recovery
      const wasConnectedBeforeCall: boolean = this.context.unityClient.connected;

      let immediateResult: unknown;
      let executionError: unknown = undefined;
      try {
        immediateResult = await this.context.unityClient.executeTool(this.toolName, actualArgs);
      } catch (error) {
        immediateResult = undefined;
        executionError = error;
      }

      // executeTool() uses throw vs return as a design contract (unity-client.ts:452-464):
      //  - throw: permanent disconnection (never connected, editor quit)
      //    → request never reached Unity → fail fast
      //  - return: temporary state or successful response → recovery is possible
      // When TCP drops *after* sendRequest(), clearPendingRequestsWithSuccess()
      // (message-handler.ts:259,278) resolves the pending promise with a guidance string,
      // so executeTool() returns normally — it does NOT throw.
      // Therefore, executionError !== undefined reliably indicates "request never dispatched".
      if (executionError !== undefined) {
        throw this.toError(executionError);
      }

      if (!compileContext.shouldWaitForDomainReload) {
        return {
          content: [
            {
              type: 'text',
              text:
                typeof immediateResult === 'string'
                  ? immediateResult
                  : JSON.stringify(immediateResult, null, 2),
            },
          ],
          isError: this.isUnityFailureResult(immediateResult),
        };
      }

      // executeTool() returned without throwing, but no request was actually dispatched.
      // This happens when Unity is temporarily disconnected (!connected && hasEverConnected):
      // executeTool() returns a guidance string without calling sendRequest().
      // Waiting for a result file that will never be created would block for 90 seconds.
      if (!wasConnectedBeforeCall && typeof immediateResult === 'string') {
        VibeLogger.logWarning(
          'compile_not_dispatched',
          'Compile request was not dispatched because Unity was disconnected before executeTool()',
          { toolName: this.toolName },
        );
        return {
          content: [
            {
              type: 'text',
              text: 'Unity is currently compiling or reloading. Please retry after the operation completes.',
            },
          ],
          isError: true,
        };
      }

      // Both cases below are safe to proceed with file-based recovery:
      //  - compile result object (normal response): file exists, will be found immediately
      //  - guidance string (TCP drop after sendRequest): file may appear after domain reload
      const projectRootFromUnity: string | undefined = this.extractProjectRoot(immediateResult);
      const storedResult = await this.waitForStoredCompileResult(
        compileContext.requestId ?? '',
        projectRootFromUnity,
      );
      const finalResult = storedResult ?? immediateResult;

      if (finalResult === undefined) {
        throw new Error(
          'Compile result was unavailable after domain reload. Run compile again or use get-logs.',
        );
      }

      const compileTargetProjectRoot: string = resolveCompileTargetProjectRoot(
        finalResult,
        projectRootFromUnity,
        this.resolveProjectRoot(),
      );

      // Why: compile completion means the result file and reload locks settled, but it does not
      // guarantee that execute-dynamic-code has finished its own post-reload cold path yet.
      if (
        shouldPrewarmDynamicCodeAfterCompile(finalResult) &&
        isDynamicCodeWarmupEnabledForProject(compileTargetProjectRoot)
      ) {
        await prewarmDynamicCodeAfterCompile(this.context.unityClient);
      }

      const cleanedResult = this.stripInternalFields(finalResult);

      return {
        content: [
          {
            type: 'text',
            text:
              typeof cleanedResult === 'string'
                ? cleanedResult
                : JSON.stringify(cleanedResult, null, 2),
          },
        ],
        // Treat Unity-side failure responses as MCP tool errors.
        // Otherwise tools that return { Success: false, ... } appear as "success" in MCP Inspector.
        isError: this.isUnityFailureResult(cleanedResult),
      };
    } catch (error) {
      return this.formatErrorResponse(error);
    }
  }

  private prepareCompileExecutionContext(
    actualArgs: Record<string, unknown>,
  ): CompileExecutionContext {
    if (this.toolName !== 'compile') {
      return { shouldWaitForDomainReload: false };
    }

    const waitForDomainReload = getCompileBooleanArg(
      actualArgs,
      COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS,
    );
    if (!waitForDomainReload) {
      return { shouldWaitForDomainReload: false };
    }

    const requestId = ensureCompileRequestId(actualArgs);
    return {
      shouldWaitForDomainReload: true,
      requestId,
    };
  }

  private async waitForStoredCompileResult(
    requestId: string,
    projectRootFromUnity: string | undefined,
  ): Promise<unknown> {
    const projectRoot = projectRootFromUnity ?? this.resolveProjectRoot();

    const { outcome, result } = await waitForCompileCompletion<unknown>({
      projectRoot,
      requestId,
      timeoutMs: DynamicUnityCommandTool.COMPILE_WAIT_TIMEOUT_MS,
      pollIntervalMs: DynamicUnityCommandTool.COMPILE_WAIT_POLL_INTERVAL_MS,
      isUnityReadyWhenIdle: () => {
        const isConnected: boolean = this.context.unityClient.connected;
        return Promise.resolve(isConnected);
      },
    });

    if (outcome === 'timed_out') {
      throw new Error(
        `Compile wait timed out after ${DynamicUnityCommandTool.COMPILE_WAIT_TIMEOUT_MS}ms for request '${requestId}'.`,
      );
    }

    return result;
  }

  private stripInternalFields(result: unknown): unknown {
    if (typeof result !== 'object' || result === null) {
      return result;
    }

    const obj = result as Record<string, unknown>;
    if (!('ProjectRoot' in obj)) {
      return result;
    }

    const cleaned = { ...obj };
    delete cleaned['ProjectRoot'];
    return cleaned;
  }

  private extractProjectRoot(result: unknown): string | undefined {
    if (typeof result !== 'object' || result === null) {
      return undefined;
    }

    const obj = result as Record<string, unknown>;
    const projectRoot = obj['ProjectRoot'];
    if (typeof projectRoot === 'string' && projectRoot.length > 0) {
      return projectRoot;
    }

    return undefined;
  }

  private resolveProjectRoot(): string {
    const logger = VibeLogger as unknown as { getProjectRoot?: () => string };
    if (typeof logger.getProjectRoot === 'function') {
      return logger.getProjectRoot();
    }

    return process.cwd();
  }

  private toError(error: unknown): Error {
    if (error instanceof Error) {
      return error;
    }

    if (typeof error === 'string') {
      return new Error(error);
    }

    return new Error('Unknown error');
  }

  private isUnityFailureResult(result: unknown): boolean {
    if (typeof result !== 'object' || result === null) {
      return false;
    }

    const obj = result as Record<string, unknown>;

    // Convention across Unity tools: Success=false means tool-level failure.
    if (obj.Success === false) {
      return true;
    }

    return false;
  }

  protected formatErrorResponse(error: unknown): ToolResponse {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error';
    return {
      content: [
        {
          type: 'text',
          text: errorMessage,
        },
      ],
      isError: true,
    };
  }
}
