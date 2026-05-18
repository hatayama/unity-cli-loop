/**
 * Process Control Service Interface
 *
 * Related classes:
 * - UnityEventHandler (implementation class)
 * - Used by server initialization for process management
 */

import { ApplicationService } from '../../domain/base-interfaces.js';

/**
 * Interface for process control and signal handling
 * Segregated from IEventService following Interface Segregation Principle
 *
 * Responsibilities:
 * - Process signal handling
 * - Graceful shutdown management
 * - Process state tracking
 */
export interface IProcessControlService extends ApplicationService {
  /**
   * Setup signal handlers
   *
   * Handles SIGINT, SIGTERM, SIGHUP, stdin close etc.
   */
  setupSignalHandlers(): void;

  /**
   * Execute graceful shutdown
   *
   * Cleanup processing on process termination
   */
  gracefulShutdown(): void;

  /**
   * Check if shutdown is in progress
   *
   * @returns true if shutting down
   */
  isShuttingDown(): boolean;
}
