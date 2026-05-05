# Unity CLI Loop Unity Editor側アーキテクチャ

## 1. 概要

このドキュメントは、`Packages/src/Editor` ディレクトリ内のC#コードのアーキテクチャについて詳細に説明します。このコードはUnity Editor内で動作し、Unity環境と外部`uloop` CLIとの橋渡しの役割を果たします。

### システムアーキテクチャ概要

```mermaid
graph TB
    subgraph "1. CLI呼び出し元"
        Agent[AI Agentまたは開発者Shell]
        CLI[uloop CLI]
    end
    
    subgraph "2. Unity Editor（Project IPCサーバー）"
        MB[McpBridgeServer<br/>Project IPCサーバー<br/>McpBridgeServer.cs]
        CMD[ツールシステム<br/>UnityApiHandler.cs]
        UI[McpEditorWindow<br/>GUI<br/>McpEditorWindow.cs]
        API[Unity APIs]
        SM[McpSessionManager<br/>McpSessionManager.cs]
    end
    
    Agent -->|コマンド実行| CLI
    CLI <-->|Project IPC JSON-RPC| MB
    MB <--> CMD
    CMD <--> API
    MB --> SM
    
    classDef client fill:#e1f5fe,stroke:#01579b,stroke-width:2px
    classDef server fill:#f3e5f5,stroke:#4a148c,stroke-width:2px
    classDef bridge fill:#fff3e0,stroke:#e65100,stroke-width:2px
    
    class Agent,CLI client
    class MB server
```

### クライアント・サーバー関係の内訳

```mermaid
graph LR
    subgraph "通信レイヤー"
        CLI[uloop CLI<br/>クライアント]
        Unity[Unity Editor<br/>Project IPCサーバー]
    end
    
    CLI -->|"Project IPC JSON-RPC<br/>ポート: 8700-9100"| Unity
    
    classDef client fill:#e1f5fe,stroke:#01579b,stroke-width:2px
    classDef server fill:#f3e5f5,stroke:#4a148c,stroke-width:2px
    classDef hybrid fill:#fff3e0,stroke:#e65100,stroke-width:2px
    
    class CLI client
    class Unity server
```

### プロトコルと通信詳細

```mermaid
sequenceDiagram
    participant Agent as AI Agentまたは開発者
    participant CLI as uloop CLI<br/>（クライアント）
    participant Unity as Unity Editor<br/>（Project IPCサーバー）<br/>McpBridgeServer.cs
    
    Agent->>CLI: uloopコマンド
    CLI->>Unity: Project IPC JSON-RPCリクエスト
    Unity->>CLI: Project IPC JSON-RPCレスポンス
    CLI->>Agent: コマンド出力
    
    Note over CLI, Unity: クライアント・サーバーの役割:
    Note over CLI: クライアント: 短命なコマンドセッションを開く
    Note over Unity: サーバー: Project IPCリクエストを受け入れ
```

### 通信プロトコルの概要

| コンポーネント | 役割 | プロトコル | ポート | 接続タイプ |
|-----------|------|----------|------|----------------|
| **uloop CLI** | **クライアント** | Project IPC JSON-RPC | 8700-9100 | Unityツールリクエストを開始 |
| **Unity Editor** | **サーバー** | Project IPC JSON-RPC | 8700-9100 | ローカルProject IPC接続を受け入れ |

主な責務は以下の通りです：
1. **Project IPCサーバーの実行（`McpBridgeServer`）**: ローカル`uloop` CLIからの接続をリッスンしてツールリクエストを受信
2. **Unity操作の実行**: 受信したツールリクエストを処理してUnity Editor内でアクションを実行（プロジェクトのコンパイル、テスト実行、ログ取得など）
3. **セキュリティ管理**: `McpSecurityChecker`を通じてツール実行を検証・制御し、未承認操作を防止
4. **セッション管理**: `McpSessionManager`を通じてクライアントセッションと接続状態を維持
5. **ユーザーインターフェースの提供（`McpEditorWindow`）**: 開発者がCLIセットアップ、スキル、セキュリティ設定、Project IPCサーバー状態を管理するためのUnity Editor内GUI
6. **設定管理**: Unity CLI Loopの設定と対応AIツール向けスキルのインストール状態を永続化

## 2. コアアーキテクチャ原則

アーキテクチャは、堅牢性、拡張性、保守性を確保するためのいくつかの重要な設計原則に基づいて構築されています。

### 2.1. UseCase + ツールパターン (DDD統合)
システムは**Domain-Driven Design**を統合した**UseCase + ツールパターン**を中心としています。各アクションはDDDの原則に従って構造化され、UseCase層でビジネスワークフローを統制し、Application Service層で単一機能を実装しています。

#### UseCase層（ドメインワークフロー統制）
- **`AbstractUseCase<TSchema, TResponse>`**: すべてのUseCaseの基底クラス、単一の`ExecuteAsync`メソッドでワークフローを統制
- **具体的UseCase**: 複雑なワークフローを管理（例：`McpServerInitializationUseCase`、`DomainReloadRecoveryUseCase`）
- **時間的結合の分離**: Martin Fowlerのリファクタリング原則に従い、時間的結合をUseCaseクラスに分離

#### Application Service層（単一機能実装）
- **単一責任の徹底**: 各Application Serviceは一つの機能のみを実装
- **Service結果の統一**: すべてのサービスが`ServiceResult<T>`で結果を返す
- **例**: `CompilationExecutionService`、`LogRetrievalService`、`TestExecutionService`

#### ツール層（CLIコマンドインターフェース）
- **`IUnityTool`**: CLIコマンドディスパッチャー経由で公開されるすべてのツールの共通インターフェース
- **`AbstractUnityTool<TSchema, TResponse>`**: パラメータとレスポンスの型安全な処理を提供
- **`McpToolAttribute`**: ツールを自動登録にマークする属性
- **ツール実装**: 各ツールはUseCaseを呼び出してビジネスロジックを実行

#### レジストリとセキュリティ
- **`UnityToolRegistry`**: 使用可能なすべてのツールを発見・保持する中央レジストリ
- **`UnityApiHandler`**: ツール名とパラメータを受け取り、レジストリでツールを検索して実行
- **`McpSecurityChecker`**: セキュリティ設定に基づいた実行権限の検証

このDDD統合アーキテクチャにより、ビジネスロジックとインフラストラクチャが明確に分離され、高い拡張性と保守性を実現しています。

### 2.2. セキュリティアーキテクチャ
システムは未承認ツール実行を防ぐための包括的なセキュリティ制御を実装しています：

- **`McpSecurityChecker`**: 実行前にツール権限をチェックする中央セキュリティ検証コンポーネント
- **属性ベースセキュリティ**: ツールを実行要件を定義するセキュリティ属性で装飾可能
- **デフォルト拒否ポリシー**: 未知のツールはデフォルトで未承認操作を防ぐためにブロック
- **設定ベース制御**: Unity Editorの設定インターフェースを通じてセキュリティポリシーを設定可能

### 2.3. セッション管理
システムはクライアント接続と状態を処理するための堅牢なセッション管理を維持します：

- **`McpSessionManager`**: ドメインリロード永続化のための`ScriptableSingleton`として実装されたシングルトンセッションマネージャー
- **クライアント状態追跡**: 接続状態、クライアント識別、セッションメタデータを維持
- **ドメインリロード耐性**: 永続ストレージを通じてUnityドメインリロードを乗り越える
- **再接続サポート**: クライアント再接続シナリオを優雅に処理

### 2.4. DDD統合システムアーキテクチャ

```mermaid
classDiagram
    class AbstractUseCase {
        <<abstract>>
        +ExecuteAsync(TSchema, CancellationToken): Task~TResponse~
    }

    class CompileUseCase {
        -compilationStateService: CompilationStateValidationService
        -executionService: CompilationExecutionService
        +ExecuteAsync(CompileSchema, CancellationToken): Task~CompileResponse~
    }

    class McpServerInitializationUseCase {
        -configService: McpServerConfigurationService
        -portService: PortAllocationService
        -startupService: McpServerStartupService
        +ExecuteAsync(ServerInitializationSchema, CancellationToken): Task~ServerInitializationResponse~
    }

    class CompilationStateValidationService {
        +ValidateCompilationState(): ServiceResult~ValidationResult~
    }

    class CompilationExecutionService {
        +ExecuteCompilationAsync(bool): Task~ServiceResult~CompilationResult~~
    }

    class IUnityTool {
        <<interface>>
        +ToolName: string
        +Description: string
        +ParameterSchema: object
        +ExecuteAsync(JToken): Task~object~
    }

    class CompileTool {
        -useCase: CompileUseCase
        +ExecuteAsync(CompileSchema): Task~CompileResponse~
    }

    class McpToolAttribute {
        <<attribute>>
        +Description: string
        +DisplayDevelopmentOnly: bool
        +RequiredSecuritySetting: SecuritySettings
    }

    AbstractUseCase <|-- CompileUseCase : extends
    AbstractUseCase <|-- McpServerInitializationUseCase : extends
    CompileUseCase --> CompilationStateValidationService : uses
    CompileUseCase --> CompilationExecutionService : uses
    McpServerInitializationUseCase --> CompilationStateValidationService : uses
    IUnityTool <|.. CompileTool : implements
    CompileTool --> CompileUseCase : delegates to
    CompileTool ..|> McpToolAttribute : uses
```

### 2.5. UI用MVP + Helperアーキテクチャ

```mermaid
classDiagram
    class McpEditorWindow {
        <<Presenter>>
        -model: McpEditorModel
        -view: McpEditorWindowView
        -eventHandler: McpEditorWindowEventHandler
        -serverOperations: McpServerOperations
        +OnEnable()
        +OnGUI()
        +OnDisable()
    }

    class McpEditorModel {
        <<Model>>
        -serverPort: int
        -isServerRunning: bool
        -selectedEditor: EditorType
        +LoadState()
        +SaveState()
        +UpdateServerStatus()
    }

    class McpEditorWindowView {
        <<View>>
        +DrawServerSection(ViewData)
        +DrawConfigSection(ViewData)
        +DrawDeveloperTools(ViewData)
    }

    class McpEditorWindowViewData {
        <<DTO>>
        +ServerPort: int
        +IsServerRunning: bool
        +SelectedEditor: EditorType
    }

    class McpEditorWindowEventHandler {
        <<Helper>>
        +HandleEditorUpdate()
        +HandleServerEvents()
        +HandleLogUpdates()
    }

    class McpServerOperations {
        <<Helper>>
        +StartServer()
        +StopServer()
        +ValidateServerConfig()
    }

    McpEditorWindow --> McpEditorModel : 状態管理
    McpEditorWindow --> McpEditorWindowView : レンダリング委譲
    McpEditorWindow --> McpEditorWindowEventHandler : イベント委譲
    McpEditorWindow --> McpServerOperations : 操作委譲
    McpEditorWindowView --> McpEditorWindowViewData : 受信
    McpEditorModel --> McpEditorWindowViewData : 作成
```

### 2.6. スキーマ駆動型・型安全通信
手動でエラーの起こりやすいJSON解析を避けるため、システムはコマンドにスキーマ駆動アプローチを使用します。

- **`*Schema.cs`ファイル**（例：`CompileSchema.cs`、`GetLogsSchema.cs`）: 単純なC#プロパティを使用してコマンドの期待パラメータを定義。`[Description]`などの属性とデフォルト値を使用してクライアント用のJSONスキーマを自動生成
- **`*Response.cs`ファイル**（例：`CompileResponse.cs`）: クライアントに返されるデータの構造を定義
- **`CommandParameterSchemaGenerator.cs`**: この汎用ユーティリティは`*Schema.cs`ファイルのリフレクションを使用してパラメータスキーマを動的に生成し、C#コードが単一の真実の源であることを保証

この設計により、サーバーとクライアント間の不整合を排除し、C#コード内で強力な型安全性を提供します。

### 2.7. SOLID原則
- **単一責任原則（SRP）**: 各クラスは明確に定義された責任を持ちます。
    - `McpBridgeServer`: 生のTCP通信を処理
    - `McpServerController`: サーバーのライフサイクルとドメインリロード間の状態を管理
    - `McpEditorSettings`: Editor Window設定の永続化を処理
    - `ToolSkillSynchronizer`: スキルインストールファイルの更新を処理
    - `JsonRpcProcessor`: JSON-RPC 2.0メッセージの解析と整形のみを扱う
    - **UIレイヤーの例**:
        - `McpEditorModel`: アプリケーション状態とビジネスロジックのみを管理
        - `McpEditorWindowView`: UIレンダリングのみを処理
        - `McpEditorWindowEventHandler`: Unity Editorイベントのみを管理
        - `McpServerOperations`: サーバー操作のみを処理
- **開放閉鎖原則（OCP）**: システムは拡張に対して開かれていますが、変更に対して閉じています。コマンドパターンが主要例です。新しいコマンドをコア実行ロジックを変更することなく追加できます。MVP + Helperパターンもこの原則を実証しています - 既存のコンポーネントを変更することなく新しいヘルパークラスを作成することで新機能を追加できます。

### 2.8. UI用MVP + Helperパターン
UIレイヤーは、1247行のモノリシッククラスから構造化された保守可能なアーキテクチャに進化した洗練された**MVP（Model-View-Presenter） + Helperパターン**を実装しています。

#### パターンコンポーネント
- **Model（`McpEditorModel`）**: すべてのアプリケーション状態、設定データ、ビジネスロジックを含む。カプセル化を維持しながら状態更新のメソッドを提供。UnityのSuryaStateとEditorPrefsを通じて永続化を処理
- **View（`McpEditorWindowView`）**: ビジネスロジックのない純粋なUIレンダリングコンポーネント。`McpEditorWindowViewData`転送オブジェクトを通じてすべての必要なデータを受信
- **Presenter（`McpEditorWindow`）**: ModelとViewを調整し、Unity固有のライフサイクルイベントを処理し、複雑な操作を専門ヘルパークラスに委譲
- **Helperクラス**: 機能の特定の側面を処理する専門コンポーネント:
  - イベント管理（`McpEditorWindowEventHandler`）
  - サーバー操作（`McpServerOperations`）
  - スキルインストールサービス（`ToolSkillSynchronizer`）

#### このアーキテクチャの利点
1. **関心の分離**: 各コンポーネントが単一の明確な責任を持つ
2. **テスト可能性**: HelperクラスはUnity Editorコンテキストから独立してユニットテスト可能
3. **保守性**: 複雑なロジックが管理可能で焦点の絞られたコンポーネントに分解
4. **拡張性**: 既存のコードを変更することなく新しいヘルパークラスで新機能を追加可能
5. **認知負荷の軽減**: 開発者は一度に機能の一側面に集中可能

#### 実装ガイドライン
- **状態管理**: すべての状態変更はModelレイヤーを通じて行う
- **UI更新**: Viewは転送オブジェクトを通じてデータを受信し、Modelに直接アクセスしない
- **複雑な操作**: Presenterで実装するのではなく適切なヘルパークラスに委譲
- **イベント処理**: すべてのUnity Editorイベント管理を専用EventHandlerに分離

### 2.9. ドメインリロードへの耐性（UseCase統合）
Unity Editorの重要な課題は、アプリケーションの状態をリセットする「ドメインリロード」です。DDD統合アーキテクチャではこれをUseCaseレベルで優雅に処理します：

#### Domain Reload Recovery UseCase
- **`DomainReloadRecoveryUseCase`**: ドメインリロードのワークフロー全体を統制
- **`DomainReloadDetectionService`**: ドメインリロードの検出と状態判定
- **`SessionRecoveryService`**: セッション状態の保存と復元

#### McpServerController統合
- **`McpServerController`**: `[InitializeOnLoad]`を使用してEditorライフサイクルイベントにフック
- **UseCase呼び出し**: `OnBeforeAssemblyReload`と`OnAfterAssemblyReload`でUseCaseを実行
- **`AssemblyReloadEvents`**: リロード前後の処理をUseCaseに委譲
- **`SessionState`**: ドメインリロード間でのデータ永続化（UseCaseが管理）

#### 統制されたワークフロー
1. **リロード前**: `DomainReloadRecoveryUseCase.ExecuteBeforeDomainReload()`でサーバー状態を保存
2. **リロード後**: `DomainReloadRecoveryUseCase.ExecuteAfterDomainReloadAsync()`で状態を復元

このUseCase統合により、ドメインリロード処理が単一のビジネスワークフローとして管理され、保守性と信頼性が向上しています。

## 3. 実装されたUseCaseとツール

システムは現在、**Domain-Driven Design**アーキテクチャに基づく**UseCase + ツールパターン**で実装された12の本番対応機能を提供しています。各機能はUseCaseでビジネスワークフローを統制し、Application Serviceで単一機能を実装し、CLI向けツールコマンドとして提供しています：

### 3.1. コアシステムUseCaseとツール
- **`PingTool`**: 接続ヘルスチェックと遅延テスト
- **`CompileUseCase` + `CompileTool`**: コンパイル状態検証と実行をApplication Serviceで分離、詳細エラーレポート付き
- **`ClearConsoleTool`**: 確認付きUnity Consoleログクリア
- **`GetCommandDetailsTool`**: ツール内省とメタデータ取得

### 3.2. 情報取得UseCaseとツール
- **`GetLogsUseCase` + `GetLogsTool`**: ログ取得とフィルタリングをApplication Serviceで分離、型選択付き
- **`GetHierarchyUseCase` + `GetHierarchyTool`**: シーン階層情報収集とコンポーネント情報付きエクスポート
- **`GetMenuItemsUseCase` + `GetMenuItemsTool`**: Unity メニューアイテム発見とメタデータ収集
- **`GetProviderDetailsUseCase` + `GetProviderDetailsTool`**: Unity Search プロバイダー情報収集

### 3.3. GameObjectとシーンUseCaseとツール
- **`FindGameObjectsUseCase` + `FindGameObjectsTool`**: 複数条件検索ロジックをUseCaseで統制、高度なGameObject検索
- **`UnitySearchUseCase` + `UnitySearchTool`**: Unity Search APIを統合したアセット、シーン、プロジェクトリソースの統合検索

### 3.4. 実行UseCaseとツール
- **`RunTestsUseCase` + `RunTestsTool`**: テストフィルタ作成と実行をApplication Serviceで分離、NUnit XMLエクスポート付き（セキュリティ制御）
- **`ExecuteMenuItemUseCase` + `ExecuteMenuItemTool`**: メニューアイテム検索と実行をUseCaseで統制、リフレクション経由実行（セキュリティ制御）

### 3.5. セキュリティ制御UseCaseとツール
いくつかのUseCaseとツールはセキュリティ制限の対象であり、設定により無効化可能：
- **テスト実行**: `RunTestsUseCase`/`RunTestsTool`は「テスト実行有効化」設定が必要
- **メニューアイテム実行**: `ExecuteMenuItemUseCase`/`ExecuteMenuItemTool`は「メニューアイテム実行許可」設定が必要
- **未知のツール**: 明示的に設定されない限りデフォルトでブロック

### 3.6. サーバーライフサイクルUseCase
- **`McpServerInitializationUseCase`**: サーバー初期化の複雑なワークフローを統制
- **`McpServerShutdownUseCase`**: サーバー終了の適切な処理を管理
- **`DomainReloadRecoveryUseCase`**: ドメインリロード前後の状態管理を完全に統制

これらのUseCaseはCLI向けツールコマンドとして直接公開されず、`McpServerController`の内部で呼び出されてシステムのライフサイクルを管理しています。

## 4. 主要コンポーネント（ディレクトリ別）

### `/Server`
このディレクトリにはコアネットワーキングとライフサイクル管理コンポーネントが含まれています。
- **`McpBridgeServer.cs`**: 低レベルTCPサーバー。指定ポートをリッスンし、クライアント接続を受け入れ、ネットワークストリーム上でContent-Lengthフレーミングを使用したJSONデータの読み書きを処理。バックグラウンドスレッドで動作
- **`FrameParser.cs`**: Content-Lengthヘッダー解析と検証の専門クラス。フレーム整合性検証とJSONコンテンツ抽出を処理
- **`DynamicBufferManager.cs`**: 動的バッファプール管理クラス。メモリ効率とバッファ再利用を実現。最大1MBまでの動的バッファリングをサポート
- **`MessageReassembler.cs`**: TCP断片化メッセージ再組み立てクラス。部分的に受信されたフレームを適切に処理し、完全なメッセージを抽出
- **`McpServerController.cs`**: サーバーの高レベル静的マネージャー。`McpBridgeServer`インスタンスのライフサイクル（開始、停止、再起動）を制御。ドメインリロード間の状態管理の中心点
- **`McpServerConfig.cs`**: サーバー設定の定数を保持する静的クラス（例：デフォルトポート、バッファサイズ）

### `/Security`
コマンド実行制御のセキュリティインフラストラクチャを含みます。
- **`McpSecurityChecker.cs`**: コマンド実行の権限チェックを実装する中央セキュリティ検証コンポーネント。セキュリティ属性と設定を評価してコマンドの実行許可を決定

### `/Api`
これがコマンド処理ロジックの中心です。
- **`/Commands`**: サポートされているすべてのコマンドの実装を含む
    - **`/Core`**: コマンドシステムの基盤クラス
        - **`IUnityCommand.cs`**: `CommandName`、`Description`、`ParameterSchema`、`ExecuteAsync`メソッドを含むすべてのコマンドの契約を定義
        - **`AbstractUnityCommand.cs`**: パラメータのデシリアライゼーションとレスポンス作成の定型文を処理してコマンド作成を簡素化する汎用基底クラス
        - **`UnityCommandRegistry.cs`**: `[McpTool]`属性を持つすべてのクラスを発見し、コマンド名をその実装にマッピングする辞書に登録
        - **`McpToolAttribute.cs`**: クラスをコマンドとして自動登録にマークするために使用される単純な属性
    - **コマンド固有フォルダー**: 実装された各コマンドにはそれぞれ以下を含むフォルダーがあります:
        - `*Command.cs`: メインコマンド実装
        - `*Schema.cs`: 型安全パラメータ定義
        - `*Response.cs`: 構造化レスポンス形式
        - コマンドには以下が含まれます: `/Compile`、`/RunTests`、`/GetLogs`、`/Ping`、`/ClearConsole`、`/FindGameObjects`、`/GetHierarchy`、`/GetMenuItems`、`/ExecuteMenuItem`、`/UnitySearch`、`/GetProviderDetails`、`/GetCommandDetails`
- **`JsonRpcProcessor.cs`**: 受信JSON文字列を`JsonRpcRequest`オブジェクトに解析し、レスポンスオブジェクトをJSON文字列にシリアライズしてJSON-RPC 2.0仕様に準拠
- **`UnityApiHandler.cs`**: API呼び出しのエントリーポイント。`JsonRpcProcessor`からメソッド名とパラメータを受け取り、`UnityCommandRegistry`を使用して適切なコマンドを実行。権限検証のために`McpSecurityChecker`と統合

### `/Core`
セッションと状態管理のコアインフラストラクチャコンポーネントを含みます。

#### セッション管理
- **`McpSessionManager.cs`**: サーバーセッションメタデータを維持し、ドメインリロードを乗り越える`ScriptableSingleton`として実装されたシングルトンセッションマネージャー

### `/UI`
**MVP（Model-View-Presenter） + Helperパターン**を使用して実装されたユーザー向けEditorWindowのコードを含みます。

#### コアMVPコンポーネント
- **`McpEditorWindow.cs`**: **Presenter**レイヤー（503行）。ModelとViewの調整役として機能し、Unity固有のライフサイクルイベントとユーザーインタラクションを処理。複雑な操作を専門ヘルパークラスに委譲
- **`McpEditorModel.cs`**: **Model**レイヤー（470行）。すべてのアプリケーション状態、永続化、ビジネスロジックを管理。UI状態、サーバー設定を含み、適切なカプセル化で状態更新のメソッドを提供
- **`McpEditorWindowView.cs`**: **View**レイヤー。純粋なUIレンダリングロジックを処理し、ビジネスロジックから完全に分離。`McpEditorWindowViewData`を通じてデータを受信してインターフェースをレンダリング
- **`McpEditorWindowViewData.cs`**: ModelからViewにすべての必要な情報を運ぶデータ転送オブジェクト、関心の清潔な分離を保証

#### 専門ヘルパークラス
- **`McpEditorWindowEventHandler.cs`**: Unity Editorイベントを管理。`EditorApplication.update`、`McpCommunicationLogger.OnLogUpdated`、サーバーライフサイクルイベント、状態変更検出を処理。イベント管理ロジックをメインウィンドウから完全に分離
- **`McpServerOperations.cs`**: 複雑なサーバー操作を処理（131行）。サーバー検証、開始、停止ロジックを含む。包括的エラー処理でユーザーインタラクティブと内部操作モードの両方をサポート
- **`McpCommunicationLog.cs`**: ウィンドウの「開発者ツール」セクションに表示されるリクエストとレスポンスのインメモリと`SessionState`バック付きログを管理

#### アーキテクチャの利点
このMVP + Helperパターンが提供するもの：
- **単一責任**: 各クラスが一つの明確で焦点の絞られた責任を持つ
- **テスト可能性**: HelperクラスはUnity Editorコンテキストから独立してテスト可能
- **保守性**: 複雑なロジックが専門化された管理可能なコンポーネントに分離
- **拡張性**: 既存のコードを変更することなく新しいヘルパークラスで新機能を追加可能
- **複雑性の軽減**: メインPresenterが適切な責任分散により1247行から503行（59%削減）に

### `/Config`
Unity CLI LoopのEditor設定、ツールアクセス設定、スキルインストール、プロジェクトパス解決を管理します。
- **`McpEditorSettings.cs`**: Editor Windowの状態とセットアップ設定を永続化
- **`ULoopSettings.cs`**: CLI関連のセットアップ設定とセキュリティ隣接設定を永続化
- **`ToolSettings.cs`**: セキュリティ層で使うツールごとのアクセス設定を保存
- **`ToolSkillSynchronizer.cs`**: 生成されたスキルファイルを対応AIツールのディレクトリにインストール
- **`UnityMcpPathResolver.cs`**: Unityプロジェクトルートとパッケージパスを解決。クラス名は歴史的なもの

### `/Tools`
コアUnity Editor機能をラップする高レベルユーティリティを含みます。
- **`/ConsoleUtility` & `/ConsoleLogFetcher`**: 主に`ConsoleLogRetriever`のクラスセットで、リフレクションを使用してUnityの内部コンソールログエントリにアクセス。これにより`getlogs`コマンドが特定の型とフィルターでログを取得可能
- **`/TestRunner`**: Unity テストを実行するロジックを含む
    - **`PlayModeTestExecuter.cs`**: PlayModeテストの実行の複雑さを処理する重要クラス。`async`タスクが正常に完了できるようにドメインリロードを無効化（`DomainReloadDisableScope`）を含む
    - **`NUnitXmlResultExporter.cs`**: テスト結果をNUnit互換XMLファイルに整形
- **`/Util`**: 汎用ユーティリティ
    - **`CompileController.cs`**: `CompilationPipeline` APIをラップしてプロジェクトコンパイルの単純な`async`インターフェースを提供

### `/Utils`
低レベル汎用ヘルパークラスを含みます。
- **`MainThreadSwitcher.cs`**: バックグラウンドスレッド（TCPサーバーなど）からUnityのメインスレッドに実行を切り替える`awaitable`オブジェクトを提供する重要ユーティリティ。ほとんどのUnity APIはメインスレッドからのみ呼び出し可能なため不可欠
- **`EditorDelay.cs`**: フレームベース遅延のカスタム`async/await`互換実装。特にドメインリロード後にEditorが安定状態に達するのを数フレーム待つのに有用
- **`VibeLogger.cs`**: Unity CLI Loop診断向けのAIフレンドリーな構造化ロガー

## 5. 主要ワークフロー

### 5.1. セキュリティ付きUseCase + ツール実行フロー

```mermaid
sequenceDiagram
    box CLI クライアント
    participant CLI as uloop CLI
    end
    
    box Unity TCP サーバー
    participant MB as McpBridgeServer<br/>McpBridgeServer.cs
    participant JP as JsonRpcProcessor<br/>JsonRpcProcessor.cs
    participant UA as UnityApiHandler<br/>UnityApiHandler.cs
    participant SC as McpSecurityChecker<br/>McpSecurityChecker.cs
    participant UR as UnityToolRegistry<br/>UnityToolRegistry.cs
    participant AT as AbstractUnityTool<br/>AbstractUnityTool.cs
    participant Tool as Concrete Tool<br/>*Tool.cs
    participant UC as UseCase<br/>*UseCase.cs
    participant AS as Application Service<br/>*Service.cs
    end

    CLI->>MB: JSON文字列
    MB->>JP: ProcessRequest(json)
    JP->>JP: JsonRpcRequestにデシリアライズ
    JP->>UA: ExecuteToolAsync(name, params)
    UA->>SC: ValidateTool(name, params)
    alt セキュリティチェック通過
        SC-->>UA: 検証成功
        UA->>UR: GetTool(name)
        UR-->>UA: IUnityToolインスタンス
        UA->>AT: ExecuteAsync(JToken)
        AT->>AT: Schemaにデシリアライズ
        AT->>Tool: ExecuteAsync(Schema)
        Tool->>UC: ExecuteAsync(Schema, CancellationToken)
        UC->>AS: 各Application Service呼び出し
        AS->>AS: 単一機能実行
        AS-->>UC: ServiceResult<T>
        UC-->>Tool: UseCase Response
        Tool-->>AT: Tool Response
        AT-->>UA: レスポンス
    else セキュリティチェック失敗
        SC-->>UA: 検証失敗
        UA-->>UA: エラーレスポンス作成
    end
    UA-->>JP: レスポンス
    JP->>JP: JSONにシリアライズ
    JP-->>MB: JSONレスポンス
    MB-->>CLI: レスポンス送信
```

### 5.2. UIインタラクションフロー（MVP + Helperパターン）
1.  **ユーザーインタラクション**: ユーザーがUnity Editorウィンドウとインタラクション（ボタンクリック、フィールド変更など）
2.  **Presenter処理**: `McpEditorWindow`（Presenter）がUnity Editorイベントを受信
3.  **状態更新**: Presenterが`McpEditorModel`で適切なメソッドを呼び出してアプリケーション状態を更新
4.  **複雑な操作**: 複雑な操作（サーバー開始/停止、検証）については、Presenterが専門ヘルパークラスに委譲:
    - サーバー関連操作は`McpServerOperations`
    - イベント管理は`McpEditorWindowEventHandler`
    - スキルインストール操作は`ToolSkillSynchronizer`
5.  **ビューデータ準備**: Model状態が`McpEditorWindowViewData`転送オブジェクトにパッケージ化
6.  **UIレンダリング**: `McpEditorWindowView`が転送オブジェクトを受信してインターフェースをレンダリング
7.  **イベント伝播**: `McpEditorWindowEventHandler`がUnity Editorイベントを管理してModelを相応に更新
8.  **永続化**: ModelがUnityの`SessionState`と`EditorPrefs`を通じて状態永続化を自動処理

このワークフローにより、アプリケーションライフサイクル全体で応答性と適切な状態管理を維持しながら関心の清潔な分離を保証します。

### 5.3. セキュリティ検証フロー

```mermaid
sequenceDiagram
    box Unity TCP サーバー
    participant UA as UnityApiHandler<br/>UnityApiHandler.cs
    participant SC as McpSecurityChecker<br/>McpSecurityChecker.cs
    participant Settings as セキュリティ設定
    participant Command as コマンドインスタンス<br/>*Command.cs
    end
    
    UA->>SC: ValidateCommand(commandName)
    SC->>Settings: セキュリティポリシーチェック
    alt コマンドがセキュリティ制御対象
        Settings-->>SC: セキュリティ状態
        alt セキュリティ無効
            SC-->>UA: 検証失敗
        else セキュリティ有効
            SC-->>UA: 検証成功
        end
    else コマンドがセキュリティ制御対象外
        SC-->>UA: 検証成功
    end
    UA->>Command: 実行（検証済みの場合）
```
