[English](README.md)

# uloop-cli

[![npm version](https://img.shields.io/npm/v/uloop-cli.svg)](https://www.npmjs.com/package/uloop-cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Node.js](https://img.shields.io/badge/Node.js-22+-green.svg)](https://nodejs.org/)

**[Unity CLI Loop](https://github.com/hatayama/uLoopMCP) の CLI コンパニオン** - AIエージェントにUnityプロジェクトのコンパイル、テスト、操作を任せましょう。

> **前提条件**: このCLIを使用するには、Unityプロジェクトに [Unity CLI Loop](https://github.com/hatayama/uLoopMCP) がインストールされ、サーバーが起動している必要があります。セットアップ手順は [メインリポジトリ](https://github.com/hatayama/uLoopMCP) を参照してください。

## インストール

```bash
npm install -g uloop-cli
```

## クイックスタート

### ステップ 1: Skills のインストール

Skills を使うと、LLMツール（Claude Code、Cursor など）がUnity操作を自動的に呼び出せるようになります。

```bash
# Claude Code 用にインストール（プロジェクトレベル）
uloop skills install --claude

# OpenAI Codex 用にインストール（プロジェクトレベル）
uloop skills install --codex

# グローバルにインストールすることも可能
uloop skills install --claude --global
uloop skills install --codex --global
```

### ステップ 2: LLM ツールで使う

Skills をインストールすると、LLMツールが以下のような指示を自動的に処理できるようになります：

| あなたの指示 | 使用される Skill |
|---|---|
| 「コンパイルエラーを直して」 | `/uloop-compile` |
| 「テストを実行して、失敗の原因を教えて」 | `/uloop-run-tests` |
| 「シーンの階層構造を確認して」 | `/uloop-get-hierarchy` |

> **MCP の設定は不要です！** uLoopMCP Window でサーバーが起動していれば、LLMツールは Skills を通じてUnityと直接通信します。

## 利用可能な Skills

Skills はUnityプロジェクト内の uLoopMCP パッケージから動的に読み込まれます。以下は uLoopMCP が提供するデフォルトの Skills です：

- `/uloop-compile` - コンパイルの実行
- `/uloop-get-logs` - コンソールログの取得
- `/uloop-run-tests` - テストの実行
- `/uloop-clear-console` - コンソールのクリア
- `/uloop-focus-window` - Unity Editor を前面に表示
- `/uloop-get-hierarchy` - シーン階層の取得
- `/uloop-find-game-objects` - GameObject の検索
- `/uloop-screenshot` - EditorWindow のスクリーンショット
- `/uloop-control-play-mode` - Play Mode の制御
- `/uloop-execute-dynamic-code` - 動的 C# コードの実行
- `/uloop-launch` - Unity プロジェクトを対応する Editor バージョンで起動

プロジェクトで定義したカスタム Skills も自動的に検出されます。

## CLI コマンドリファレンス  
([English](README.md#cli-command-reference))

Skills を使わずに、CLI を直接呼び出すこともできます。

### プロジェクトパス指定

`--project-path` を省略した場合は、カレントディレクトリの Unity プロジェクトで設定されたポートが自動選択されます。

一つのLLMツールから複数のUnityインスタンスを操作したい場合、プロジェクトパスを明示的に指定します：

```bash
# プロジェクトパスで指定（絶対パス・相対パスどちらも可）
uloop compile --project-path /Users/foo/my-unity-project
uloop compile --project-path ../other-project
```

### コマンドの連携

`&&` でコマンドを連結することで、一連の操作を自動化できます：

```bash
# コンパイル → Domain Reload 待機 → 再生 → Unity にフォーカス
uloop compile --wait-for-domain-reload true && uloop control-play-mode --action Play && uloop focus-window
```

### ユーティリティコマンド

```bash
uloop list           # Unity から利用可能なツールを一覧表示
uloop sync           # Unity からツール定義をローカルキャッシュに同期
uloop completion     # シェル補完のセットアップ
```

### compile

Unity プロジェクトのコンパイルを実行します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--force-recompile` | boolean | `false` | 強制再コンパイルを実行 |
| `--wait-for-domain-reload` | boolean | `false` | Domain Reload 完了まで待機 |

```bash
uloop compile
uloop compile --force-recompile true --wait-for-domain-reload true
```

**Output:**
- `Success` (boolean) - コンパイル成功（`--force-recompile` 使用時は `null`）
- `ErrorCount` / `WarningCount` (integer) - エラー数/警告数
- `Errors` / `Warnings` (array) - `Message`, `File`, `Line` フィールドを含む問題一覧

### get-logs

Unity コンソールからログを取得します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--log-type` | enum | `All` | ログタイプフィルタ: `Error`, `Warning`, `Log`, `All` |
| `--max-count` | integer | `100` | 取得するログの最大数 |
| `--search-text` | string | | ログ内で検索するテキスト |
| `--include-stack-trace` | boolean | `false` | スタックトレースを含める |
| `--use-regex` | boolean | `false` | 正規表現で検索 |
| `--search-in-stack-trace` | boolean | `false` | スタックトレース内も検索 |

```bash
uloop get-logs
uloop get-logs --log-type Error --max-count 10
uloop get-logs --search-text "NullReference" --include-stack-trace true
```

**Output:**
- `TotalCount` (integer) - フィルタに一致するログの総数
- `DisplayedCount` (integer) - 返されたログ数
- `Logs` (array) - `Type` (Error/Warning/Log), `Message`, `StackTrace` フィールドを含むログエントリ

### run-tests

Unity Test Runner を実行します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--test-mode` | enum | `EditMode` | テストモード: `EditMode`, `PlayMode` |
| `--filter-type` | enum | `all` | フィルタタイプ: `all`, `exact`, `regex`, `assembly` |
| `--filter-value` | string | | フィルタ値（filter-type が `all` 以外の場合に使用） |

```bash
uloop run-tests
uloop run-tests --test-mode EditMode --filter-type regex --filter-value "MyTests"
uloop run-tests --filter-type assembly --filter-value "MyApp.Tests.Editor"
```

**Output:**
- `Success` (boolean) - 全テスト合格
- `TestCount` / `PassedCount` / `FailedCount` / `SkippedCount` (integer) - テスト結果の件数
- `XmlPath` (string) - NUnit XML 結果ファイルのパス（失敗時に保存）

### clear-console

Unity コンソールログをクリアします。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--add-confirmation-message` | boolean | `true` | クリア後に確認メッセージを追加 |

```bash
uloop clear-console
```

**Output:**
- `Success` (boolean) - 操作成功
- `ClearedLogCount` (integer) - クリアされたログ数

### focus-window

Unity Editor ウィンドウを前面に表示します。

パラメータなし。

```bash
uloop focus-window
```

**Output:**
- `Success` (boolean) - 操作成功
- `Message` (string) - 結果メッセージ

### get-hierarchy

Unity Hierarchy 構造を取得します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--root-path` | string | | 開始するルート GameObject パス |
| `--max-depth` | integer | `-1` | 最大深度（-1 で無制限） |
| `--include-components` | boolean | `true` | コンポーネント情報を含める |
| `--include-inactive` | boolean | `true` | 非アクティブな GameObject を含める |
| `--include-paths` | boolean | `false` | パス情報を含める |
| `--use-components-lut` | string | `auto` | コンポーネント用 LUT の使用: `auto`, `true`, `false` |
| `--use-selection` | boolean | `false` | 選択中の GameObject をルートとして使用 |

```bash
uloop get-hierarchy
uloop get-hierarchy --root-path "Canvas" --max-depth 3
uloop get-hierarchy --use-selection true
```

**Output:**
- `hierarchyFilePath` (string) - 階層データを含む JSON ファイルのパス

### find-game-objects

検索条件で GameObject を検索します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--name-pattern` | string | | 検索する名前パターン |
| `--search-mode` | enum | `Exact` | 検索モード: `Exact`, `Path`, `Regex`, `Contains`, `Selected` |
| `--required-components` | array | | 必須コンポーネントタイプ名 |
| `--tag` | string | | タグフィルター |
| `--layer` | integer | | レイヤーフィルター |
| `--max-results` | integer | `20` | 結果の最大数 |
| `--include-inactive` | boolean | `false` | 非アクティブな GameObject を含める |
| `--include-inherited-properties` | boolean | `false` | 継承プロパティを含める |

```bash
uloop find-game-objects --name-pattern "Player"
uloop find-game-objects --required-components "Camera" --include-inactive true
uloop find-game-objects --tag "Enemy" --max-results 50
```

**Output:**
- `totalFound` (integer) - 見つかった GameObject の総数
- `results` (array) - `name`, `path`, `isActive`, `tag`, `layer`, `components` フィールドを含む GameObject

### screenshot

Unity EditorWindow のスクリーンショットを撮影して PNG として保存します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--window-name` | string | `Game` | ウィンドウ名（例: "Game", "Scene", "Console", "Inspector", "Project", "Hierarchy"） |
| `--resolution-scale` | number | `1` | 解像度スケール（0.1〜1.0） |
| `--match-mode` | enum | `exact` | マッチングモード: `exact`, `prefix`, `contains`（大文字小文字区別なし） |
| `--output-directory` | string | | 出力ディレクトリパス（空の場合 .uloop/outputs/Screenshots/ を使用） |

```bash
uloop screenshot
uloop screenshot --window-name Scene
uloop screenshot --window-name Project --match-mode prefix
```

**Output:**
- `ScreenshotCount` (integer) - キャプチャされたウィンドウ数
- `Screenshots` (array) - `ImagePath`, `Width`, `Height`, `FileSizeBytes` フィールドを含むアイテム

### execute-dynamic-code

Unity Editor 内で C# コードを実行します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--code` | string | | 実行する C# コード |
| `--parameters` | object | | 実行時パラメータ |
| `--compile-only` | boolean | `false` | コンパイルのみ（実行はしない） |

```bash
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("Hello!");'
uloop execute-dynamic-code --code 'Selection.activeGameObject.name' --compile-only true
```

**Output:**
- `Success` (boolean) - 実行成功
- `Result` (string) - 実行コードの戻り値
- `Logs` (array) - 実行中に出力されたログメッセージ
- `CompilationErrors` (array) - `Message`, `Line`, `Column`, `Hint` フィールドを含むエラー

### control-play-mode

Unity Editor のプレイモードを制御します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `--action` | enum | `Play` | アクション: `Play`, `Stop`, `Pause` |

```bash
uloop control-play-mode --action Play
uloop control-play-mode --action Stop
uloop control-play-mode --action Pause
```

**Output:**
- `IsPlaying` (boolean) - プレイモード中
- `IsPaused` (boolean) - 一時停止中
- `Message` (string) - アクション説明

### launch

Unity プロジェクトを対応する Editor バージョンで起動します。

| フラグ | 型 | デフォルト | 説明 |
|--------|------|---------|-------------|
| `[project-path]` | string | | Unity プロジェクトのパス（省略時はカレントディレクトリを検索） |
| `-r, --restart` | flag | | 実行中の Unity を終了して再起動 |
| `-q, --quit` | flag | | 実行中の Unity を正常終了 |
| `-d, --delete-recovery` | flag | | 起動前に Assets/_Recovery を削除 |
| `-p, --platform <platform>` | string | | ビルドターゲット（例: Android, iOS, StandaloneOSX） |
| `--max-depth <n>` | number | `3` | project-path 省略時の検索深度（-1 で無制限） |
| `-a, --add-unity-hub` | flag | | Unity Hub に追加のみ（起動しない） |
| `-f, --favorite` | flag | | Unity Hub にお気に入りとして追加（起動しない） |

```bash
uloop launch
uloop launch /path/to/project
uloop launch -r
uloop launch -p Android
```

## シェル補完

Bash/Zsh/PowerShell のタブ補完をインストールできます：

```bash
# シェルを自動検出してインストール
uloop completion --install

# シェルを明示的に指定
uloop completion --shell bash --install        # Git Bash / MINGW64
uloop completion --shell powershell --install  # PowerShell
```

## 動作要件

- **Node.js 22.0 以降**
- **Unity 2022.3 以降**（[uLoopMCP](https://github.com/hatayama/uLoopMCP) がインストール済みであること）
- uLoopMCP サーバーが起動していること（Window > Unity CLI Loop > Start Server）

## リンク

- [uLoopMCP リポジトリ](https://github.com/hatayama/uLoopMCP) - メインパッケージとドキュメント
- [ツールリファレンス](https://github.com/hatayama/uLoopMCP/blob/main/Packages/src/TOOL_REFERENCE.md) - 詳細な API 仕様とレスポンススキーマ

## ライセンス

MIT License
