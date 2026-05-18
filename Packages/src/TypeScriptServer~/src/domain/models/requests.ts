/**
 * Request Models for UseCase Layer
 *
 * Related classes:
 * - UseCase implementations use these as TRequest type parameters
 * - Response models defined in responses.ts
 */

/**
 * Request for tool execution UseCase
 */
export interface ExecuteToolRequest {
  toolName: string;
  arguments: Record<string, unknown>;
}

/**
 * Request for tools refresh UseCase
 */
export interface RefreshToolsRequest {
  includeDevelopmentOnly?: boolean;
}
