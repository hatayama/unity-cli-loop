/**
 * Response Models for UseCase Layer
 *
 * Related classes:
 * - UseCase implementations use these as TResponse type parameters
 * - Request models defined in requests.ts
 */

import { DomainTool } from './domain-tool.js';

/**
 * Response for tool execution UseCase
 */
export interface ExecuteToolResponse {
  content: Array<{
    type: string;
    text: string;
  }>;
  isError?: boolean;
}

/**
 * Response for tools refresh UseCase
 */
export interface RefreshToolsResponse {
  tools: DomainTool[];
  refreshedAt: string;
}
