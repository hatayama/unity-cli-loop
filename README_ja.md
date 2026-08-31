# Unity CLI Loop

[English](README.md) | 日本語

[![Unity](https://img.shields.io/badge/Unity-2022.3+-red.svg)](https://unity3d.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.md)<br>
![ClaudeCode](https://img.shields.io/badge/Claude_Code-555?logo=claude)
![Cursor](https://img.shields.io/badge/Cursor-111?logo=Cursor)
![Codex](https://img.shields.io/badge/Codex-111?logo=data:image/svg+xml;base64,PHN2ZyByb2xlPSJpbWciIHZpZXdCb3g9IjAgMCAyNCAyNCIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSJ3aGl0ZSIgZD0iTTIyLjI4MTkgOS44MjExYTUuOTg0NyA1Ljk4NDcgMCAwIDAtLjUxNTctNC45MTA4IDYuMDQ2MiA2LjA0NjIgMCAwIDAtNi41MDk4LTIuOUE2LjA2NTEgNi4wNjUxIDAgMCAwIDQuOTgwNyA0LjE4MThhNS45ODQ3IDUuOTg0NyAwIDAgMC0zLjk5NzcgMi45IDYuMDQ2MiA2LjA0NjIgMCAwIDAgLjc0MjcgNy4wOTY2IDUuOTggNS45OCAwIDAgMCAuNTExIDQuOTEwNyA2LjA1MSA2LjA1MSAwIDAgMCA2LjUxNDYgMi45MDAxQTUuOTg0NyA1Ljk4NDcgMCAwIDAgMTMuMjU5OSAyNGE2LjA1NTcgNi4wNTU3IDAgMCAwIDUuNzcxOC00LjIwNTggNS45ODk0IDUuOTg5NCAwIDAgMCAzLjk5NzctMi45MDAxIDYuMDU1NyA2LjA1NTcgMCAwIDAtLjc0NzUtNy4wNzI5em0tOS4wMjIgMTIuNjA4MWE0LjQ3NTUgNC40NzU1IDAgMCAxLTIuODc2NC0xLjA0MDhsLjE0MTktLjA4MDQgNC43NzgzLTIuNzU4MmEuNzk0OC43OTQ4IDAgMCAwIC4zOTI3LS42ODEzdi02LjczNjlsMi4wMiAxLjE2ODZhLjA3MS4wNzEgMCAwIDEgLjAzOC4wNTJ2NS41ODI2YTQuNTA0IDQuNTA0IDAgMCAxLTQuNDk0NSA0LjQ5NDR6bS05LjY2MDctNC4xMjU0YTQuNDcwOCA0LjQ3MDggMCAwIDEtLjUzNDYtMy4wMTM3bC4xNDIuMDg1MiA0Ljc4MyAyLjc1ODJhLjc3MTIuNzcxMiAwIDAgMCAuNzgwNiAwbDUuODQyOC0zLjM2ODV2Mi4zMzI0YS4wODA0LjA4MDQgMCAwIDEtLjAzMzIuMDYxNUw5Ljc0IDE5Ljk1MDJhNC40OTkyIDQuNDk5MiAwIDAgMS02LjE0MDgtMS42NDY0ek0yLjM0MDggNy44OTU2YTQuNDg1IDQuNDg1IDAgMCAxIDIuMzY1NS0xLjk3MjhWMTEuNmEuNzY2NC43NjY0IDAgMCAwIC4zODc5LjY3NjVsNS44MTQ0IDMuMzU0My0yLjAyMDEgMS4xNjg1YS4wNzU3LjA3NTcgMCAwIDEtLjA3MSAwbC00LjgzMDMtMi43ODY1QTQuNTA0IDQuNTA0IDAgMCAxIDIuMzQwOCA3Ljg3MnptMTYuNTk2MyAzLjg1NThMMTMuMTAzOCA4LjM2NCAxNS4xMTkyIDcuMmEuMDc1Ny4wNzU3IDAgMCAxIC4wNzEgMGw0LjgzMDMgMi43OTEzYTQuNDk0NCA0LjQ5NDQgMCAwIDEtLjY3NjUgOC4xMDQydi01LjY3NzJhLjc5Ljc5IDAgMCAwLS40MDctLjY2N3ptMi4wMTA3LTMuMDIzMWwtLjE0Mi0uMDg1Mi00Ljc3MzUtMi43ODE4YS43NzU5Ljc3NTkgMCAwIDAtLjc4NTQgMEw5LjQwOSA5LjIyOTdWNi44OTc0YS4wNjYyLjA2NjIgMCAwIDEgLjAyODQtLjA2MTVsNC44MzAzLTIuNzg2NmE0LjQ5OTIgNC40OTkyIDAgMCAxIDYuNjgwMiA0LjY2ek04LjMwNjUgMTIuODYzbC0yLjAyLTEuMTYzOGEuMDgwNC4wODA0IDAgMCAxLS4wMzgtLjA1NjdWNi4wNzQyYTQuNDk5MiA0LjQ5OTIgMCAwIDEgNy4zNzU3LTMuNDUzN2wtLjE0Mi4wODA1TDguNzA0IDUuNDU5YS43OTQ4Ljc5NDggMCAwIDAtLjM5MjcuNjgxM3ptMS4wOTc2LTIuMzY1NGwyLjYwMi0xLjQ5OTggMi42MDY5IDEuNDk5OHYyLjk5OTRsLTIuNTk3NCAxLjQ5OTctMi42MDY3LTEuNDk5N1oiLz48L3N2Zz4=)
![Antigravity](https://img.shields.io/badge/Antigravity-111?logo=google)
![GitHubCopilot](https://img.shields.io/badge/GitHub_Copilot-111?logo=githubcopilot)

<p align="center">
    <img height="450" alt="logo-black-bg" src="Packages/src/Documentation~/images/logo.png" /><br>
    <sub>(Logo inspired by Daft Punk's <i>Human After All</i> album art)</sub>  
</p>
  

CLIを通じて、AIエージェントがUnityプロジェクトのコンパイル・テスト・操作を各種LLMツールから実行できるようにします。

AI駆動の開発ループを既存のUnityプロジェクト内で自律的に回し続けるために設計されています。

> [!IMPORTANT]
> - **[V3の新機能](Packages/src/Documentation~/whats-new-v3_ja.md)** — Node.jsのセットアップとポート管理の廃止、`hot-reload` / `pause-point` の追加、CLIの自動アップデートとプロジェクトごとの自動バージョン使い分け、接続の安定性の向上
> - **[カスタムツール／スキルのV3移行ガイド](Packages/src/Documentation~/migration-v2-to-v3_ja.md)** — C#カスタムツールや、`uloop` コマンドを呼び出すスキル／スクリプトを自作している方向け。それ以外の方は、パッケージとCLIを更新するだけで移行できます

# コンセプト
Unity CLI Loopは、「AIがUnityプロジェクトの実装をできるだけ人手を介さずに進められる」ことを目指して作られた Unity連携ツールです。
人間が手で行っていたコンパイル、Test Runner の実行、ログ確認、シーン編集、画面キャプチャによるUIレイアウト確認、さらには実装した機能が本当に動くかどうかを実際に操作して確かめる動作確認までを、LLM ツールからまとめて行えるようにします。

Unity CLI Loopのコアとなるコンセプトは次の4つです。

1. **AIが自律的にビルド・テスト・ログ解析・修正を回し続ける「自律開発ループ」** — コードを書き換えずに任意の行で実行を止め、その瞬間の変数を読み取って原因を特定することもできます。メソッド本体の修正は再コンパイルを待たずに実行中のゲームへ即時反映できます。`compile`, `run-tests`, `get-logs`, `clear-console`, `pause-point`, `hot-reload`
2. **シーン構築、オブジェクト操作、メニュー実行、スクリーンショットからのUI改善など、Unity Editorの操作をAIに委任** — `execute-dynamic-code`, `screenshot`
3. **PlayMode中の自動テスト — ボタンクリック、ドラッグ、キーボード入力、記録済み入力の再生、ゲーム動作の検証をAIが実行** — `simulate-mouse-ui`, `simulate-mouse-input`, `simulate-keyboard`, `replay-input`, `execute-dynamic-code`, `screenshot`
4. **上記を最小限のツール数で実現する** → [設計思想](#設計思想)

https://github.com/user-attachments/assets/569a2110-7351-4cf3-8281-3a83fe181817

# クイックスタート

このガイドでは、CLI、Unity パッケージ、スキルの 3 つをインストールし、LLM ツールから Unity を操作できる状態にします。ターミナルから入れる方法と Unity の UI から入れる方法があり、どちらか一方で完了します。

## 始める前に

以下を確認してください：

- Unity 2022.3 以降のプロジェクトがある
- Claude Code、Codex など、スキルを読み込める LLM ツールを使っている

> [!NOTE]
> **V2 からアップグレードする場合**: V2 API で作ったカスタムツールがあるプロジェクトでは、V3 パッケージを入れた直後にコンパイルエラーが出ます。これは想定内の挙動です。手で直さず、Unity 起動時の Safe Mode の問い合わせで `Ignore` を選び、自動で開く移行ウィンドウ（`Window > Unity CLI Loop > Custom Tool Migration`）で **Migrate** を押してください。手順は [カスタムツール／スキルの V3 移行ガイド](Packages/src/Documentation~/migration-v2-to-v3_ja.md) を参照してください。

## ターミナルから入れる場合

### ステップ 1：CLI をインストールする

**macOS、Windows Git Bash:**

```sh
curl -fsSL https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.sh | sh
```

**Windows PowerShell:**

```powershell
irm https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1 | iex
```

**Homebrew（macOS）:**

```bash
brew install hatayama/tap/uloop
```

### ステップ 2：Unity パッケージをインストールする

Unity プロジェクトのルートで実行します：

```bash
uloop package install
```

OpenUPM の scoped registry と `io.github.hatayama.uloopmcp` の依存を `Packages/manifest.json` に追加します。バージョンを固定するには `--version <x.y.z>` を付けます。

### ステップ 3：スキルをインストールする

使っている LLM ツールに合わせて、Unity プロジェクトのルートで実行します：

```bash
# Claude Code
uloop skills install --claude

# Codex など .agents/skills を読むツール
uloop skills install --agents

# プロジェクトではなくグローバルにインストール
uloop skills install --claude --global

# 任意のディレクトリ（外部のスキルパッケージストアなど）へ
uloop skills install --output-dir path/to/skills
```

### ステップ 4：動作を確認する

Unity でプロジェクトを開いた状態で、プロジェクトのルートで実行します：

```bash
uloop -v
```

CLI のバージョンに続いて、このプロジェクトが使う project runner のバージョンが表示されれば完了です：

```text
3.0.1
This Unity project pins uloop project runner 3.0.0.
```

## Unity の UI から入れる場合

### ステップ 1：Unity パッケージをインストールする

`Window > Package Manager` の「+」から **Add package from git URL** を選び、次の URL を入力します：

```text
https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
```

<details>
<summary>OpenUPM の scoped registry から入れる場合</summary>

1. `Project Settings > Package Manager` の Scoped Registries に以下を追加します：
```text
Name: OpenUPM
URL: https://package.openupm.com
Scope(s): io.github.hatayama.uloopmcp
```
2. `Window > Package Manager` で My Registries の OpenUPM を選び、Unity CLI Loop をインストールします。

</details>

### ステップ 2：CLI をインストールする

`Window > Unity CLI Loop > Settings` を開き、**Install CLI** を押します：

<img width="350" alt="CLI未インストール状態のSettingsウィンドウ。Install CLIボタンが表示されている" src="Packages/src/Documentation~/images/settings-cli-not-installed.png" />

ボタンが消えて CLI のバージョンが表示されれば完了です：

<img width="350" alt="CLI検出に成功したSettingsウィンドウ。緑のインジケータとCLIバージョンが表示されている" src="Packages/src/Documentation~/images/settings-cli-installed.png" />

### ステップ 3：スキルをインストールする

同じ Settings ウィンドウで、Claude Code、Codex など対象を選び、**Install Skills** を押します：

<img width="350" alt="SettingsウィンドウのSkillsセクション。対象を選択してInstall Skillsボタンが押せる状態" src="Packages/src/Documentation~/images/settings-skills-install.png" />

<details>
<summary>V2 プロジェクトと併用する場合</summary>

v2とv3のプロジェクトを併用するときも、v3 dispatcherをインストールしたままにしてください。Unityがプロジェクトをv2系の`io.github.hatayama.uloopmcp` packageへ解決している場合、dispatcherは同じバージョンのv2 `uloop-cli` releaseをバージョン別user cacheへ自動的にインストールし、コマンドを委譲します。解決済みpackageのバージョンは、downgrade後に残った古いv3 project-runner pinより優先されます。初回のnpmインストールとv2モードの注記はstderrへ出力されるため、stdoutには委譲先コマンドの出力だけが残ります。v3プロジェクトはpinで選ばれたproject runnerを引き続き使用します。

グローバルな`install`、`update`、`uninstall`、`launch`コマンドは、どのプロジェクトでもv3 dispatcherが処理します。検出されたv2プロジェクトでは、それ以外のプロジェクトコマンド、help、プロジェクトスコープのversion表示が委譲されます。

v2への委譲には、初回コマンドでcacheを作成するnpmを含むNode.js 22以降が必要です。v2プロジェクトのSettingsウィンドウでは、**Update CLI**または**Downgrade CLI**を押さないでください。委譲先CLIが同じv2バージョンを返すため通常はボタン自体が表示されませんが、使用するとグローバルnpm版CLIが復活し、PATHの順序によってv3 dispatcherが隠れる可能性があります。

同じ理由で、v2 CLIをグローバルにnpmインストールし直さないでください。インストーラは古いnpm版 `uloop-cli` を自動で削除しようとし、できない場合は手動で実行するコマンドを表示します。terminalの `uloop` が古いnpm版を指している場合は `npm uninstall -g uloop-cli` を実行してから、上記のインストーラをもう一度実行してください。

</details>

これで完了です！Skillsをインストールすると、LLMツールが以下のような指示に自動で対応できるようになります：

| あなたの指示 | LLMツールが使うSkill |
|---|---|
| 「このプロジェクトのUnityを起動して」 | `/uloop-launch` |
| 「コンパイルエラーを直して」 | `/uloop-compile` |
| 「この修正をコンパイルせずに今すぐ反映して」 | `/uloop-hot-reload` |
| 「テストを実行して失敗原因を教えて」 | `/uloop-run-tests` + `/uloop-get-logs` |
| 「シーンの階層構造を確認して」 | `/uloop-get-hierarchy` |
| 「Unityを再生させて、Unityを前面に出して」 | `/uloop-control-play-mode` + `/uloop-focus-window` |
| 「Prefabのパラメータを一括修正して」 | `/uloop-execute-dynamic-code` |
| 「Game Viewのスクショを撮って、UIレイアウトを調整して」 | `/uloop-screenshot` + `/uloop-execute-dynamic-code` |
| 「記録した入力を再生して」 | `/uloop-replay-input` |
| 「バグの原因をこの行で止めて調べて」 | `/uloop-pause-point` |


<details>
<summary>バンドルされている全18個のSkills一覧</summary>

- `/uloop-launch` - 正しいバージョンでUnityを起動
- `/uloop-compile` - コンパイルの実行
- `/uloop-get-logs` - Consoleログの取得
- `/uloop-run-tests` - テストの実行
- `/uloop-hot-reload` - メソッド本体の変更を再コンパイルなしで実行中のコードへ即時適用
- `/uloop-clear-console` - Consoleのクリア
- `/uloop-focus-window` - Unity Editorを前面に表示
- `/uloop-get-hierarchy` - シーン階層の取得
- `/uloop-find-game-objects` - GameObject検索
- `/uloop-screenshot` - EditorWindowのキャプチャ
- `/uloop-pause-point` - 任意の行で実行を止めて変数をキャプチャ
- `/uloop-set-game-view-size` - Game Viewのカスタム解像度の取得・設定
- `/uloop-simulate-mouse-ui` - PlayMode UI要素のクリック・長押し・ドラッグシミュレーション
- `/uloop-simulate-mouse-input` - Input System経由のPlayModeマウス入力シミュレーション
- `/uloop-simulate-keyboard` - Input System経由のPlayModeキーボード入力シミュレーション
- `/uloop-replay-input` - Recordingsウィンドウで記録した入力のPlayMode再生
- `/uloop-control-play-mode` - Play Modeの制御
- `/uloop-execute-dynamic-code` - 動的C#コード実行

</details>

<details>
<summary>CLIの直接利用（上級者向け）</summary>

Skillsを使わずにCLIを直接呼び出すこともできます：

```bash
# 利用可能なツール一覧を取得
uloop list

# 正しいバージョンでUnityプロジェクトを起動
uloop launch

# ビルドターゲットを指定して起動（Android, iOS, StandaloneOSX など）
uloop launch -p Android

# 実行中のUnityを終了して再起動
uloop launch -r

# コンパイルを実行
uloop compile

# Domain Reloadを待たずにコンパイルを開始
uloop compile --no-wait-for-domain-reload

# 変更した.csのメソッド本体を再コンパイルなしで実行中のコードへ適用
uloop hot-reload

# ログを取得
uloop get-logs --max-count 10

# テストを実行
uloop run-tests --filter-type all

# 動的コードを実行
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("Hello from CLI!");'
```

</details>

## Claude Codeを使う場合の設定

Claude Codeはシェルコマンドをサンドボックス内で実行し、サンドボックスは通信をデフォルトで遮断します。UnityとのIPC接続も対象になるため、`uloop` はUnityが正常に動いていても、接続しようとした時点で `UNITY_NOT_REACHABLE`（`connect: operation not permitted`）で失敗します。

`~/.claude/settings.json` の `sandbox.excludedCommands` に `uloop *` を追加して、`uloop` コマンドをサンドボックスの対象外にしてください。

```json
{
  "sandbox": {
    "excludedCommands": ["uloop *"]
  }
}
```

このパターンは**入力されたコマンド文字列**に対して照合されるため、`uloop` で始まる呼び出しが対象外になります。詳細と検証結果は [docs/claude-code-sandbox.md](docs/claude-code-sandbox.md) を参照してください。

# 仕組み

`uloop` コマンドがUnity Editorに届くまでの流れは次のとおりです。

- **グローバル `uloop` dispatcher** — PATH上に1つだけ置かれる入口。コマンドを解釈し、対象プロジェクト用のrunnerへ委譲します
- **`uloop-project-runner`** — プロジェクトごとのrunner。使用するバージョンは各プロジェクトの `.uloop/project-runner-pin.json`（pin）で決まり、バージョン別のユーザーキャッシュへ自動的にダウンロードされます。そのため、異なるバージョンの複数プロジェクトを1台のマシンで共存させられます → [project runner pinの詳細](docs/project-runner-pin.md)
- **Unity Editor内のIPCサーバー** — runnerからの接続を受け取り、Unity APIを実行して結果を返します

接続には**TCPポートを使いません**。macOS/LinuxではUnixドメインソケット、Windowsでは名前付きパイプで接続するため、ポートの設定も、他のEditorインスタンスとのポート衝突もありません。

# 設計思想

Unity CLI Loop はツールの数を追い求めません。C#コードの動的実行（`execute-dynamic-code`）があれば、Unity Editor上のほとんどの操作はそれ一つで実現できます。

ツールを増やしすぎると、AIがどのツールを使うべきか適切に判断できなくなります。さらに、たとえSkill化したとしても各ツールのdescriptionはコンテキストウィンドウを消費します。必要最低限に絞ることがよい設計だと考えています。

専用ツールを設けているのは、フレームをまたぐ入力シミュレーションやスクリーンショット取得のように動的コード実行では原理的に対応できない操作と、compile・get-logsのように開発ループの中で繰り返し呼ばれる操作です。後者を専用ツール化することで、毎回C#コードを生成するトークンコストを削減しています。

新しい専用ツールが欲しくなったときも、その多くはSkill化で足ります。定型の操作をSKILL.mdの手順、シェルスクリプト、`execute-dynamic-code` に渡すC#スニペットとして書いておけば、AIはそれを呼び出すだけで済みます。Skillの本体は起動時にしか読み込まれず、実行のたびにコードを生成し直す必要もないため、追加のトークン消費はほぼゼロです。作り方は[カスタムツール開発ガイド](Packages/src/Documentation~/custom-tools_ja.md#カスタムツール用-skills)を参照してください。

# 主要機能

各ツールの詳しい説明と使用例は **[ツールリファレンス](Packages/src/Documentation~/tools_ja.md)** を参照してください。

## 自律開発ループ系ツール
- `compile` - コンパイルを実行し、エラー・警告を返す
- `get-logs` - Consoleと同じ内容のログを、種類や検索文字列で絞り込んで取得
- `run-tests` - Unity Test Runnerを実行（PlayMode / EditMode対応）
- `hot-reload` - メソッド本体の変更を再コンパイルなしで実行中のコードへ即時適用
- `pause-point` - コードを書き換えずに任意の行でPlayModeを止め、その瞬間の変数を読む

## Unity Editor 自動化・探索ツール
- `clear-console` - Consoleのログをクリア
- `find-game-objects` - シーン内のオブジェクトを検索し、コンポーネントを調査
- `get-hierarchy` - シーン構造をJSONで取得
- `focus-window` - Unity Editorウィンドウを前面化
- `screenshot` - EditorWindowやGame Viewのスクリーンショットを保存
- `set-game-view-size` - Game Viewのカスタム解像度を取得・設定
- `control-play-mode` - Play Modeの再生・停止・一時停止
- `execute-dynamic-code` - 動的C#コード実行

## PlayMode 自動テスト系ツール
- `simulate-mouse-ui` - UI要素へのマウス操作シミュレーション（EventSystem経由）
- `simulate-mouse-input` - Input System経由のマウス入力シミュレーション
- `simulate-keyboard` - Input System経由のキーボード入力シミュレーション
- `replay-input` - Recordingsウィンドウで記録した入力のPlayMode再生

## Unity CLI Loop 拡張ツールの開発

コアパッケージに手を入れることなく、プロジェクト固有のカスタムツールを型安全に追加できます。`Skill/SKILL.md` を添えれば、AIエージェントがカスタムツールを自動認識します。

実装手順とSkillsの書き方は **[カスタムツール開発ガイド](Packages/src/Documentation~/custom-tools_ja.md)** を参照してください。

## その他

### Unity CLI Loop 関連ファイル

`UserSettings/UnityCliLoopSettings.json` はユーザー個別のエディタ設定を保持するため、常にローカル専用です。

プロジェクトルートの `.uloop/` ディレクトリには、CLIキャッシュ、ツールレジストリ、ランタイム出力が格納されます。大半はローカル専用ですが、一部のファイルはチーム共有のためにオプションでgit管理できます。

| ファイル | 用途 | git管理 |
|---------|------|---------|
| `project-runner-pin.json` | グローバルdispatcherが使うproject runnerのバージョン契約 | Yes |
| `settings.tools.json` | ツールごとの有効・無効設定 | 任意 |
| `tools.json` | 自動生成されるCLIツールレジストリ | No |
| `outputs/` | ランタイム出力（テスト結果、スクリーンショット、ヒエラルキーダンプ） | No |

> [!TIP]
> **推奨 `.gitignore` パターン**
>
> ```gitignore
> **/.uloop/*
> !**/.uloop/project-runner-pin.json
> !**/.uloop/settings.tools.json
> ```
>
> 自動生成ファイルやランタイム出力を無視しつつ、dispatcherのpinとチーム共有の設定ファイルをgit管理できます。
> ツールの有効・無効設定を共有しない場合は、`!` の行を削除してください。

## ライセンス
MIT License
