# ツールリファレンス

[English](tools.md) | 日本語

Unity CLI Loop に内蔵されている各ツールの詳細な説明です。全体像や設計思想は [README](../../../README_ja.md) を参照してください。

## 自律開発ループ系ツール

### 1. compile - コンパイルの実行
AssetDatabase.Refresh()をした後、Domain Reload完了まで待ってコンパイル結果を返却します。内蔵のLinterでは発見できないエラー・警告を見つける事ができます。
差分コンパイルと強制全体コンパイルを選択できます。
即時に戻したい場合だけ `--no-wait-for-domain-reload` を指定します。
```text
→ compile実行、エラー・警告内容を解析
→ 該当ファイルを自動修正
→ 再度compileで確認
```

### 2. get-logs - UnityのConsoleと同じ内容のLogを取得します
LogTypeや検索対象の文字列で絞り込む事ができます。また、stacktraceの有無も選択できます。
これにより、コンテキストを小さく保ちながらlogを取得できます。
**MaxCountの動作**: 最新のログから指定数を取得します（tail的な動作）。MaxCount=10なら最新の10件のログを返します。
**高度な検索機能**:
- **正規表現サポート**: `UseRegex: true`で強力なパターンマッチングが可能
- **スタックトレース検索**: `SearchInStackTrace: true`でスタックトレース内も検索対象
```text
→ get-logs (LogType: Error, SearchText: "NullReference", MaxCount: 10)
→ get-logs (LogType: All, SearchText: "(?i).*error.*", UseRegex: true, MaxCount: 20)
→ get-logs (LogType: All, SearchText: "MyClass", SearchInStackTrace: true, MaxCount: 50)
→ スタックトレースから原因箇所を特定、該当コードを修正
```

### 3. run-tests - TestRunnerの実行 (PlayMode, EditMode対応)
Unity Test Runnerを実行し、テスト結果を取得します。FilterTypeとFilterValueで条件を設定できます。
- FilterType: all（全テスト）、exact（個別テストメソッド名）、regex（クラス名や名前空間）、assembly（アセンブリ名）
- FilterValue: フィルタータイプに応じた値（クラス名、名前空間など）
- SaveBeforeRun: デフォルトで未保存のロード済みScene変更と現在のPrefab Stage変更を保存してからテストを実行。無効にした場合、未保存のエディタ変更は破棄されず、テスト実行前に停止します。
テスト結果をxmlで出力する事が可能です。出力pathを返すので、それをAIに読み取ってもらう事ができます。
これもコンテキストを圧迫しないための工夫です。
```text
→ run-tests (FilterType: exact, FilterValue: "PlayerControllerTests.TestJump")
→ run-tests (SaveBeforeRun: false、未保存のエディタ変更があれば停止)
→ 失敗したテストを確認、実装を修正してテストをパス
```
> [!WARNING]
> PlayModeテスト実行の際、Domain Reloadは強制的にOFFにされます。(テスト終了後に元の設定に戻ります)
> この際、Static変数がリセットされない事に注意して下さい。

### 4. pause-point - コードを書き換えずに任意の行で止めて変数を見る
ソースを編集することも再コンパイルすることもなく、任意の `file:line` でPlayModeを停止します。コンパイル済みのメソッドを直接パッチするため、PlayMode実行中に仕掛けることもできます。

ヒット時のレスポンスには `CapturedVariables` が含まれます。これは対象行が実行される**直前**に取得した、メソッドのローカル変数・引数・`this` のインスタンスフィールドで、IDEのブレークポイントとまったく同じタイミングです。値はライブ参照ではなくその時点の文字列として記録されるため、Unityが再開した後も証拠として有効です。`Debug.Log` を仕込んでコンパイルし直す往復が不要になります。

3つのキャプチャモードがあります。`single-shot`（デフォルト）は最初のヒットで自動解除、`continuous` はヒットのたびに停止して履歴を保持、`trace` は停止せずヒットだけを記録します。watch式（`enable-watch` / `get-watch-values`）を使うと、停止中のStepごとに値が自動で再評価されるため、フレーム単位の変化を追えます。

> [!NOTE]
> EditorのCode OptimizationモードはDebugである必要があります（Releaseの場合は対処方法を示して拒否されます）。また、コンパイルやドメインリロードが起きるとパッチは解除されるので、その後は仕掛け直してください。

```text
→ enable-pause-point (File: "Assets/Scripts/Enemy.cs", Line: 42, Await: true,
                      Trigger: "simulate-keyboard --action Press --key Space")
→ CapturedVariablesから、その瞬間のローカル変数・引数・フィールドを読み取る
→ 原因を特定して修正
```

## Unity Editor 自動化・探索ツール

### 5. clear-console - ログのクリーンアップ
log検索時、ノイズのとなるlogをクリアする事ができます。
```text
→ clear-console
→ 新しいデバッグセッションを開始
```

### 6. find-game-objects - シーン内オブジェクト検索
オブジェクトを取得し、コンポーネントのパラメータを調べます。また、Unity Editorで選択中のGameObject（複数可）の情報も取得できます。
```text
→ find-game-objects (RequiredComponents: ["Camera"])
→ Cameraコンポーネントのパラメータを調査

→ find-game-objects (SearchMode: "Selected")
→ Unity Editorで選択中のGameObjectの詳細情報を取得（複数選択対応）
```

### 7. get-hierarchy - シーン構造の解析
現在アクティブなHierarchyの情報をネストされたJSON形式で取得します。ランタイムでも動作します。
**自動ファイル出力**: 取得したHierarchyは常に`{project_root}/.uloop/outputs/HierarchyResults/`ディレクトリにJSONとして保存されます。レスポンスにはファイルパスのみが返るため、大量データでもトークン消費を最小限に抑えられます。
**選択モード**: `uloop get-hierarchy --use-selection` を指定すると、Unity Editorで選択中のGameObjectから階層を取得できます。複数選択にも対応 - 親子両方が選択されている場合、重複を避けるため親のみがルートとして使用されます。
```text
→ GameObject間の親子関係を理解。構造的な問題を発見・修正
→ シーンの規模にかかわらず、Hierarchyデータはファイルに保存され、生のJSONの代わりにパスが返されます
→ uloop get-hierarchy --use-selection
→ パスを手動で指定せずに、選択中のGameObjectの階層を取得
```

### 8. focus-window - Unity Editorウィンドウを前面化（macOS / Windows対応）
macOS / Windows Editor上で、Unity Editor ウィンドウを最前面に表示させます。
他アプリにフォーカスが奪われた後でも、視覚的なフィードバックをすぐ確認できます。（Linuxは未対応）

### 9. screenshot - EditorWindowのスクリーンショット
任意のEditorWindowのスクリーンショットをPNGとして保存します。ウィンドウ名（タイトルバーに表示されている文字列）を指定してキャプチャできます。
同じ種類のウィンドウが複数開いている場合（例：Inspectorを3つ開いている場合）、すべてのウィンドウを連番で保存します。
3つのマッチングモードをサポート: `exact`（デフォルト）、`prefix`、`contains` - すべて大文字小文字を区別しません。

`CaptureMode: rendering` を指定すると、EditorWindowの見た目ではなくGame Viewのレンダリング結果を直接キャプチャします。PlayMode中のゲーム画面を、Editorのウィンドウ枠やスケーリングの影響を受けずに取得したい場合に使います。
`AnnotateRaycastGrid: true` を併用すると、キャプチャ画像に座標グリッドが重ねて描画されます。画像を見たAIが `simulate-mouse-input` に渡す座標を決めやすくなります。

`uloop set-game-view-size --width 1920 --height 1080` でGame Viewのカスタム解像度を固定できます。`CaptureMode: rendering` の座標系を実行ごとに安定させたいときに使ってください（引数なしで実行すると現在の解像度を取得できます）。
```text
→ screenshot (WindowName: "Console")
→ Console画面の状態をPNGで保存
→ AIに視覚的なフィードバックを提供
```

### 10. control-play-mode - Play Modeの制御
Unity EditorのPlay Modeを制御します。Play（再生開始/一時停止解除）、Stop（停止）、Pause（一時停止）の3つのアクションを実行できます。
```text
→ control-play-mode (Action: Play)
→ Play Modeを開始してゲームの動作を確認
→ control-play-mode (Action: Pause)
→ 一時停止して状態を確認
```

### 11. execute-dynamic-code - 動的C#コード実行
Unity Editor内で動的にC#コードを実行します。

**Async対応**:
- スニペット内で await が利用可能です（Task / ValueTask / UniTask など awaitable 全般）
- CancellationToken をツールに渡すと、キャンセルが末端まで伝播します

有効化されている場合、動的コード実行はUnity Editorプロセスの権限で実行され、Unity API、.NET API、プロジェクトのアセンブリを利用できます。AIエージェントに任意のC#コードを実行させたくない場合は、Tool Settingsのトグルでこのツールを無効化してください。
```text
→ execute-dynamic-code (Code: "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); return \"Cube created\";")
→ プロトタイプの迅速な検証、バッチ処理の自動化
→ 信頼できる自動化向けにUnity Editor APIへフルアクセス
```

## PlayMode 自動テスト系ツール

### 12. simulate-mouse-ui - PlayMode UI要素のマウス操作シミュレーション
PlayMode中のUI要素に対してマウスクリック・長押し・ドラッグをシミュレーションします。EventSystemとExecuteEventsを使ってポインタイベントを直接ディスパッチするため、旧Input System・新Input Systemの両方に依存せず動作します。ゲームロジックがInput Systemを直接読み取る場合（例：`Mouse.current.leftButton.wasPressedThisFrame`）は、`simulate-mouse-input` を使用してください。

6つのアクションに対応: Click、LongPress、Drag（ワンショット）、DragStart/DragMove/DragEnd（分割ドラッグ）

```text
→ screenshot (CaptureMode: rendering, AnnotateElements: true)
→ AnnotatedElementsから要素の座標（SimX/SimY）を取得
→ simulate-mouse-ui (Action: Click, X: 400, Y: 300)
→ simulate-mouse-ui (Action: LongPress, X: 400, Y: 300, Duration: 5.0)
→ simulate-mouse-ui (Action: Drag, FromX: 100, FromY: 500, X: 400, Y: 300)
→ simulate-mouse-ui (Action: DragStart, X: 100, Y: 500)
→ simulate-mouse-ui (Action: DragMove, X: 200, Y: 400, DragSpeed: 300)
→ simulate-mouse-ui (Action: DragEnd, X: 400, Y: 300)
```
https://github.com/user-attachments/assets/c7ee9103-c282-4f90-8b01-64bb17400f3e

### 13. simulate-mouse-input - Input System経由のPlayModeマウス入力シミュレーション
Input System経由でPlayMode中のマウス入力をシミュレーションします。ボタンクリック、マウスデルタ、スクロールホイールを`Mouse.current`に直接注入します。EventSystemのポインタイベントを発火する`simulate-mouse-ui`と異なり、`Mouse.current`を直接読み取るゲームロジック向けのツールです。このツールは Input System パッケージ導入時のみ利用可能で、Player SettingsのActive Input Handlingを`Input System Package (New)`または`Both`に設定する必要があります。

5つのアクションに対応: Click、LongPress、MoveDelta、SmoothDelta、Scroll

`--dry-run` を付けると、マウス入力を注入する代わりに、その座標が3D物理で何にヒットするか（GameObjectの名前とパス、レイヤー、距離、ヒット位置・法線）だけを返します。スクリーンショットで決めた座標にクリックを送る前の確認に使います。dry-runはEditModeでも動作し、Input Systemパッケージも不要です。

```text
→ simulate-mouse-input (DryRun: true, X: 400, Y: 300)
→ simulate-mouse-input (Action: Click, X: 400, Y: 300)
→ simulate-mouse-input (Action: Click, X: 400, Y: 300, Button: Right)
→ simulate-mouse-input (Action: LongPress, X: 400, Y: 300, Duration: 2.0)
→ simulate-mouse-input (Action: MoveDelta, DeltaX: 100, DeltaY: 0)
→ simulate-mouse-input (Action: Scroll, ScrollY: 120)
→ simulate-mouse-input (Action: SmoothDelta, DeltaX: 300, DeltaY: 0, Duration: 0.5)
```

### 14. simulate-keyboard - PlayModeでのキーボード入力シミュレーション
Input System経由でPlayMode中のキーボード入力をシミュレーションします。単発のキータップ、長押し、複数キーの同時押し（例：Shift+Wでスプリント）に対応しています。このツールは Input System パッケージ導入時のみ利用可能で、Player SettingsのActive Input Handlingを `Input System Package (New)` または `Both` に設定する必要があります。ゲームコードがInput System API（例: `Keyboard.current[Key.W].isPressed`）で入力を読み取っている必要があり、レガシーの `Input.GetKey()` には対応していません。

3つのアクションに対応: Press（ワンショットタップまたは時間指定ホールド）、KeyDown（キーを押し続ける）、KeyUp（押下中のキーを解放）。`Keyboard.current.spaceKey.wasPressedThisFrame` のような立ち上がり検出には Press を使います。KeyDown は最初の押下エッジを1回だけ発行し、その後は押下状態を保つだけなので、意図的にキーを保持したい場合だけ KeyDown/KeyUp を使います。

```text
→ simulate-keyboard (Action: Press, Key: Space)
→ simulate-keyboard (Action: Press, Key: W, Duration: 2.0)
→ simulate-keyboard (Action: KeyDown, Key: LeftShift)
→ simulate-keyboard (Action: KeyDown, Key: W)
→ screenshot (CaptureMode: rendering)
→ simulate-keyboard (Action: KeyUp, Key: W)
→ simulate-keyboard (Action: KeyUp, Key: LeftShift)
```

### 15. replay-input - 記録された入力のPlayMode再生
記録されたキーボード・マウス入力をPlayMode中に再生します。JSON記録を読み込み、Input System経由でフレーム単位で入力を注入します。ループ再生と進捗モニタリングに対応しています。このツールは Input System パッケージ導入時のみ利用可能です。記録ファイルは、まず Unity Editor の **Window > Unity CLI Loop > Recordings** で **Start Recording** と **Stop Recording** を使って作成します。CLI に記録コマンドはありません。

```text
→ replay-input (Action: Start)
→ replay-input (Action: Start, InputPath: "scripts/my-play.json", Loop: true)
→ replay-input (Action: Status)
→ replay-input (Action: Stop)
```

terminal から uloop コマンドを実行するE2Eは、shell 系統ごとに1つのrunnerから実行します:

```bash
sh scripts/run-posix-e2e.sh --project-path /path/to/unity-project
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-windows-e2e.ps1
```

`run-posix-e2e.sh` は、デフォルトでチェックイン済みのネイティブCLIバイナリを使い、すべての `uloop` 呼び出しに明示的な `--project-path` を渡します。CLI recovery/readiness と simulate-mouse UI を1つの流れで検証します。Recordingsウィンドウで作成したJSONの replay-input 検証は `verify-replay-via-cli.sh` を個別に実行します。
