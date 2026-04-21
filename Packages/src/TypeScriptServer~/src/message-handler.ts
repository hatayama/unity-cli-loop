import { JSONRPC } from './constants.js';
import { ContentLengthFramer } from './utils/content-length-framer.js';
import { DynamicBuffer } from './utils/dynamic-buffer.js';
import { VibeLogger } from './utils/vibe-logger.js';

// Constants for JSON-RPC error types
const JsonRpcErrorTypes = {
  SECURITY_BLOCKED: 'security_blocked',
  INTERNAL_ERROR: 'internal_error',
} as const;

// Type definitions for JSON-RPC messages
interface JsonRpcNotification {
  method: string;
  params?: unknown;
  jsonrpc?: string;
}

interface JsonRpcResponse {
  id: string;
  result?: unknown;
  error?: {
    message: string;
    data?: {
      command?: string;
      reason?: string;
      message?: string;
      type?: string;
    };
  };
  jsonrpc?: string;
}

// Type guard functions
const isJsonRpcNotification = (msg: unknown): msg is JsonRpcNotification => {
  return (
    typeof msg === 'object' &&
    msg !== null &&
    'method' in msg &&
    typeof (msg as JsonRpcNotification).method === 'string' &&
    !('id' in msg)
  );
};

const isJsonRpcResponse = (msg: unknown): msg is JsonRpcResponse => {
  return (
    typeof msg === 'object' &&
    msg !== null &&
    'id' in msg &&
    typeof (msg as JsonRpcResponse).id === 'string' &&
    !('method' in msg)
  );
};

const hasValidId = (msg: unknown): msg is { id: string } => {
  return (
    typeof msg === 'object' &&
    msg !== null &&
    'id' in msg &&
    typeof (msg as { id: string }).id === 'string'
  );
};

/**
 * Handles JSON-RPC message processing with Content-Length framing support
 * Follows Single Responsibility Principle - only handles message parsing and routing
 *
 * Design document reference: /Packages/docs/ARCHITECTURE_TypeScript.md
 * Architecture reference: Packages/src/TypeScriptServer~/ARCHITECTURE.md
 *
 * Related classes:
 * - UnityClient: Uses this class for JSON-RPC message handling
 * - UnityMcpServer: Indirectly uses via UnityClient for Unity communication
 * - ContentLengthFramer: Handles framing/parsing of Content-Length protocol
 * - DynamicBuffer: Manages buffering of incoming data fragments
 */
export class MessageHandler {
  private notificationHandlers: Map<string, (params: unknown) => void> = new Map();
  private pendingRequests: Map<
    string,
    { resolve: (value: unknown) => void; reject: (reason: unknown) => void; timestamp: number }
  > = new Map();

  // Content-Length framing components
  private dynamicBuffer: DynamicBuffer = new DynamicBuffer();

  /**
   * Register notification handler for specific method
   */
  onNotification(method: string, handler: (params: unknown) => void): void {
    this.notificationHandlers.set(method, handler);
  }

  /**
   * Remove notification handler
   */
  offNotification(method: string): void {
    this.notificationHandlers.delete(method);
  }

  /**
   * Register a pending request
   */
  registerPendingRequest(
    id: string,
    resolve: (value: unknown) => void,
    reject: (reason: unknown) => void,
  ): void {
    this.pendingRequests.set(id, { resolve, reject, timestamp: Date.now() });
  }

  /**
   * Handle incoming data from Unity using Content-Length framing
   */
  handleIncomingData(data: Buffer | string): void {
    // Append new data to dynamic buffer (Buffer or string - DynamicBuffer handles both)
    this.dynamicBuffer.append(data);

    // Extract all complete frames
    const frames = this.dynamicBuffer.extractAllFrames();

    for (const frame of frames) {
      if (!frame || frame.trim() === '') {
        continue;
      }

      try {
        const message: unknown = JSON.parse(frame);

        // Check if this is a notification (no id field)
        if (isJsonRpcNotification(message)) {
          this.handleNotification(message);
        } else if (isJsonRpcResponse(message)) {
          // This is a response to a request
          this.handleResponse(message);
        } else if (hasValidId(message)) {
          // Fallback for other messages with valid id
          this.handleResponse(message);
        }
      } catch (parseError) {
        VibeLogger.logError(
          'json_parse_error',
          'Error parsing JSON frame',
          {
            error: parseError instanceof Error ? parseError.message : String(parseError),
            frame: frame.substring(0, 200), // Truncate for security
          },
          undefined,
          'Invalid JSON received from Unity, frame may be corrupted',
        );
      }
    }
  }

  /**
   * Handle notification from Unity
   */
  private handleNotification(notification: JsonRpcNotification): void {
    const { method, params } = notification;

    const handler = this.notificationHandlers.get(method);
    if (handler) {
      try {
        handler(params);
      } catch (error) {
        VibeLogger.logError(
          'notification_handler_error',
          `Error in notification handler for ${method}`,
          { error: error instanceof Error ? error.message : String(error) },
          undefined,
          'Exception occurred while processing notification',
        );
      }
    }
  }

  /**
   * Handle response from Unity
   */
  private handleResponse(response: JsonRpcResponse): void {
    const { id } = response;
    const pending = this.pendingRequests.get(id);

    if (pending) {
      this.pendingRequests.delete(id);

      if (response.error) {
        let errorMessage = response.error.message || 'Unknown error';

        // If security blocked, provide detailed information
        if (response.error.data?.type === JsonRpcErrorTypes.SECURITY_BLOCKED) {
          const data = response.error.data;
          errorMessage = `${data.reason || errorMessage}`;
          if (data.command) {
            errorMessage += ` (Command: ${data.command})`;
          }
          // Add instruction for enabling the feature
          errorMessage +=
            ' To use this feature, enable the corresponding option in Unity menu: Window > uLoopMCP > Security Settings';
        }

        pending.reject(new Error(errorMessage));
      } else {
        pending.resolve(response);
      }
    } else {
      // This can happen due to connection issues, reconnection, or timing issues
      // Log as warning instead of error since it's not always a critical issue
      const activeRequestIds = Array.from(this.pendingRequests.keys()).join(', ');
      const currentTime = Date.now();

      // Use structured logging instead of console for better debugging
      VibeLogger.logWarning(
        'unknown_request_response',
        `Received response for unknown request ID: ${id}`,
        {
          unknown_request_id: id,
          active_request_ids: activeRequestIds,
          current_time: currentTime,
        },
        undefined,
        'This may be a delayed response from before reconnection',
        'Monitor if this pattern indicates connection stability issues',
      );
    }
  }

  /**
   * Clear all pending requests with rejection (used during permanent disconnect)
   */
  clearPendingRequests(reason: string): void {
    const requestIds = Array.from(this.pendingRequests.keys());
    const pendingCount = this.pendingRequests.size;

    if (pendingCount > 0) {
      VibeLogger.logWarning(
        'pending_requests_clearing_with_rejection',
        'Clearing all pending requests with rejection',
        {
          pending_count: pendingCount,
          request_ids: requestIds,
          reason: reason,
        },
        undefined,
        'Connection permanently lost - rejecting all pending requests',
      );
    }

    for (const [, pending] of this.pendingRequests) {
      pending.reject(new Error(reason));
    }
    this.pendingRequests.clear();
  }

  /**
   * Clear all pending requests with resolution (used during temporary disconnect)
   * Returns success message instead of error, allowing AI to understand reconnection is possible
   */
  clearPendingRequestsWithSuccess(message: string): void {
    const requestIds = Array.from(this.pendingRequests.keys());
    const pendingCount = this.pendingRequests.size;

    if (pendingCount > 0) {
      VibeLogger.logInfo(
        'pending_requests_clearing_with_success',
        'Clearing all pending requests with success (temporary disconnect)',
        {
          pending_count: pendingCount,
          request_ids: requestIds,
          message: message,
        },
        undefined,
        'Temporary disconnect - resolving pending requests with guidance message',
      );
    }

    for (const [id, pending] of this.pendingRequests) {
      pending.resolve({ id, result: message });
    }
    this.pendingRequests.clear();
  }

  /**
   * Remove a specific pending request (used for individual timeout)
   */
  removePendingRequest(requestId: string): void {
    this.pendingRequests.delete(requestId);
  }

  /**
   * Create JSON-RPC request with Content-Length framing
   */
  createRequest(method: string, params: Record<string, unknown>, id: string): string {
    const request = {
      jsonrpc: JSONRPC.VERSION,
      id,
      method,
      params,
    };
    const jsonContent = JSON.stringify(request);
    return ContentLengthFramer.createFrame(jsonContent);
  }

  /**
   * Create JSON-RPC notification with Content-Length framing (no id = fire-and-forget)
   */
  createNotification(method: string, params: Record<string, unknown>): string {
    const notification = {
      jsonrpc: JSONRPC.VERSION,
      method,
      params,
    };
    const jsonContent = JSON.stringify(notification);
    return ContentLengthFramer.createFrame(jsonContent);
  }

  /**
   * Clear the dynamic buffer (for connection reset)
   */
  clearBuffer(): void {
    this.dynamicBuffer.clear();
  }

  /**
   * Get buffer statistics for debugging
   */
  getBufferStats(): {
    size: number;
    maxSize: number;
    utilization: number;
    hasCompleteHeader: boolean;
    preview: string;
  } {
    return this.dynamicBuffer.getStats();
  }
}
