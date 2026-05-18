/**
 * Tool Query Service Interface
 *
 * Related classes:
 * - UnityToolManager (implementation class)
 * - Used by UseCases that need tool querying only
 */

import { DomainTool } from '../../domain/models/domain-tool.js';
import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for tool querying operations
 * Segregated from IToolService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Tool information retrieval
 * - Tool existence checking
 * - Tool collection access
 */
export interface IToolQueryService extends ApplicationService {
  /**
   * Get all tools
   *
   * @returns Array of tools
   */
  getAllTools(): DomainTool[];

  /**
   * Check if specified tool exists
   *
   * @param name Tool name
   * @returns true if exists
   */
  hasTool(name: string): boolean;

  /**
   * Get specified tool
   *
   * @param name Tool name
   * @returns Tool instance, undefined if not found
   */
  getTool(name: string): DomainTool | undefined;

  /**
   * Get number of tools
   *
   * @returns Current number of tools
   */
  getToolsCount(): number;
}
