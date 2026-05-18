/**
 * Tool Management Service Interface
 *
 * Related classes:
 * - UnityToolManager (implementation class)
 * - Used by UseCases that need tool lifecycle management
 */

import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for tool lifecycle management operations
 * Segregated from IToolService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Tool initialization and refresh
 * - Client configuration
 * - Tool lifecycle management
 */
export interface IToolManagementService extends ApplicationService {
  /**
   * Initialize dynamic tools
   *
   * @throws ToolExecutionError if initialization fails
   */
  initializeTools(): Promise<void>;

  /**
   * Refresh dynamic tools (re-fetch)
   *
   * @throws ToolExecutionError if refresh fails
   */
  refreshTools(): Promise<void>;

  /**
   * Set client name
   *
   * @param clientName Client name
   */
  setClientName(clientName: string): void;
}
