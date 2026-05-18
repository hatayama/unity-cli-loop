/**
 * Error Conversion Service
 *
 * Related classes:
 * - DomainError (domain/errors.ts) - conversion target
 * - InfrastructureError (infrastructure/errors.ts) - conversion source
 * - Application Services - use this for error conversion
 *
 * Key principles:
 * - Infrastructure層のエラーをDomain層のエラーに変換
 * - 技術的詳細をログに記録しつつ、ビジネス的な意味でエラーを分類
 * - Clean Architectureの依存関係を保持
 */

import {
  DomainError,
  ConnectionError,
  ToolExecutionError,
  ValidationError,
  DiscoveryError,
  ClientCompatibilityError,
} from '../domain/errors.js';
import {
  InfrastructureError,
  UnityCommunicationError,
  ToolManagementError,
  ServiceResolutionError,
  NetworkError,
  McpProtocolError,
} from '../infrastructure/errors.js';
import { VibeLogger } from '../utils/vibe-logger.js';

/**
 * Infrastructure層のエラーをDomain層のエラーに変換するサービス
 *
 * 責任:
 * - Infrastructure固有エラーのDomainエラーへの変換
 * - 技術的詳細のログ記録
 * - エラー情報の適切な抽象化
 */
export class ErrorConverter {
  /**
   * Infrastructure層のエラーをDomain層のエラーに変換
   *
   * @param error 変換対象のエラー
   * @param operation 操作名（ログ用）
   * @param correlationId 相関ID
   * @returns 変換されたDomainError
   */
  static convertToDomainError(
    error: unknown,
    operation: string,
    correlationId?: string,
  ): DomainError {
    // 既にDomainErrorの場合はそのまま返す
    if (error instanceof DomainError) {
      return error;
    }

    // Infrastructure層エラーの変換
    if (error instanceof InfrastructureError) {
      return this.convertInfrastructureError(error, operation, correlationId);
    }

    // 一般的なErrorの変換
    if (error instanceof Error) {
      return this.convertGenericError(error, operation, correlationId);
    }

    // 不明なエラーの処理
    return this.convertUnknownError(error, operation, correlationId);
  }

  /**
   * Infrastructure層エラーをDomain層エラーに変換
   */
  private static convertInfrastructureError(
    error: InfrastructureError,
    operation: string,
    correlationId?: string,
  ): DomainError {
    // 技術的詳細をログに記録
    VibeLogger.logError(
      `${operation}_infrastructure_error`,
      `Infrastructure error during ${operation}`,
      error.getFullErrorInfo(),
      correlationId,
      'Error Converter logging technical details before domain conversion',
    );

    // カテゴリに基づいてDomainErrorに変換
    switch (error.category) {
      case 'UNITY_COMMUNICATION':
        return new ConnectionError(`Unity communication failed: ${error.message}`, {
          original_category: error.category,
          unity_endpoint: (error as UnityCommunicationError).unityEndpoint,
        });

      case 'TOOL_MANAGEMENT':
        return new ToolExecutionError(`Tool management failed: ${error.message}`, {
          original_category: error.category,
          tool_name: (error as ToolManagementError).toolName,
        });

      case 'SERVICE_RESOLUTION':
        return new ValidationError(`Service resolution failed: ${error.message}`, {
          original_category: error.category,
          service_token: (error as ServiceResolutionError).serviceToken,
        });

      case 'NETWORK':
        return new DiscoveryError(`Network operation failed: ${error.message}`, {
          original_category: error.category,
          endpoint: (error as NetworkError).endpoint,
          port: (error as NetworkError).port,
        });

      case 'MCP_PROTOCOL':
        return new ClientCompatibilityError(`MCP protocol error: ${error.message}`, {
          original_category: error.category,
          protocol_version: (error as McpProtocolError).protocolVersion,
        });

      default:
        return new ToolExecutionError(`Infrastructure error: ${error.message}`, {
          original_category: error.category,
        });
    }
  }

  /**
   * 一般的なErrorをDomain層エラーに変換
   */
  private static convertGenericError(
    error: Error,
    operation: string,
    correlationId?: string,
  ): DomainError {
    VibeLogger.logError(
      `${operation}_generic_error`,
      `Generic error during ${operation}`,
      {
        error_name: error.name,
        error_message: error.message,
        stack: error.stack,
      },
      correlationId,
      'Error Converter handling generic Error instance',
    );

    // メッセージから推測してエラー種別を決定
    const message = error.message.toLowerCase();

    if (message.includes('connection') || message.includes('connect')) {
      return new ConnectionError(`Connection error: ${error.message}`);
    }

    if (message.includes('tool') || message.includes('execute')) {
      return new ToolExecutionError(`Tool execution error: ${error.message}`);
    }

    if (message.includes('validation') || message.includes('invalid')) {
      return new ValidationError(`Validation error: ${error.message}`);
    }

    if (message.includes('discovery') || message.includes('network')) {
      return new DiscoveryError(`Discovery error: ${error.message}`);
    }

    // デフォルトはToolExecutionError
    return new ToolExecutionError(`Unexpected error: ${error.message}`);
  }

  /**
   * 不明なエラーオブジェクトをDomain層エラーに変換
   */
  private static convertUnknownError(
    error: unknown,
    operation: string,
    correlationId?: string,
  ): DomainError {
    const errorString = typeof error === 'string' ? error : JSON.stringify(error);

    VibeLogger.logError(
      `${operation}_unknown_error`,
      `Unknown error type during ${operation}`,
      { error_value: error, error_type: typeof error },
      correlationId,
      'Error Converter handling unknown error type',
    );

    return new ToolExecutionError(`Unknown error occurred: ${errorString}`);
  }

  /**
   * エラーが回復可能かどうかを判定
   *
   * @param error 判定対象のエラー
   * @returns 回復可能な場合true
   */
  static isRecoverable(error: DomainError): boolean {
    switch (error.code) {
      case 'CONNECTION_ERROR':
      case 'DISCOVERY_ERROR':
        return true; // 接続・発見エラーは再試行可能

      case 'VALIDATION_ERROR':
      case 'CLIENT_COMPATIBILITY_ERROR':
        return false; // 検証・互換性エラーは回復不可能

      case 'TOOL_EXECUTION_ERROR':
        return true; // ツール実行エラーは状況によって再試行可能

      default:
        return false; // 不明なエラーは安全のため回復不可能とする
    }
  }
}
