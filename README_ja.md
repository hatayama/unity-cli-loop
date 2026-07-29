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
    <img height="450" alt="logo-black-bg" src="https://github.com/user-attachments/assets/fca3047f-9042-4bf9-83bd-58b03f061082" /><br>
    <sub>(Logo inspired by Daft Punk's <i>Discovery</i> album art)</sub>  
</p>
  

CLIを通じて、AIエージェントがUnityプロジェクトのコンパイル・テスト・操作を各種LLMツールから実行できるようにします。

AI駆動の開発ループを既存のUnityプロジェクト内で自律的に回し続けるために設計されています。

> [!IMPORTANT]
> - **[V3の新機能](Packages/src/Documentation~/whats-new-v3_ja.md)** — ネイティブGo CLIへの移行、ポート管理の廃止、`pause-point` の追加など、V2からの変更点
> - **[カスタムツール／スキルのV3移行ガイド](Packages/src/Documentation~/migration-v2-to-v3_ja.md)** — C#カスタムツールや、`uloop` を呼び出す自作スキル／スクリプトを持っている人向け。それ以外の人は、パッケージとCLIを更新するだけで移行できます

# コンセプト
Unity CLI Loopは、「AIがUnityプロジェクトの実装をできるだけ人手を介さずに進められる」ことを目指して作られた Unity連携ツールです。
人間が手で行っていたコンパイル、Test Runner の実行、ログ確認、シーン編集、画面キャプチャによるUIレイアウト確認などの作業を、LLM ツールからまとめて操作できるようにします。

Unity CLI Loopのコアとなるコンセプトは次の4つです。

1. **AIが自律的にビルド・テスト・ログ解析・修正を回し続ける「自律開発ループ」** — コードを書き換えずに任意の行で実行を止め、その瞬間の変数を読み取って原因を特定することもできます。`compile`, `run-tests`, `get-logs`, `clear-console`, `pause-point`
2. **シーン構築、オブジェクト操作、メニュー実行、スクリーンショットからのUI改善など、Unity Editorの操作をAIに委任** — `execute-dynamic-code`, `screenshot`
3. **PlayMode中の自動テスト — ボタンクリック、ドラッグ、キーボード入力、入力の記録・再生、ゲーム動作の検証をAIが実行** — `simulate-mouse-ui`, `simulate-mouse-input`, `simulate-keyboard`, `record-input`, `replay-input`, `execute-dynamic-code`, `screenshot`
4. **上記を最小限のツール数で実現する** → [設計思想](#設計思想)

https://github.com/user-attachments/assets/569a2110-7351-4cf3-8281-3a83fe181817

# インストール

> [!WARNING]
> 以下のソフトウェアが必須です
>
> - **Unity 2022.3以上**
>
> CLIはネイティブバイナリで配布されるため、**Node.jsは不要です。**

ここでインストールするのはUnityパッケージです。CLI本体（ネイティブバイナリ）は、パッケージ導入後に[クイックスタートのステップ1](#ステップ1-cliのインストール)でインストールします。Unityを経由せずterminalだけでCLIを入れる方法も、同じステップに畳んで記載しています。

## Unity Package Manager経由

1. Unity Editorを開く
2. Window > Package Managerを開く
3. 「+」ボタンをクリック
4. 「Add package from git URL」を選択
5. 以下のURLを入力：
```text
https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
```

## OpenUPM経由（推奨）

## Unity Package ManagerでScoped registryを使用
1. Project Settingsウィンドウを開き、Package Managerページに移動
2. Scoped Registriesリストに以下のエントリを追加：
```text
Name: OpenUPM
URL: https://package.openupm.com
Scope(s): io.github.hatayama.uloopmcp
```

3. Package Managerウィンドウを開き、My RegistriesセクションのOpenUPMを選択。Unity CLI Loopが表示されます。

> [!NOTE]
> `com.unity.inputsystem` は optional dependency になりました。`simulate-keyboard`、`simulate-mouse-input`、`record-input`、`replay-input`、Recordings ウィンドウを使いたい場合だけ追加してください。
> `com.unity.test-framework` も optional dependency です。`run-tests` で Unity Test Runner を実行したい場合だけ追加してください。

# クイックスタート

## ステップ1: CLIのインストール

Window > Unity CLI Loop > Settingsを選択します。専用ウィンドウが開くので、**CLI** ボタンが青くなっていなければ **Install CLI** を押してください。

installerはグローバルな`uloop` dispatcherをPATH上に配置します。プロジェクト固有の`uloop-project-runner` binaryは、各プロジェクトの`.uloop/project-runner-pin.json`に従ってuser cacheへ自動的にdownloadされます。

<details>
<summary>V2プロジェクトと併用する場合</summary>

v2とv3のプロジェクトを併用するときも、v3 dispatcherをインストールしたままにしてください。Unityがプロジェクトをv2系の`io.github.hatayama.uloopmcp` packageへ解決している場合、dispatcherは同じバージョンのv2 `uloop-cli` releaseをバージョン別user cacheへ自動的にインストールし、コマンドを委譲します。解決済みpackageのバージョンは、downgrade後に残った古いv3 project-runner pinより優先されます。初回のnpmインストールとv2モードの注記はstderrへ出力されるため、stdoutには委譲先コマンドの出力だけが残ります。v3プロジェクトはpinで選ばれたproject runnerを引き続き使用します。

グローバルな`install`、`update`、`uninstall`、`launch`コマンドは、どのプロジェクトでもv3 dispatcherが処理します。検出されたv2プロジェクトでは、それ以外のプロジェクトコマンド、help、プロジェクトスコープのversion表示が委譲されます。

v2への委譲には、初回コマンドでcacheを作成するnpmを含むNode.js 22以降が必要です。v2プロジェクトのSettingsウィンドウでは、**Update CLI**または**Downgrade CLI**を押さないでください。委譲先CLIが同じv2バージョンを返すため通常はボタン自体が表示されませんが、使用するとグローバルnpm版CLIが復活し、PATHの順序によってv3 dispatcherが隠れる可能性があります。

</details>

<details>
<summary>CLIだけをterminalからinstallする場合はこちら</summary>

Unity Package の setup を開かず、standalone の global CLI だけを入れたい場合に使ってください。

最初にOSまたはパッケージ管理経由で`gh`と`jq`を導入してください。bootstrapはこれらを導入せず、代替手段にもフォールバックしません。immutableなdispatcher Release tagとsource branchを選択します。mainのReleaseは`refs/heads/main`、v3-betaのReleaseは`refs/heads/v3-beta`を指定してください。

macOS、Windows Git Bash の場合:

```bash
REPOSITORY=hatayama/unity-cli-loop
RELEASE_TAG=dispatcher-v<RELEASE_VERSION>
SOURCE_REF=refs/heads/v3-beta
tmp_dir=$(mktemp -d)
gh release download "$RELEASE_TAG" --repo "$REPOSITORY" --pattern 'install.sh' --pattern 'install.sh.sigstore.json' --dir "$tmp_dir" && \
tag_sha=$(gh api "repos/$REPOSITORY/commits/$RELEASE_TAG" --jq .sha) && \
gh attestation verify "$tmp_dir/install.sh" --bundle "$tmp_dir/install.sh.sigstore.json" --repo "$REPOSITORY" --signer-workflow "$REPOSITORY/.github/workflows/dispatcher-publish.yml" --source-ref "$SOURCE_REF" --source-digest "$tag_sha" && \
manifest=$(jq -r '.dsseEnvelope.payload | @base64d | fromjson | .subject[] | "\(.digest.sha256)  \(.name)"' "$tmp_dir/install.sh.sigstore.json" | LC_ALL=C sort) && \
ULOOP_VERSION="$RELEASE_TAG" ULOOP_ARCHIVE_MANIFEST="$manifest" sh "$tmp_dir/install.sh"
```

Windows PowerShell の場合:

```powershell
$repository = 'hatayama/unity-cli-loop'
$releaseTag = 'dispatcher-v<RELEASE_VERSION>'
$sourceRef = 'refs/heads/v3-beta'
$temporaryDirectory = New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP ([guid]::NewGuid()))
gh release download $releaseTag --repo $repository --pattern 'install.ps1' --pattern 'install.ps1.sigstore.json' --dir $temporaryDirectory.FullName
if ($LASTEXITCODE -ne 0) { throw 'Installer download failed.' }
$tagSha = gh api "repos/$repository/commits/$releaseTag" --jq .sha
if ($LASTEXITCODE -ne 0) { throw 'Release tag resolution failed.' }
gh attestation verify (Join-Path $temporaryDirectory.FullName 'install.ps1') --bundle (Join-Path $temporaryDirectory.FullName 'install.ps1.sigstore.json') --repo $repository --signer-workflow "$repository/.github/workflows/dispatcher-publish.yml" --source-ref $sourceRef --source-digest $tagSha
if ($LASTEXITCODE -ne 0) { throw 'Installer attestation verification failed.' }
$bundle = Get-Content -Raw -Encoding UTF8 (Join-Path $temporaryDirectory.FullName 'install.ps1.sigstore.json') | ConvertFrom-Json
$statement = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($bundle.dsseEnvelope.payload)) | ConvertFrom-Json
$manifest = [string]::Join("`n", @($statement.subject | ForEach-Object { "$($_.digest.sha256)  $($_.name)" } | Sort-Object))
$env:ULOOP_VERSION = $releaseTag
$env:ULOOP_ARCHIVE_MANIFEST = $manifest
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $temporaryDirectory.FullName 'install.ps1')
```

native CLI のインストール後、installer は古い npm package を `npm uninstall -g uloop-cli` で自動削除しようとします。
npm が見つからない場合や、古い command が別の Node prefix に属している場合は、手動で実行する command を表示します。

```bash
npm uninstall -g uloop-cli
```

プロジェクトを切り替えるためにv2 CLIをグローバルインストールしないでください。ターミナルの`uloop`がnative dispatcherではなく古いnpm版を指している場合は、npm版を削除してnative dispatcherを再インストールしてください。

```bash
npm uninstall -g uloop-cli
# 上記の検証済みnative installerをもう一度実行します。
which uloop
uloop --version
```

Windows PowerShell の場合:

```powershell
npm uninstall -g uloop-cli
# 上記の検証済みnative installerをもう一度実行します。
Get-Command uloop
uloop --version
```

</details>


> 📸 **SCREENSHOT NEEDED** — user-attachments URLに差し替え
> V3のSettingsウィンドウ。CLI未インストール状態で **Install CLI** ボタンが見える状態。

Settings ウィンドウでは、グローバルな `uloop` コマンドが検出されているかを確認できます。



下記の表示になれば成功です。  
> 📸 **SCREENSHOT NEEDED** — user-attachments URLに差し替え
> V3のSettingsウィンドウ。緑のインジケータと `CLI: v3.x.x` が表示され、CLI検出に成功した状態。

## ステップ2: Skillsのインストール

Claude CodeやCodexなど、対象を選択して **Install Skills** ボタンを押します。  
> 📸 **SCREENSHOT NEEDED** — user-attachments URLに差し替え
> V3のSettingsウィンドウのSkillsセクション。対象（Claude Code / Codexなど）を選択して **Install Skills** ボタンが押せる状態。


<details> 
<summary>terminalからinstallする場合はこちら</summary>

```bash
# Claude Code のプロジェクトにインストール
uloop skills install --claude

# OpenAI Codex のプロジェクトにインストール
uloop skills install --codex

# または、グローバルにインストール
uloop skills install --claude --global
```
</details>

これで完了です！Skillsをインストールすると、LLMツールが以下のような指示に自動で対応できるようになります：

| あなたの指示 | LLMツールが使うSkill |
|---|---|
| 「このプロジェクトのUnityを起動して」 | `/uloop-launch` |
| 「コンパイルエラーを直して」 | `/uloop-compile` |
| 「テストを実行して失敗原因を教えて」 | `/uloop-run-tests` + `/uloop-get-logs` |
| 「シーンの階層構造を確認して」 | `/uloop-get-hierarchy` |
| 「Unityを再生させて、Unityを前面に出して」 | `/uloop-control-play-mode` + `/uloop-focus-window` |
| 「Prefabのパラメータを一括修正して」 | `/uloop-execute-dynamic-code` |
| 「Game Viewのスクショを撮って、UIレイアウトを調整して」 | `/uloop-screenshot` + `/uloop-execute-dynamic-code` |
| 「ゲームプレイの入力を記録して」 | `/uloop-record-input` |
| 「記録した入力を再生して」 | `/uloop-replay-input` |
| 「バグの原因をこの行で止めて調べて」 | `/uloop-pause-point` |


<details>
<summary>バンドルされている全19個のSkills一覧</summary>

- `/uloop-launch` - 正しいバージョンでUnityを起動
- `/uloop-compile` - コンパイルの実行
- `/uloop-get-logs` - Consoleログの取得
- `/uloop-run-tests` - テストの実行
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
- `/uloop-record-input` - PlayMode中のキーボード・マウス入力の記録
- `/uloop-replay-input` - 記録された入力のPlayMode再生
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

このパターンは**入力されたコマンド文字列**に対して照合されるため、`uloop` で始まる呼び出しが対象外になります。詳細と検証結果は [docs/claude-code-sandbox.md](/docs/claude-code-sandbox.md) を参照してください。

## プロジェクトパス指定

`--project-path` を省略した場合は、カレントディレクトリから Unity プロジェクトを検出して接続します。

一つのLLMツールから複数のUnityインスタンスを操作したい場合、プロジェクトパスを明示的に指定します：

```bash
# プロジェクトパスで指定（絶対パス・相対パスどちらも可）
uloop compile --project-path /Users/foo/my-unity-project
uloop compile --project-path ../other-project
```

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

新しい専用ツールが欲しくなったときも、その多くはSkill化で足ります。定型の操作をSKILL.mdの手順、シェルスクリプト、`execute-dynamic-code` に渡すC#スニペットとして書いておけば、AIはそれを呼び出すだけで済みます。Skillの本体は起動時にしか読み込まれず、実行のたびにコードを生成し直す必要もないため、追加のトークン消費はほぼゼロです。作り方は[カスタムツール用 Skills](#カスタムツール用-skills)を参照してください。

# 主要機能
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
```
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
```
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
```
→ execute-dynamic-code (Code: "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); return \"Cube created\";")
→ プロトタイプの迅速な検証、バッチ処理の自動化
→ 信頼できる自動化向けにUnity Editor APIへフルアクセス
```

### PlayMode 自動テスト系ツール
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

```text
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

### 15. record-input - PlayMode中の入力記録
PlayMode中のキーボード・マウス入力をフレーム単位でJSONファイルに記録します。Input Systemのデバイス状態差分によりキー押下、マウス移動、クリック、スクロールイベントをキャプチャします。このツールは Input System パッケージ導入時のみ利用可能です。

```text
→ record-input (Action: Start)
→ record-input (Action: Start, Keys: "W,A,S,D,Space")
→ record-input (Action: Stop)
→ JSONファイルが .uloop/outputs/InputRecordings/ に保存される
```

### 16. replay-input - 記録された入力のPlayMode再生
記録されたキーボード・マウス入力をPlayMode中に再生します。JSON記録を読み込み、Input System経由でフレーム単位で入力を注入します。ループ再生と進捗モニタリングに対応しています。このツールは Input System パッケージ導入時のみ利用可能です。

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

`run-posix-e2e.sh` は、デフォルトでチェックイン済みのネイティブCLIバイナリを使い、すべての `uloop` 呼び出しに明示的な `--project-path` を渡します。CLI recovery/readiness、input record/replay、simulate-mouse UI を1つの流れで検証します。

## Unity CLI Loop 拡張ツールの開発
Unity CLI Loopはコアパッケージへの変更を必要とせず、プロジェクト固有のツールを効率的に開発できます。
型安全な設計により、信頼性の高いカスタムツールを短時間で実装可能です。
(AIに依頼すればすぐに作ってくれるはずです✨)

開発した拡張ツールはGitHubで公開し、他のプロジェクトでも再利用できます。

> [!TIP]
> **AI支援開発向け**: 詳細な実装ガイドが [.claude/rules/cli.md](/.claude/rules/cli.md) に用意されています。このガイドは、Claude Codeが該当ディレクトリで作業する際に自動的に読み込まれます。

<details>
<summary>実装ガイドを見る</summary>

**ステップ1: スキーマクラスの作成**（パラメータを定義）：
```csharp
using io.github.hatayama.UnityCliLoop.ToolContracts;

public class MyCustomSchema : UnityCliLoopToolSchema
{
    public string MyParameter { get; set; } = "default_value";

    public MyEnum EnumParameter { get; set; } = MyEnum.Option1;
}

public enum MyEnum
{
    Option1 = 0,
    Option2 = 1,
    Option3 = 2
}
```

**ステップ2: レスポンスクラスの作成**（返却データを定義）：
```csharp
using io.github.hatayama.UnityCliLoop.ToolContracts;

public class MyCustomResponse : UnityCliLoopToolResponse
{
    public string Result { get; set; }
    public bool Success { get; set; }

    public MyCustomResponse(string result, bool success)
    {
        Result = result;
        Success = success;
    }

    // 必須のパラメータなしコンストラクタ
    public MyCustomResponse() { }
}
```

**ステップ3: ツールクラスの作成**：
```csharp
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public class MyCustomTool : UnityCliLoopTool<MyCustomSchema, MyCustomResponse>
{
    public override string ToolName => "my-custom-tool";

    // メインスレッドで実行されます
    protected override Task<MyCustomResponse> ExecuteAsync(MyCustomSchema parameters, CancellationToken ct)
    {
        // 型安全なパラメータアクセス
        string param = parameters.MyParameter;
        MyEnum enumValue = parameters.EnumParameter;

        // 長時間実行される処理の前にキャンセレーションをチェック
        ct.ThrowIfCancellationRequested();

        // カスタムロジックをここに実装
        string result = ProcessCustomLogic(param, enumValue);
        bool success = !string.IsNullOrEmpty(result);

        // 長時間実行される処理では定期的にキャンセレーションをチェック
        // ct.ThrowIfCancellationRequested();

        return Task.FromResult(new MyCustomResponse(result, success));
    }

    private string ProcessCustomLogic(string input, MyEnum enumValue)
    {
        // カスタムロジックを実装
        return $"Processed '{input}' with enum '{enumValue}'";
    }
}
```

[カスタムツールのサンプル](/Assets/Editor/CustomToolSamples)も参考にして下さい。

</details>

### カスタムツール用 Skills

カスタムツールを作成した際、ツールフォルダ内に `Skill/` サブフォルダを作成し、`SKILL.md` ファイルを配置することで、LLMツールがSkillsシステムを通じて自動的にカスタムツールを認識・使用できるようになります。

**仕組み:**
1. カスタムツールのフォルダ内に `Skill/` サブフォルダを作成
2. `Skill/` フォルダ内に `SKILL.md` ファイルを配置
3. `uloop skills install --claude` を実行（バンドル + プロジェクトのSkillsをまとめてインストール）
4. LLMツールがカスタムSkillを自動認識

**ディレクトリ構造:**
```
Assets/Editor/CustomTools/MyTool/
├── MyTool.cs           # ツール実装
└── Skill/
    ├── SKILL.md        # スキル定義（必須）
    └── references/     # 追加ファイル（オプション）
        └── usage.md
```

**SKILL.md のフォーマット:**
```markdown
---
name: uloop-my-custom-tool
description: "ツールの説明と使用タイミング"
---

# uloop my-custom-tool

ツールの詳細ドキュメント...
```

**スキャン対象**（`Skill/SKILL.md` ファイルを検索）:
- `Assets/**/Editor/<ToolFolder>/Skill/SKILL.md`
- `Packages/*/Editor/<ToolFolder>/Skill/SKILL.md`
- `Library/PackageCache/*/Editor/<ToolFolder>/Skill/SKILL.md`

> [!TIP]
> - フロントマターに `internal: true` を追加すると、インストール対象から除外されます（内部ツールやデバッグ用ツールに便利）
> - `Skill/` フォルダ内の追加ファイル（`references/`、`scripts/`、`assets/` など）もインストール時に一緒にコピーされます

完全な例は [HelloWorld サンプル](/Assets/Editor/CustomCommandSamples/HelloWorld/Skill/SKILL.md) を参照してください。

> [!IMPORTANT]
> **V2でカスタムツールやカスタムスキルを作っていた場合**、V3に上げると拡張APIの名前空間と型名が変わるため、**必ずコンパイルエラーが発生します**。これは想定内の挙動で、内蔵の移行ウィザードが該当ファイルを自動で書き換えます。手作業で直し始める前に、[カスタムツール／スキルのV3移行ガイド](Packages/src/Documentation~/migration-v2-to-v3_ja.md) を参照してください。

## その他

### Unity CLI Loop 関連ファイル

`UserSettings/UnityMcpSettings.json` はユーザー個別のエディタセッション状態を保持するため、常にローカル専用です。このファイル名は旧名称由来の互換名です。

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
