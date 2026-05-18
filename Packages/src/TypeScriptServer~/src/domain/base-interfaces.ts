/**
 * DDD（ドメイン駆動設計）パターンの基底インターフェース
 *
 * 関連クラス:
 * - UseCaseクラス群（domain/use-cases/）
 * - ApplicationServiceクラス群（application/services/）
 */

/**
 * UseCaseクラスの基底インターフェース
 *
 * 責任:
 * - 時間的凝集を持つビジネスワークフローの管理
 * - 毎回新しいインスタンスを生成して使用する
 * - 複数のアプリケーションサービスを組み合わせた処理の実行
 *
 * @template TRequest リクエストの型
 * @template TResponse レスポンスの型
 */
export interface UseCase<TRequest, TResponse> {
  /**
   * UseCaseの実行
   *
   * @param request リクエストデータ
   * @returns 処理結果のPromise
   */
  execute(request: TRequest): Promise<TResponse>;
}

/**
 * ApplicationServiceクラスの基底インターフェース
 *
 * 責任:
 * - 単一の技術的機能の提供
 * - ステートレスな動作
 * - インフラストラクチャ層への委譲
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type -- Marker interface for type safety
export interface ApplicationService {
  // 各アプリケーションサービスは具体的なメソッドを定義する
  // このインターフェースは型安全性を保証するためのマーカーインターフェース
}
