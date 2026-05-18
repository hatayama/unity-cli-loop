/**
 * ドメインエラー階層の定義
 *
 * 関連クラス:
 * - UseCase実装クラス群（domain/use-cases/）
 * - ApplicationService実装クラス群（application/services/）
 */

/**
 * ドメインエラーの基底クラス
 *
 * 責任:
 * - 全てのドメイン関連エラーの基底
 * - エラーコードによる分類
 * - 型安全なエラーハンドリングの提供
 */
export abstract class DomainError extends Error {
  abstract readonly code: string;

  constructor(
    message: string,
    public readonly details?: unknown,
  ) {
    super(message);
    this.name = this.constructor.name;

    // TypeScriptでError継承時のプロトタイプチェーン修正
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/**
 * Unity接続関連のエラー
 *
 * 使用場面:
 * - Unity接続の確立に失敗した場合
 * - 接続が予期せず切断された場合
 * - ポート設定に問題がある場合
 */
export class ConnectionError extends DomainError {
  readonly code = 'CONNECTION_ERROR';
}

/**
 * ツール実行関連のエラー
 *
 * 使用場面:
 * - ツールの実行に失敗した場合
 * - ツールが存在しない場合
 * - ツールの引数が不正な場合
 */
export class ToolExecutionError extends DomainError {
  readonly code = 'TOOL_EXECUTION_ERROR';
}

/**
 * 入力値検証エラー
 *
 * 使用場面:
 * - リクエストパラメータが不正な場合
 * - 必須パラメータが不足している場合
 * - 値の形式が不正な場合
 */
export class ValidationError extends DomainError {
  readonly code = 'VALIDATION_ERROR';
}

/**
 * 発見プロセス関連のエラー
 *
 * 使用場面:
 * - Unity発見プロセスに失敗した場合
 * - 発見されたUnityとの接続に失敗した場合
 * - 発見タイムアウトが発生した場合
 */
export class DiscoveryError extends DomainError {
  readonly code = 'DISCOVERY_ERROR';
}

/**
 * クライアント互換性関連のエラー
 *
 * 使用場面:
 * - サポートされていないクライアントからの接続
 * - クライアント設定の初期化に失敗した場合
 * - 互換性チェックに失敗した場合
 */
export class ClientCompatibilityError extends DomainError {
  readonly code = 'CLIENT_COMPATIBILITY_ERROR';
}
