/**
 * Unity Tool Communication Service Interface
 *
 * Related classes:
 * - UnityToolManager (implementation class)
 * - Used by UseCases that need Unity communication for tools
 */

import { Tool } from '@modelcontextprotocol/sdk/types.js';
import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for Unity communication related to tools
 * Segregated from IToolService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Unity tool communication
 * - Tool data retrieval from Unity
 */
export interface IUnityToolCommunicationService extends ApplicationService {
  /**
   * Get tool list from Unity
   *
   * @returns Array of tools retrieved from Unity
   * @throws ToolExecutionError if Unity communication fails
   */
  getToolsFromUnity(): Promise<Tool[]>;
}
