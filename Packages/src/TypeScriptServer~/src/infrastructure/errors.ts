/**
 * Infrastructure Error Types
 *
 * Related classes:
 * - DomainError (domain/errors.ts) - for conversion to domain errors
 * - Infrastructure service implementations
 *
 * Key principles:
 * - Infrastructure層固有のエラー型定義
 * - Domain層への変換をサポート
 * - 技術的詳細を含む詳細なエラー情報
 */

/**
 * Infrastructure層エラーの基底クラス
 *
 * 責任:
 * - Infrastructure層での技術的エラーの基底
 * - 詳細な技術情報の保持
 * - Domain層エラーへの変換サポート
 */
export abstract class InfrastructureError extends Error {
  abstract readonly category: string;

  constructor(
    message: string,
    public readonly technicalDetails?: unknown,
    public readonly originalError?: Error,
  ) {
    super(message);
    this.name = this.constructor.name;

    // TypeScriptでError継承時のプロトタイプチェーン修正
    Object.setPrototypeOf(this, new.target.prototype);
  }

  /**
   * 技術的詳細を含む完全なエラー情報を取得
   */
  getFullErrorInfo(): {
    message: string;
    category: string;
    technicalDetails?: unknown;
    originalError?: string;
    stack?: string;
  } {
    return {
      message: this.message,
      category: this.category,
      technicalDetails: this.technicalDetails,
      originalError: this.originalError?.message,
      stack: this.stack,
    };
  }
}

/**
 * Unity通信関連のエラー
 *
 * 使用場面:
 * - Unity TCPソケット通信エラー
 * - Unity APIレスポンスの解析エラー
 * - ネットワーク接続タイムアウト
 */
export class UnityCommunicationError extends InfrastructureError {
  readonly category = 'UNITY_COMMUNICATION';

  constructor(
    message: string,
    public readonly unityEndpoint?: string,
    public readonly requestData?: unknown,
    originalError?: Error,
  ) {
    super(message, { unityEndpoint, requestData }, originalError);
  }
}

/**
 * Tool管理関連のエラー
 *
 * 使用場面:
 * - Dynamic tool作成失敗
 * - Tool schema解析エラー
 * - Tool実行環境エラー
 */
export class ToolManagementError extends InfrastructureError {
  readonly category = 'TOOL_MANAGEMENT';

  constructor(
    message: string,
    public readonly toolName?: string,
    public readonly toolData?: unknown,
    originalError?: Error,
  ) {
    super(message, { toolName, toolData }, originalError);
  }
}

/**
 * ServiceLocator関連のエラー
 *
 * 使用場面:
 * - サービス解決失敗
 * - 循環依存検出
 * - サービス登録エラー
 */
export class ServiceResolutionError extends InfrastructureError {
  readonly category = 'SERVICE_RESOLUTION';

  constructor(
    message: string,
    public readonly serviceToken?: string,
    public readonly resolutionStack?: unknown[],
    originalError?: Error,
  ) {
    super(message, { serviceToken, resolutionStack }, originalError);
  }
}

/**
 * Network/Discovery関連のエラー
 *
 * 使用場面:
 * - Unity発見プロセスエラー
 * - TCP接続エラー
 * - ポートバインディングエラー
 */
export class NetworkError extends InfrastructureError {
  readonly category = 'NETWORK';

  constructor(
    message: string,
    public readonly endpoint?: string,
    public readonly port?: number,
    originalError?: Error,
  ) {
    super(message, { endpoint, port }, originalError);
  }
}

/**
 * MCP Protocol関連のエラー
 *
 * 使用場面:
 * - MCPメッセージ解析エラー
 * - プロトコルバージョン不一致
 * - クライアント互換性エラー
 */
export class McpProtocolError extends InfrastructureError {
  readonly category = 'MCP_PROTOCOL';

  constructor(
    message: string,
    public readonly protocolVersion?: string,
    public readonly messageData?: unknown,
    originalError?: Error,
  ) {
    super(message, { protocolVersion, messageData }, originalError);
  }
}
