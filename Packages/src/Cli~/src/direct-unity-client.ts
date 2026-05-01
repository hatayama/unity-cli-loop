/**
 * Direct Unity client for CLI usage.
 * Establishes one-shot local IPC connections to the Unity Editor bridge.
 */

// Non-null assertions are used after framed response parsing where data existence is guaranteed by protocol
/* eslint-disable @typescript-eslint/no-non-null-assertion */

import * as net from 'net';
import { createFrame, parseFrameFromBuffer, extractFrameFromBuffer } from './simple-framer.js';
import type { UloopRequestMetadata } from './request-metadata.js';
import type { UnityConnectionEndpoint } from './ipc-endpoint.js';

const JSONRPC_VERSION = '2.0';
const NETWORK_TIMEOUT_MS = 180000;

interface JsonRpcRequest {
  jsonrpc: string;
  method: string;
  params?: Record<string, unknown>;
  id: number;
  'x-uloop'?: UloopRequestMetadata;
}

interface JsonRpcResponse {
  jsonrpc: string;
  result?: unknown;
  error?: {
    code: number;
    message: string;
    data?: unknown;
  };
  id: number;
}

export class DirectUnityClient {
  private socket: net.Socket | null = null;
  private requestId: number = 0;
  private receiveBuffer: Buffer = Buffer.alloc(0);
  private readonly endpoint: UnityConnectionEndpoint;

  constructor(endpoint: UnityConnectionEndpoint) {
    this.endpoint = endpoint;
  }

  async connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      this.socket = new net.Socket();

      this.socket.on('error', (error: Error) => {
        reject(new Error(`Connection error: ${error.message}`));
      });

      this.socket.connect(this.endpoint.path, () => {
        resolve();
      });
    });
  }

  async sendRequest<T>(
    method: string,
    params?: Record<string, unknown>,
    options?: { requestMetadata?: UloopRequestMetadata },
  ): Promise<T> {
    if (!this.socket) {
      throw new Error('Not connected to Unity');
    }

    const request: JsonRpcRequest = {
      jsonrpc: JSONRPC_VERSION,
      method,
      params: params ?? {},
      id: ++this.requestId,
    };
    if (options?.requestMetadata !== undefined) {
      request['x-uloop'] = options.requestMetadata;
    }

    const requestJson = JSON.stringify(request);
    const framedMessage = createFrame(requestJson);

    return new Promise((resolve, reject) => {
      const socket = this.socket!;

      const cleanup = (): void => {
        clearTimeout(timeoutId);
        socket.off('data', onData);
        socket.off('error', onError);
        socket.off('close', onClose);
      };

      const timeoutId = setTimeout(() => {
        cleanup();
        reject(
          new Error(
            `Request timed out after ${NETWORK_TIMEOUT_MS}ms. Unity may be frozen or busy. [For AI] Report this to the user and ask how to proceed.`,
          ),
        );
      }, NETWORK_TIMEOUT_MS);

      const onData = (chunk: Buffer): void => {
        this.receiveBuffer = Buffer.concat([this.receiveBuffer, chunk]);

        const parseResult = parseFrameFromBuffer(this.receiveBuffer);
        if (!parseResult.isComplete) {
          return;
        }

        const extractResult = extractFrameFromBuffer(
          this.receiveBuffer,
          parseResult.contentLength,
          parseResult.headerLength,
        );

        if (extractResult.jsonContent === null) {
          return;
        }

        cleanup();
        this.receiveBuffer = extractResult.remainingData;

        const response = JSON.parse(extractResult.jsonContent) as JsonRpcResponse;

        if (response.error) {
          const data = response.error.data;
          const dataMessage =
            data !== null && data !== undefined && typeof data === 'object' && 'message' in data
              ? ` (${(data as { message: string }).message})`
              : '';
          reject(new Error(`Unity error: ${response.error.message}${dataMessage}`));
          return;
        }

        resolve(response.result as T);
      };

      const onError = (error: Error): void => {
        cleanup();
        reject(new Error(`Connection lost: ${error.message}`));
      };

      const onClose = (): void => {
        cleanup();
        reject(new Error('UNITY_NO_RESPONSE'));
      };

      socket.on('data', onData);
      socket.on('error', onError);
      socket.on('close', onClose);
      socket.write(framedMessage);
    });
  }

  disconnect(): void {
    if (this.socket) {
      this.socket.destroy();
      this.socket = null;
    }
    this.receiveBuffer = Buffer.alloc(0);
  }

  isConnected(): boolean {
    return this.socket !== null && !this.socket.destroyed;
  }
}
