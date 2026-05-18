/**
 * Execute Tool UseCase
 *
 * Related classes:
 * - IConnectionService (application/interfaces/connection-service.ts)
 * - IToolQueryService (application/interfaces/tool-query-service.ts)
 */

import { UseCase } from '../base-interfaces.js';
import { ExecuteToolRequest } from '../models/requests.js';
import { ExecuteToolResponse } from '../models/responses.js';
import { IConnectionService } from '../../application/interfaces/connection-service.js';
import { IToolQueryService } from '../../application/interfaces/tool-query-service.js';
import { ConnectionError, ToolExecutionError } from '../errors.js';
import { VibeLogger } from '../../utils/vibe-logger.js';
import { DynamicUnityCommandTool } from '../../tools/dynamic-unity-command-tool.js';

/**
 * UseCase for executing Unity tools
 *
 * Responsibilities:
 * - Orchestrate the complete tool execution workflow
 * - Manage temporal cohesion of tool execution process
 * - Handle business-level error scenarios
 * - Coordinate multiple application services
 *
 * Workflow:
 * 1. Validate connection state
 * 2. Verify tool existence
 * 3. Execute the tool
 * 4. Handle errors and return formatted response
 */
export class ExecuteToolUseCase implements UseCase<ExecuteToolRequest, ExecuteToolResponse> {
  private connectionService: IConnectionService;
  private toolService: IToolQueryService;

  constructor(connectionService: IConnectionService, toolService: IToolQueryService) {
    this.connectionService = connectionService;
    this.toolService = toolService;
  }

  /**
   * Execute the tool execution workflow
   *
   * @param request Tool execution request
   * @returns Tool execution response
   */
  async execute(request: ExecuteToolRequest): Promise<ExecuteToolResponse> {
    const { toolName, arguments: args } = request;
    const correlationId = VibeLogger.generateCorrelationId();

    VibeLogger.logInfo(
      'execute_tool_use_case_start',
      'Starting tool execution workflow',
      { tool_name: toolName, has_arguments: Object.keys(args).length > 0 },
      correlationId,
      'UseCase orchestrating tool execution workflow',
    );

    try {
      // Step 1: Ensure Unity connection is established
      await this.ensureUnityConnection(correlationId);

      // Step 2: Verify tool exists and get tool instance
      const dynamicTool = this.validateAndGetTool(toolName, correlationId);

      // Step 3: Execute the tool
      const result = await this.executeTool(dynamicTool, args || {}, correlationId);

      VibeLogger.logInfo(
        'execute_tool_use_case_success',
        'Tool execution workflow completed successfully',
        { tool_name: toolName, has_error: result.isError || false },
        correlationId,
        'Tool executed successfully through UseCase workflow',
      );

      return result;
    } catch (error) {
      return this.handleExecutionError(error, toolName, correlationId);
    }
  }

  /**
   * Ensure Unity connection is established
   *
   * @param correlationId Correlation ID for logging
   * @throws ConnectionError if connection cannot be established
   */
  private async ensureUnityConnection(correlationId: string): Promise<void> {
    if (!this.connectionService.isConnected()) {
      VibeLogger.logWarning(
        'execute_tool_unity_not_connected',
        'Unity not connected, attempting to establish connection',
        { connected: false },
        correlationId,
        'Unity connection required for tool execution',
      );

      try {
        await this.connectionService.ensureConnected();
      } catch (error) {
        throw new ConnectionError(
          `Cannot execute tool: Unity connection failed - ${error instanceof Error ? error.message : 'Unknown error'}`,
          { original_error: error },
        );
      }
    }
  }

  /**
   * Validate tool exists and retrieve tool instance
   *
   * @param toolName Tool name to validate
   * @param correlationId Correlation ID for logging
   * @returns Dynamic tool instance
   * @throws ToolExecutionError if tool doesn't exist
   */
  private validateAndGetTool(toolName: string, correlationId: string): DynamicUnityCommandTool {
    if (!this.toolService.hasTool(toolName)) {
      VibeLogger.logError(
        'execute_tool_not_found',
        'Requested tool does not exist',
        { tool_name: toolName, available_tools_count: this.toolService.getToolsCount() },
        correlationId,
        'Tool validation failed - tool not found in available tools',
      );

      throw new ToolExecutionError(`Tool ${toolName} is not available`, { tool_name: toolName });
    }

    const domainTool = this.toolService.getTool(toolName);
    if (!domainTool) {
      VibeLogger.logError(
        'execute_tool_instance_not_found',
        'Tool exists in registry but instance not found',
        { tool_name: toolName },
        correlationId,
        'Tool registry inconsistency - tool marked as available but instance is null',
      );

      throw new ToolExecutionError(`Tool ${toolName} instance is not available`, {
        tool_name: toolName,
      });
    }

    // Cast to DynamicUnityCommandTool since the current implementation uses it
    // TODO: This is a temporary solution - need proper abstraction for tool execution
    return domainTool as unknown as DynamicUnityCommandTool;
  }

  /**
   * Execute the dynamic tool
   *
   * @param dynamicTool Tool instance to execute
   * @param args Tool arguments
   * @param correlationId Correlation ID for logging
   * @returns Tool execution result
   * @throws ToolExecutionError if execution fails
   */
  private async executeTool(
    dynamicTool: DynamicUnityCommandTool,
    args: Record<string, unknown>,
    correlationId: string,
  ): Promise<ExecuteToolResponse> {
    try {
      VibeLogger.logDebug(
        'execute_tool_calling_unity',
        'Calling Unity tool execution',
        { tool_name: dynamicTool.name, argument_keys: Object.keys(args) },
        correlationId,
        'Delegating to dynamic tool for Unity communication',
      );

      const result = await dynamicTool.execute(args);

      // Convert ToolResponse to UseCase response format
      return {
        content: result.content,
        isError: result.isError,
      };
    } catch (error) {
      VibeLogger.logError(
        'execute_tool_unity_execution_failed',
        'Unity tool execution failed',
        {
          tool_name: dynamicTool.name,
          error_message: error instanceof Error ? error.message : String(error),
        },
        correlationId,
        'Tool execution failed during Unity communication',
      );

      throw new ToolExecutionError(
        `Tool execution failed: ${error instanceof Error ? error.message : 'Unknown error'}`,
        { tool_name: dynamicTool.name, original_error: error },
      );
    }
  }

  /**
   * Handle execution errors and return formatted error response
   *
   * @param error Error that occurred
   * @param toolName Tool name for context
   * @param correlationId Correlation ID for logging
   * @returns Error response
   */
  private handleExecutionError(
    error: unknown,
    toolName: string,
    correlationId: string,
  ): ExecuteToolResponse {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error';

    VibeLogger.logError(
      'execute_tool_use_case_error',
      'Tool execution workflow failed',
      {
        tool_name: toolName,
        error_message: errorMessage,
        error_type: error instanceof Error ? error.constructor.name : typeof error,
      },
      correlationId,
      'UseCase workflow failed - returning error response to client',
    );

    return {
      content: [
        {
          type: 'text',
          text: `Error executing ${toolName}: ${errorMessage}`,
        },
      ],
      isError: true,
    };
  }
}
