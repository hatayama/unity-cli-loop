/**
 * Unity Event Service Interface
 *
 * Related classes:
 * - UnityEventHandler (implementation class)
 * - Used by UseCases that need Unity event handling
 */

import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for Unity-specific event handling
 * Segregated from IEventService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Setup Unity event listeners only
 * - Single-purpose Unity event operations
 */
export interface IUnityEventService extends ApplicationService {
  /**
   * Setup Unity event listener
   *
   * @param onToolsChanged Callback for when tools change
   */
  setupUnityEventListener(onToolsChanged: () => Promise<void>): void;
}
