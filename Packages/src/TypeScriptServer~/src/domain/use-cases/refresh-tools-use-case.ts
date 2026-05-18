/**
 * Refresh Tools UseCase
 *
 * Related classes:
 * - IConnectionService (application/interfaces/connection-service.ts)
 * - IToolManagementService (application/interfaces/tool-management-service.ts)
 * - IToolQueryService (application/interfaces/tool-query-service.ts)
 */

import { UseCase } from '../base-interfaces.js';
import { RefreshToolsRequest } from '../models/requests.js';
import { RefreshToolsResponse } from '../models/responses.js';
import { VibeLogger } from '../../utils/vibe-logger.js';
import { ErrorConverter } from '../../application/error-converter.js';
import { IConnectionService } from '../../application/interfaces/connection-service.js';
import { IToolManagementService } from '../../application/interfaces/tool-management-service.js';
import { TIMEOUTS } from '../../constants.js';
import { IToolQueryService } from '../../application/interfaces/tool-query-service.js';

/**
 * UseCase for refreshing Unity tools
 *
 * Responsibilities:
 * - Orchestrate the complete tool refresh workflow
 * - Handle Unity reconnection scenarios (domain reload recovery)
 * - Manage temporal cohesion of tool refresh process
 * - Coordinate tool refresh with notification sending
 *
 * Workflow:
 * 1. Ensure Unity connection is established
 * 2. Initialize/refresh dynamic tools from Unity
 * 3. Return refreshed tools list
 * 4. Support notification callback for MCP client updates
 */
export class RefreshToolsUseCase implements UseCase<RefreshToolsRequest, RefreshToolsResponse> {
  private connectionService: IConnectionService;
  private toolManagementService: IToolManagementService;
  private toolQueryService: IToolQueryService;

  constructor(
    connectionService: IConnectionService,
    toolManagementService: IToolManagementService,
    toolQueryService: IToolQueryService,
  ) {
    this.connectionService = connectionService;
    this.toolManagementService = toolManagementService;
    this.toolQueryService = toolQueryService;
  }

  /**
   * Execute the tool refresh workflow
   *
   * @param request Tool refresh request
   * @returns Tool refresh response
   */
  async execute(request: RefreshToolsRequest): Promise<RefreshToolsResponse> {
    const correlationId = VibeLogger.generateCorrelationId();

    VibeLogger.logInfo(
      'refresh_tools_use_case_start',
      'Starting tool refresh workflow',
      { include_development: request.includeDevelopmentOnly },
      correlationId,
      'UseCase orchestrating tool refresh workflow for domain reload recovery',
    );

    try {
      // Step 1: Ensure Unity connection is established (critical for domain reload recovery)
      await this.ensureUnityConnection(correlationId);

      // Step 2: Initialize/refresh dynamic tools from Unity
      await this.refreshToolsFromUnity(correlationId);

      // Step 3: Get refreshed tools list
      const refreshedTools = this.toolQueryService.getAllTools();

      const response: RefreshToolsResponse = {
        tools: refreshedTools,
        refreshedAt: new Date().toISOString(),
      };

      VibeLogger.logInfo(
        'refresh_tools_use_case_success',
        'Tool refresh workflow completed successfully',
        {
          tool_count: refreshedTools.length,
          refreshed_at: response.refreshedAt,
        },
        correlationId,
        'Tool refresh completed successfully - Unity tools updated after domain reload',
      );

      return response;
    } catch (error) {
      return this.handleRefreshError(error, request, correlationId);
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
        'refresh_tools_unity_not_connected',
        'Unity not connected during tool refresh, attempting to establish connection',
        { connected: false },
        correlationId,
        'Unity connection required for tool refresh after domain reload',
      );

      try {
        await this.connectionService.ensureConnected(TIMEOUTS.CONNECTION_WAIT);
      } catch (error) {
        // Use ErrorConverter for consistent error handling
        const domainError = ErrorConverter.convertToDomainError(
          error,
          'refresh_tools_connection_ensure',
          correlationId,
        );
        throw domainError;
      }
    }

    VibeLogger.logDebug(
      'refresh_tools_connection_verified',
      'Unity connection verified for tool refresh',
      { connected: true },
      correlationId,
      'Connection ready for tool refresh after domain reload',
    );
  }

  /**
   * Refresh tools from Unity by re-initializing dynamic tools
   *
   * @param correlationId Correlation ID for logging
   */
  private async refreshToolsFromUnity(correlationId: string): Promise<void> {
    try {
      VibeLogger.logDebug(
        'refresh_tools_initializing',
        'Re-initializing dynamic tools from Unity',
        {},
        correlationId,
        'Fetching latest tool definitions from Unity after domain reload',
      );

      // Re-initialize tools (this will fetch latest tool definitions from Unity)
      await this.toolManagementService.initializeTools();

      VibeLogger.logInfo(
        'refresh_tools_initialized',
        'Dynamic tools re-initialized successfully from Unity',
        { tool_count: this.toolQueryService.getToolsCount() },
        correlationId,
        'Tool definitions updated from Unity after domain reload',
      );
    } catch (error) {
      // Use ErrorConverter to convert Infrastructure errors to Domain errors
      const domainError = ErrorConverter.convertToDomainError(
        error,
        'refresh_tools_initialization',
        correlationId,
      );

      throw domainError;
    }
  }

  /**
   * Handle tool refresh errors
   *
   * @param error Error that occurred
   * @param request Original request
   * @param correlationId Correlation ID for logging
   * @returns RefreshToolsResponse with error state
   */
  private handleRefreshError(
    error: unknown,
    request: RefreshToolsRequest,
    correlationId: string,
  ): RefreshToolsResponse {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error';

    VibeLogger.logError(
      'refresh_tools_use_case_error',
      'Tool refresh workflow failed',
      {
        include_development: request.includeDevelopmentOnly,
        error_message: errorMessage,
        error_type: error instanceof Error ? error.constructor.name : typeof error,
      },
      correlationId,
      'UseCase workflow failed - returning empty tools list to prevent client errors',
    );

    // Return empty tools list instead of throwing - tool refresh should be resilient
    return {
      tools: [],
      refreshedAt: new Date().toISOString(),
    };
  }
}
