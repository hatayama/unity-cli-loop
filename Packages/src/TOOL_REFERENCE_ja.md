[English](TOOL_REFERENCE.md)

# Unity CLI Loop ツールリファレンス

このドキュメントでは、全Unity CLI Loopツールの詳細仕様を提供します。

## 共通レスポンス形式

すべてのUnity CLI Loopツールは以下の共通要素を持ちます：

### 共通レスポンスプロパティ
すべてのツールには以下のプロパティが自動的に含まれます：
- `Ver` (string): CLIとの互換性チェック用のUnity CLI Loopパッケージバージョン

---

## Unity コアツール

### 1. compile
- **説明**: AssetDatabase.Refresh()を実行後、コンパイルを行います。詳細なタイミング情報付きでコンパイル結果を返します。
- **パラメータ**:
  - `ForceRecompile` (boolean): 強制再コンパイルを実行するかどうか（デフォルト: false）
  - `WaitForDomainReload` (boolean): Domain Reload完了まで待機するかどうか（デフォルト: false）
- **レスポンス**:
  - `Success` (boolean | null): コンパイルが成功したかどうか。ForceRecompile=true時はDomain Reload完了まで結果が取得できないためnull
  - `ErrorCount` (number | null): エラーの総数。ForceRecompile=true時はnull
  - `WarningCount` (number | null): 警告の総数。ForceRecompile=true時はnull
  - `Errors` (array | null): コンパイルエラーの配列。ForceRecompile=true時はnull
    - `Message` (string): エラーメッセージ
    - `File` (string): エラーが発生したファイルパス
    - `Line` (number): エラーが発生した行番号
  - `Warnings` (array | null): コンパイル警告の配列。ForceRecompile=true時はnull
    - `Message` (string): 警告メッセージ
    - `File` (string): 警告が発生したファイルパス
    - `Line` (number): 警告が発生した行番号
  - `Message` (string): 追加情報のためのオプションメッセージ
  - `ProjectRoot` (string): Unityプロジェクトのルートパス。WaitForDomainReload=true時のみセットされる

### 2. get-logs
- **説明**: フィルタリングおよび高度な検索機能付きでUnityコンソールからログ情報を取得します
- **パラメータ**:
  - `LogType` (enum): フィルタするログタイプ - "Error", "Warning", "Log", "All"（デフォルト: "All"）
  - `MaxCount` (number): 取得するログの最大数（デフォルト: 100）
  - `SearchText` (string): ログメッセージ内で検索するテキスト（空の場合はすべて取得）（デフォルト: ""）
  - `UseRegex` (boolean): 検索に正規表現を使用するかどうか（デフォルト: false）
  - `SearchInStackTrace` (boolean): スタックトレース内も検索対象に含めるかどうか（デフォルト: false）
  - `IncludeStackTrace` (boolean): スタックトレースを表示するかどうか（デフォルト: false）
- **レスポンス**:
  - `TotalCount` (number): 利用可能なログの総数
  - `DisplayedCount` (number): このレスポンスで表示されるログの数
  - `LogType` (string): 使用されたログタイプフィルタ
  - `MaxCount` (number): 使用された最大数制限
  - `SearchText` (string): 使用された検索テキストフィルタ
  - `IncludeStackTrace` (boolean): スタックトレースが含まれているかどうか
  - `Logs` (array): ログエントリの配列
    - `Type` (string): ログタイプ（Error, Warning, Log）
    - `Message` (string): ログメッセージ
    - `StackTrace` (string): スタックトレース（IncludeStackTraceがtrueの場合）

### 3. run-tests
- **説明**: Unity Test Runnerを実行し、包括的なレポート付きでテスト結果を取得します
- **パラメータ**:
  - `FilterType` (enum): テストフィルタのタイプ - "all"(0), "exact"(1), "regex"(2), "assembly"(3)（デフォルト: "all"）
  - `FilterValue` (string): フィルタ値（FilterTypeがall以外の場合に指定）（デフォルト: ""）
    - `exact`: 個別テストメソッド名（完全一致）（例：io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs）
    - `regex`: クラス名または名前空間（正規表現パターン）（例：io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests, io.github.hatayama.UnityCliLoop）
    - `assembly`: アセンブリ名（例：uLoopMCP.Tests.Editor）
  - `TestMode` (enum): テストモード - "EditMode"(0), "PlayMode"(1)（デフォルト: "EditMode"）
    - **PlayMode注意**: PlayModeテスト実行時は、一時的にdomain reloadが無効化されます
  - `SaveBeforeRun` (boolean): 未保存のロード済みScene変更と現在のPrefab Stage変更を保存してからテストを実行（デフォルト: false）
- **レスポンス**:
  - `Success` (boolean): テスト実行が成功したかどうか
  - `Message` (string): テスト実行メッセージ
  - `CompletedAt` (string): テスト実行完了タイムスタンプ（ISO形式）
  - `TestCount` (number): 実行されたテストの総数
  - `PassedCount` (number): 合格したテストの数
  - `FailedCount` (number): 失敗したテストの数
  - `SkippedCount` (number): スキップされたテストの数
  - `XmlPath` (string): NUnit XML結果ファイルのパス（テスト失敗時に自動保存）。XMLファイルは `{project_root}/.uloop/outputs/TestResults/` フォルダに保存されます

### 4. clear-console
- **説明**: クリーンな開発ワークフローのためにUnityコンソールログをクリアします
- **パラメータ**:
  - `AddConfirmationMessage` (boolean): クリア後に確認ログメッセージを追加するかどうか（デフォルト: false）
- **レスポンス**:
  - `Success` (boolean): コンソールクリア操作が成功したかどうか
  - `ClearedLogCount` (number): コンソールからクリアされたログの数
  - `ClearedCounts` (object): タイプ別のクリアされたログの内訳
    - `ErrorCount` (number): クリアされたエラーログの数
    - `WarningCount` (number): クリアされた警告ログの数
    - `LogCount` (number): クリアされた情報ログの数
  - `Message` (string): クリア操作結果を説明するメッセージ
  - `ErrorMessage` (string): 操作が失敗した場合のエラーメッセージ

### 5. find-game-objects
- **説明**: 高度な検索条件（コンポーネントタイプ、タグ、レイヤーなど）で複数のGameObjectを検索します
- **パラメータ**:
  - `NamePattern` (string): 検索するGameObject名のパターン（デフォルト: ""）
  - `SearchMode` (enum): 検索モード - "Exact", "Path", "Regex", "Contains", "Selected"（デフォルト: "Exact"）
  - `RequiredComponents` (array): GameObjectが持つ必要のあるコンポーネントタイプ名の配列（デフォルト: []）
  - `Tag` (string): タグフィルター（デフォルト: ""）
  - `Layer` (number): レイヤーフィルター（デフォルト: null）
  - `IncludeInactive` (boolean): 非アクティブなGameObjectを含めるかどうか（デフォルト: false）
  - `MaxResults` (number): 返す結果の最大数（デフォルト: 20）
  - `IncludeInheritedProperties` (boolean): 継承プロパティを含めるかどうか（デフォルト: false）
- **レスポンス**:
  - `results` (array): 見つかったGameObjectの配列
    - `name` (string): GameObject名
    - `path` (string): 完全な階層パス
    - `isActive` (boolean): GameObjectがアクティブかどうか
    - `tag` (string): GameObjectタグ
    - `layer` (number): GameObjectレイヤー
    - `components` (array): GameObject上のコンポーネントの配列
      - `type` (string): コンポーネントタイプ名
      - `fullTypeName` (string): 完全なアセンブリ修飾タイプ名
      - `properties` (array): コンポーネントプロパティ（IncludeInheritedPropertiesがtrueの場合）
  - `totalFound` (number): 見つかったGameObjectの総数
  - `errorMessage` (string): 検索が失敗した場合のエラーメッセージ
  - `resultsFilePath` (string): 結果がファイルにエクスポートされた場合のファイルパス
  - `message` (string): 操作メッセージ
  - `processingErrors` (array): シリアライズに失敗したオブジェクトの配列
    - `gameObjectName` (string): 失敗したGameObjectの名前
    - `gameObjectPath` (string): 失敗したGameObjectのパス
    - `error` (string): エラーの説明

---

## Unity 検索・発見ツール

### 6. get-hierarchy
- **説明**: Unity階層構造をネストされたJSON形式で取得します
- **パラメータ**:
  - `IncludeInactive` (boolean): 階層結果に非アクティブなGameObjectを含めるかどうか（デフォルト: true）
  - `MaxDepth` (number): 階層を探索する最大深度（無制限深度の場合は-1）（デフォルト: -1）
  - `RootPath` (string): 階層探索を開始するルートGameObjectパス（すべてのルートオブジェクトの場合は空/null）（デフォルト: null）
  - `IncludeComponents` (boolean): 階層内の各GameObjectのコンポーネント情報を含めるかどうか（デフォルト: true）
  - `IncludePaths` (boolean): ノードのパス情報を含めるかどうか（デフォルト: false）
  - `UseComponentsLut` (string): コンポーネント用LUTの使用 - "auto", "true", "false"（デフォルト: "auto"）
  - `UseSelection` (boolean): 現在選択中のGameObjectをルートとして使用するかどうか。trueの場合、RootPathは無視されます（デフォルト: false）
- **レスポンス**:
  - `message` (string): クライアントがJSONファイルを参照するための案内メッセージ
  - `hierarchyFilePath` (string): 階層データが保存されたファイルパス（例: "{project_root}/.uloop/outputs/HierarchyResults/hierarchy_2025-07-10_21-30-15.json"）。エクスポートされたJSONファイルには `hierarchy`（GameObjectのネスト配列）と `context`（シーン情報、ノード数、最大深度）が含まれる

### 7. execute-dynamic-code
- **説明**: Unity Editor内で動的C#コードを実行します。セキュリティレベルに応じてAPI利用を制限し、using文の自動処理やエラーメッセージの改善機能を提供します
- **パラメータ**:
  - `Code` (string): 実行するC#コード（デフォルト: ""）
  - `Parameters` (Dictionary<string, object>): 実行時パラメータ（デフォルト: {}）
  - `CompileOnly` (boolean): コンパイルのみ実行（実行はしない）（デフォルト: false）
- **レスポンス**:
  - `Success` (boolean): 実行が成功したかどうか
  - `Result` (string): 実行結果
  - `Logs` (array): ログメッセージの配列
  - `CompilationErrors` (array): コンパイルエラーの配列（存在する場合）
    - `Message` (string): エラーメッセージ
    - `Line` (number): エラーが発生した行番号
    - `Column` (number): エラーが発生した列番号
    - `ErrorCode` (string): コンパイラーエラーコード（CS0103など）
    - `Hint` (string): エラー解決のためのオプションヒント
    - `Suggestions` (array of string): 修正候補（例: usingの追加や完全修飾名の使用）
    - `Context` (string): エラー周辺のコード行とキャレットポインタ
    - `PointerColumn` (number): キャレット描画用のポインタカラム（1-based）
  - `ErrorMessage` (string): エラーメッセージ（失敗時）
  - `SecurityLevel` (string): 現在のセキュリティレベル（"Disabled", "Restricted", "FullAccess"）
  - `UpdatedCode` (string): 更新されたコード（修正適用後）
  - `DiagnosticsSummary` (string): 診断情報のサマリー（ユニークエラー数、総数、最初のエラー概要）
  - `Diagnostics` (array): リッチクライアント向けの構造化された診断情報（CompilationErrorsと同じ構造）

### 8. focus-window
- **説明**: macOSおよびWindowsでUnity Editorウィンドウを前面に表示します
- **パラメータ**: なし
- **レスポンス**:
  - `Success` (boolean): 操作が成功したかどうか
  - `Message` (string): 操作結果メッセージ
  - `ErrorMessage` (string): 操作が失敗した場合のエラーメッセージ

### 9. screenshot
- **説明**: Unity EditorWindowのスクリーンショットを撮影してPNG画像として保存します。名前による柔軟なマッチングモードで任意のEditorWindowをキャプチャ可能です
- **パラメータ**:
  - `WindowName` (string): キャプチャするウィンドウ名（例: "Game", "Scene", "Console", "Inspector", "Project", "Hierarchy"）（デフォルト: "Game"）
  - `ResolutionScale` (number): キャプチャ画像の解像度スケール、0.1〜1.0（デフォルト: 1）
  - `MatchMode` (enum): ウィンドウ名のマッチングモード（すべて大文字小文字を区別しない） - "exact", "prefix", "contains"（デフォルト: "exact"）
    - `exact`: ウィンドウ名が完全に一致する必要があります
    - `prefix`: ウィンドウ名が入力で始まる必要があります
    - `contains`: ウィンドウ名に入力が含まれている必要があります
  - `OutputDirectory` (string): スクリーンショット保存先のディレクトリパス。空の場合はデフォルトパス（.uloop/outputs/Screenshots/）を使用。絶対パスも指定可能（デフォルト: ""）
- **レスポンス**:
  - `ScreenshotCount` (number): キャプチャされたウィンドウの数
  - `Screenshots` (array): スクリーンショット情報の配列
    - `ImagePath` (string): 保存されたPNGファイルの絶対パス
    - `FileSizeBytes` (number): 保存されたファイルのサイズ（バイト）
    - `Width` (number): キャプチャ画像の幅（ピクセル）
    - `Height` (number): キャプチャ画像の高さ（ピクセル）

### 10. control-play-mode
- **説明**: Unity Editorのプレイモードを制御します（再生/停止/一時停止）
- **パラメータ**:
  - `Action` (enum): 実行するアクション - "Play", "Stop", "Pause"（デフォルト: "Play"）
    - `Play`: プレイモードを開始（一時停止からの再開も含む）
    - `Stop`: プレイモードを終了し、エディットモードに戻る
    - `Pause`: プレイモードのまま一時停止する
- **レスポンス**:
  - `IsPlaying` (boolean): Unityが現在プレイモードかどうか
  - `IsPaused` (boolean): プレイモードが一時停止中かどうか
  - `Message` (string): 実行されたアクションの説明

### 11. simulate-mouse-ui
- **説明**: PlayMode中のUI要素に対してマウスクリック・長押し・ドラッグをシミュレーション。EventSystemとExecuteEventsを使ってポインタイベントを直接ディスパッチするため、旧・新Input Systemの両方に依存せず動作。Input Systemを読むゲームロジック（`Mouse.current.leftButton.wasPressedThisFrame`等）には `simulate-mouse-input` を使用
- **パラメータ**:
  - `Action` (enum): マウスアクション - "Click", "Drag", "DragStart", "DragMove", "DragEnd", "LongPress"（デフォルト: "Click"）
    - `Click`: (X, Y)でクリック。PointerDown → PointerUp → PointerClick を発火
    - `LongPress`: (X, Y)でDuration秒間押し続けてからリリース。PointerClickは発火しない
    - `Drag`: (FromX, FromY)から(X, Y)へのワンショットドラッグ
    - `DragStart`: (X, Y)でドラッグを開始しホールド
    - `DragMove`: 現在位置から(X, Y)へ指定速度でアニメーション移動
    - `DragEnd`: (X, Y)へ移動後、ドラッグをリリース
  - `X` (number): ターゲットX座標（スクリーンピクセル、原点: 左上）（デフォルト: 0）
  - `Y` (number): ターゲットY座標（スクリーンピクセル、原点: 左上）（デフォルト: 0）
  - `FromX` (number): Dragアクションの開始X座標（デフォルト: 0）
  - `FromY` (number): Dragアクションの開始Y座標（デフォルト: 0）
  - `DragSpeed` (number): ドラッグ速度（ピクセル/秒）、0で即時移動（デフォルト: 2000）
  - `Duration` (number): LongPressアクションのホールド秒数（デフォルト: 0.5）
  - `Button` (enum): マウスボタン - "Left", "Right", "Middle"（デフォルト: "Left"）
- **レスポンス**:
  - `Success` (boolean): アクションが正常に完了したかどうか
  - `Message` (string): 実行されたアクションの説明
  - `Action` (string): 実行されたアクション
  - `HitGameObjectName` (string): ヒットしたUI要素の名前（ヒットなしの場合null）
  - `PositionX` (number): 使用されたX座標
  - `PositionY` (number): 使用されたY座標
  - `EndPositionX` (number): 終了X座標（Dragアクション用）
  - `EndPositionY` (number): 終了Y座標（Dragアクション用）

### 12. simulate-mouse-input
- **説明**: Input System経由でPlayMode中のマウス入力をシミュレーション。ボタンクリック、マウスデルタ、スクロールホイールを `Mouse.current` に直接注入。`wasPressedThisFrame`や`Mouse.current.delta`等を読むゲームロジック向け。Input Systemパッケージ導入時のみ利用可能で、Player SettingsのActive Input Handlingを `Input System Package (New)` または `Both` に設定する必要がある。IPointerClickHandler等のUI要素には `simulate-mouse-ui` を使用
- **パラメータ**:
  - `Action` (enum): マウス入力アクション - "Click", "LongPress", "MoveDelta", "SmoothDelta", "Scroll"（デフォルト: "Click"）
    - `Click`: ボタンのpress+releaseを注入し、`wasPressedThisFrame`がtrueを返すようにする
    - `LongPress`: Duration秒間ボタンをホールド
    - `MoveDelta`: マウスデルタを注入（ワンショット、FPSカメラ操作等）
    - `SmoothDelta`: Duration秒かけて滑らかにマウスデルタを注入（人間的なカメラ旋回）
    - `Scroll`: スクロールホイールを注入（ホットバー切替、ズーム等）
  - `X` (number): ターゲットX座標（スクリーンピクセル、原点: 左上）。ClickとLongPressで使用（デフォルト: 0）
  - `Y` (number): ターゲットY座標（スクリーンピクセル、原点: 左上）。ClickとLongPressで使用（デフォルト: 0）
  - `Button` (enum): マウスボタン - "Left", "Right", "Middle"（デフォルト: "Left"）。ClickとLongPressで使用
  - `Duration` (number): LongPressのホールド秒数、またはClickの最小ホールド時間。0=ワンショットタップ（デフォルト: 0）
  - `DeltaX` (number): MoveDeltaアクションのデルタX（ピクセル）。正=右（デフォルト: 0）
  - `DeltaY` (number): MoveDeltaアクションのデルタY（ピクセル）。正=上（デフォルト: 0）
  - `ScrollX` (number): Scrollアクションの水平スクロールデルタ（デフォルト: 0）
  - `ScrollY` (number): Scrollアクションの垂直スクロールデルタ。正=上、通常1ノッチ=120（デフォルト: 0）
- **レスポンス**:
  - `Success` (boolean): アクションが正常に完了したかどうか
  - `Message` (string): 実行されたアクションの説明
  - `Action` (string): 実行されたアクション
  - `Button` (string): 使用されたボタン（Click/LongPress用）
  - `PositionX` (number, nullable): 使用されたX座標（位置を使わないアクションではnull）
  - `PositionY` (number, nullable): 使用されたY座標（位置を使わないアクションではnull）

### 13. simulate-keyboard
- **説明**: Input System経由でPlayMode中のキーボード入力をシミュレーション。単発のキータップ、長押し、複数キーの同時押しに対応。Input Systemパッケージ導入時のみ利用可能で、Player SettingsのActive Input Handlingを `Input System Package (New)` または `Both` に設定する必要がある。ゲームコードがInput System API（例: `Keyboard.current[Key.W].isPressed`）で入力を読み取っている必要があり、レガシーの `Input.GetKey()` には非対応
- **パラメータ**:
  - `Action` (enum): キーボードアクション - "Press", "KeyDown", "KeyUp"（デフォルト: "Press"）
    - `Press`: ワンショットキータップ（KeyDown→KeyUp）。`Duration`で長押し時間を指定可能
    - `KeyDown`: KeyUpで明示的に解放するまでキーを押し続ける
    - `KeyUp`: KeyDownで押下中のキーを解放
  - `Key` (string): Input SystemのKey enumに対応するキー名（例: "W", "Space", "LeftShift", "Enter"）。大文字小文字を区別しない
  - `Duration` (number): Pressアクションのホールド秒数、0でワンショットタップ（デフォルト: 0）。KeyDown/KeyUpでは無視される
- **レスポンス**:
  - `Success` (boolean): アクションが正常に完了したかどうか
  - `Message` (string): 実行されたアクションの説明
  - `Action` (string): 実行されたアクション
  - `KeyName` (string, nullable): 操作対象のキー名

---

## 関連ドキュメント

- [メインREADME](README_ja.md) - プロジェクト概要とセットアップ
- [アーキテクチャドキュメント](ARCHITECTURE_ja.md) - 技術アーキテクチャの詳細
- [変更履歴](CHANGELOG.md) - バージョン履歴と更新
