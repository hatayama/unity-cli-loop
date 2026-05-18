/**
 * Notification Service Interface
 *
 * Related classes:
 * - UnityEventHandler (implementation class)
 * - Used by UseCases that need MCP notification sending
 */

import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for MCP notification sending
 * Segregated from IEventService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Send notifications to MCP clients
 * - Handle duplicate prevention
 */
export interface INotificationService extends ApplicationService {
  /**
   * Send tools changed notification
   *
   * With duplicate sending prevention
   */
  sendToolsChangedNotification(): void;
}
