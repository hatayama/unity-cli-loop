# カスタムツール／スキルのV3移行ガイド

[English](migration-v2-to-v3.md) | 日本語

> [!NOTE]
> **ほとんどのユーザーには、このガイドは不要です。** C#カスタムツールも、`uloop` を呼び出す自作のスキル／スクリプトも持っていないなら、アップグレードは2ステップで終わります。Unityパッケージのバージョンを上げ、`Window > Unity CLI Loop > Settings` で **Install CLI**（または **Update CLI**）を押すだけです。これで移行は完了なので、ここで読み終えて構いません。V3で何が変わったかは [V3の新機能](whats-new-v3_ja.md) を参照してください。

## このガイドが必要な人

次のどちらかに当てはまる場合は、続きを読んでください。

- **V2 APIで書いたC#カスタムツールがある** — V2のツール基底型を継承したクラスや、`io.github.hatayama.uLoopMCP` 名前空間をimportしているコードがある場合です。Step 1〜Step 3に従ってください。移行ウィザードが自動で書き換えます。
- **`uloop` を呼び出す `SKILL.md`・Markdownドキュメント・POSIXシェルスクリプト・PowerShellスクリプトがある** — V3ではbooleanオプションの書式が変わり、いくつかのコマンドが削除されたため、既存の呼び出しが気づかないうちに壊れる可能性があります。Step 4以降に従ってください。

両方に当てはまる場合は、このガイドを上から順に進めてください。

## 事前に知っておくこと

**V2 APIのカスタムツールがあるプロジェクトをV3に上げると、必ずコンパイルエラーが発生します。これは想定内の挙動です。** V3の拡張APIは新しい名前空間と新しい型名になったため、V2のソースはそのままではコンパイルできません。**このエラーを手作業で直し始めないでください。** 内蔵の移行ウィザードがプロジェクトをスキャンして該当ファイルを書き換えます。先に手で編集してしまうと、ウィザードの処理をかえって難しくします。

作業を始める前に、**commitするかバックアップを取ってください**。ウィザードは対象ファイルをその場で書き換えます。確認ダイアログにも同じ警告が表示されます（*"Commit or back up your project first (VCS recommended)."*）。ウィザードが何を変更したかを確認するには、バージョン管理システムを使うのが一番簡単です。

## Step 1: V3でUnityを起動し、Safe Modeを拒否する

前提として、この先の手順は**V3に更新したパッケージでUnity Editorが起動するところ**から始まります。パッケージをどう更新したかで、そこまでの操作が変わります。

- **Unityを終了した状態で `Packages/manifest.json` を直接書き換えた場合** — そのままUnityを起動してください。再起動は不要で、この起動がそのまま「V3での最初の起動」になります。
- **Unityを起動したままPackage Managerで更新した場合** — Unityを一度閉じて、開き直してください。Safe Modeに入るかを尋ねるダイアログは**Editorの起動時にしか表示されない**ため、起動したままのセッションではこのダイアログを見ることができず、この先の手順に進めません。

起動時、UnityはV2ソースのコンパイルエラーを検出し、Safe Modeに入るかどうかを尋ねてきます。**`Ignore` を押して、Safe Modeに入らずに起動させてください。**

**なぜ重要なのか:** Safe Modeでは、ホワイトリストに登録されたアセンブリだけが読み込まれます。Unity CLI Loopのエディタ拡張はこのホワイトリストに含まれないため、Safe Modeではパッケージのコードが動かず、**移行ウィンドウが開きません**。Safe Modeを拒否することが、この先の手順の前提になります。

> 📸 **SCREENSHOT NEEDED** — `images/migration-safe-mode-dialog.png`
> 起動時に表示される Safe Mode 確認ダイアログ。`Ignore` ボタンが見える状態。

> [!WARNING]
> `Preferences > Asset Pipeline > Show Enter Safe Mode Dialog` をOFFにしていると、Unityは**確認なしでSafe Modeに入ります**。この場合 `Ignore` を押す機会自体がありません。この設定をONに戻してから、Editorを再起動してください。

誤ってSafe Modeに入ってしまっても、やり直せます。Safe Modeではパッケージのコードが動かないため、移行状態は何も記録されていません。Unityを再起動して `Ignore` を選べば、以下のとおりウィザードが開きます。

CLIから起動してもこのダイアログは回避できません。`uloop launch` でも同じダイアログが出ます。Unityの仕様上、`-ignorecompilererrors` はGUIモードではこのダイアログを抑止しないためです。

## Step 2: ウィザードのスキャンを待つ

Editorが起動すると、`Unity CLI Loop Migration` ウィンドウが**自動的に開き、スキャンが始まります**。この自動オープンは、パッケージのメジャーバージョンがV2からV3になった最初の起動時に発火します。つまり一度きりのイベントで、再起動すればまた出せるというものではありません。

> 📸 **SCREENSHOT NEEDED** — `images/migration-wizard-overview.png`
> `Unity CLI Loop Migration` ウィンドウ全体。`C# Source Structure Migration` と `AI Skill and Script Migration` の2セクションが両方見える状態。

**スキャンが終わるまで少し待ってください。** コンパイルが失敗している間、Unityはドメインリロードを行いません。そのためパッケージはコールバック1回に頼らず、失敗状態をポーリングして検出します。ステータスが表示されるまでに少し間があるのは正常な挙動で、ウィンドウが固まったわけではありません。

スキャンが落ち着くと、`C# Source Structure Migration` セクションが結果を表示します。自動オープン直後は、コンパイルエラー起点の検出として次のように表示されます。

> Detected legacy V2 custom tool API usage from a compile error. Click Migrate to scan the project and update the affected files.

プロジェクトスキャンでファイル一覧が確定すると、次の表示になります。

> Found 3 C# files that need V3 migration.

> 📸 **SCREENSHOT NEEDED** — `images/migration-wizard-detected.png`
> 移行対象を検出した状態のウィザード。`Found {N} C# files that need V3 migration.` のステータスが表示され、`Migrate` ボタンが押せる状態。

**ウィンドウが自動で開かなかった場合**は、`Window > Unity CLI Loop > Custom Tool Migration` から手動で開いてください。

## Step 3: Migrateを押す

**`Migrate`** を押します。`Migrate C# Sources?` というタイトルの確認ダイアログが表示され、ファイルがその場で書き換えられること、先にcommitまたはバックアップを取るべきことが警告されます。**`Migrate`** で確定してください。

> 📸 **SCREENSHOT NEEDED** — `images/migration-wizard-confirm-dialog.png`
> `Migrate C# Sources?` 確認ダイアログ。"Commit or back up your project first (VCS recommended)." の警告文が読める状態。

実行中はボタンが `Migrating...` になり、ステータス行に `{n}/{N} steps complete.` と進捗が表示されます。完了すると次のように表示されます。

> Migration complete. No further C# migration is needed.

この時点で書き換え後のソースはコンパイルが通り、起動時に出ていたエラーは解消しています。

### ステータス文言とボタン表示の意味

| ステータス文言 | 意味 |
|---|---|
| `C# source migration status has not been checked.` | このセッションではまだプロジェクトをスキャンしていない。 |
| `Detected legacy V2 custom tool API usage from a compile error.` | コンパイルエラーからV2 APIの使用を検出した。`Migrate` を押すと実際のプロジェクトスキャンが走り、該当ファイルを書き換える。 |
| `Found {N} C# files that need V3 migration.` | プロジェクトスキャンが完了し、書き換え対象が `{N}` 件見つかった。 |
| `No C# source structure migration is needed.` | スキャンの結果、書き換える対象はなかった。 |
| `Migration complete. No further C# migration is needed.` | 書き換えが正常に完了した。 |

| ボタン表示 | 意味 |
|---|---|
| `Migrate` | スキャンと書き換えを実行できる状態。 |
| `Migrating...` | 書き換えを実行中。 |
| `Check required` | まだ状態が未チェック。押すとスキャンする。 |
| `Nothing to migrate` | スキャンの結果、移行が必要なファイルはなかった。 |

### ウィザードが書き換える内容

- **名前空間** — ツールの契約型について、`io.github.hatayama.uLoopMCP` が `io.github.hatayama.UnityCliLoop.ToolContracts` になります。
- **基底型** — `AbstractUnityTool` が `UnityCliLoopTool<TSchema, TResponse>` に、`BaseToolSchema` が `UnityCliLoopToolSchema` に、`BaseToolResponse` が `UnityCliLoopToolResponse` になります。
- **属性** — `[McpTool]` が `[UnityCliLoopTool]` になります。`Description` 引数は削除され、`DisplayDevelopmentOnly` と `RequiredSecuritySetting` は引き継がれます。
- **asmdefの参照** — `.asmdef` ファイル内の `uLoopMCP.Editor` と `uLoopMCP.Runtime` への参照が、V3のアセンブリに張り替えられます。

## Step 4: uloopを呼び出すスキル・スクリプトを移行する

2つ目のセクション `AI Skill and Script Migration` は、残り半分の作業を担当します。対象は、`uloop` を呼び出している `SKILL.md`・Markdownドキュメント・シェルスクリプト・PowerShellスクリプトです。

> 📸 **SCREENSHOT NEEDED** — `images/migration-wizard-ai-skill.png`
> ウィザードの `AI Skill and Script Migration` セクション。`Prompt for your AI agent` の折りたたみを開いた状態が望ましい。

**このウィンドウ自身はファイルを書き換えません。** 行うのは一時AIスキルのインストールと削除だけで、実際の編集はAIエージェントが行います。手順は次のとおりです。

1. **`Install Migration Skill`** を押します。`v3-cli-invocation-migration` という名前の一時スキルがプロジェクトにインストールされます。
2. **`Prompt for your AI agent`** の折りたたみを開き、**`Copy AI Prompt`** を押します。
3. コピーしたプロンプトをAIエージェントに貼り付けて実行させます。このスキルは、`uloop` の呼び出しを検索し、前後の文脈を確認したうえで、本物のV2 CLI使用箇所だけを更新する手順をエージェントに教えます。C#のスニペット、enum参照、無関係なJSONは書き換えません。
4. エージェントの変更内容をレビューします。編集したファイル、移行候補として見つかった削除済みコマンド、手動で確認すべき箇所がレポートされます。

## CLIオプションの変更点

これらの変更はAIエージェントが自動で適用します。以下の表は、その結果を人間がレビューするための参照用です。正典は `Packages/src/TemporarySkills~/v3-cli-invocation-migration/Skill/references/first-party-v2-to-v3.md` です。

### booleanオプションの変換ルール

V3のbooleanオプションは値を取りません。V2の呼び出しをどう変換するかは、V3側オプションのデフォルト値によって決まります。

| V2の書式 | V3の書式 |
|---|---|
| `--flag true` / `--flag=true` | V3オプションがデフォルトfalseの肯定形なら `--flag` |
| `--flag false` / `--flag=false` | V3のデフォルトが既にfalseならオプションごと削除 |
| `--flag true` / `--flag=true` | V3のデフォルトが既にtrueならオプションごと削除 |
| `--flag false` / `--flag=false` | V3のデフォルトがtrueならV3の否定形オプションを使う |

以下の表にないbooleanオプションは、`uloop <command> --help` で確認してください。V3の各フラグはデフォルト値が `default: enabled`（true）または `default: disabled`（false）として表示されます。

### 名前が変わったfirst-partyオプション

| V2コマンド | V2オプション | V3での置き換え |
|---|---|---|
| `uloop compile` | `--force-recompile true` | `--force-recompile` |
| `uloop compile` | `--force-recompile false` | 削除 |
| `uloop compile` | `--wait-for-domain-reload true` またはフラグのみ | 削除 |
| `uloop compile` | `--wait-for-domain-reload false` | `--no-wait-for-domain-reload` |
| `uloop compile` | `--reload-external-scene-changes true` | 削除 |
| `uloop compile` | `--reload-external-scene-changes false` | `--stop-on-external-scene-changes` |
| `uloop run-tests` | `--save-before-run true` またはフラグのみ | 削除 |
| `uloop run-tests` | `--save-before-run false` | `--fail-on-unsaved-changes` |
| `uloop record-input` | `--show-overlay true` | 削除 |
| `uloop record-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop replay-input` | `--show-overlay true` | 削除 |
| `uloop replay-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop get-hierarchy` | `--include-components true` | 削除 |
| `uloop get-hierarchy` | `--include-components false` | `--no-include-components` |
| `uloop get-hierarchy` | `--include-inactive true` | 削除 |
| `uloop get-hierarchy` | `--include-inactive false` | `--no-include-inactive` |
| `uloop execute-dynamic-code` | `--compile-only true` | `--compile-only` |
| `uloop execute-dynamic-code` | `--compile-only false` | 削除 |

> [!WARNING]
> このうち2つのオプション名は、**別のコマンドではV3として正しい書式**なので、名前だけを見て一括置換してはいけません。`uloop execute-dynamic-code` ではフラグのみの `--wait-for-domain-reload` がデフォルトfalseの正しいV3フラグであり、`uloop find-game-objects` ではフラグのみの `--include-inactive` が同じくデフォルトfalseの正しいV3フラグです。編集する前に、その呼び出しがどのコマンドのものかを必ず確認してください。

### 削除・改名されたコマンド

`capture-window` から `get-menu-items` までの6つは、V2の途中で既に削除・改名されたもので、V2の最終版には存在しません。古いV2向けに書かれたスクリプトに残っている場合に備えて載せています。

| V2コマンド | V3での扱い |
|---|---|
| `uloop capture-window` | `uloop screenshot` に改名。 |
| `uloop unity-search` | 削除。`uloop execute-dynamic-code` を使うか、通常のシーン内検索なら `uloop find-game-objects` を使う。 |
| `uloop get-unity-search-providers` | 削除。`uloop execute-dynamic-code` を使う。 |
| `uloop get-provider-details` | 削除。`uloop execute-dynamic-code` を使う。 |
| `uloop execute-menu-item` | 削除。`uloop execute-dynamic-code` から `EditorApplication.ExecuteMenuItem(...)` を呼ぶ。 |
| `uloop get-menu-items` | 削除。`uloop execute-dynamic-code` を使う。 |
| `uloop get-version` | ユーザー向けコマンドとしては削除。CLIのバージョンは `uloop --version`、Unity Editorのバージョンは `uloop execute-dynamic-code` で取得する。 |
| `uloop get-project-info` | 削除。必要なプロジェクト情報は `uloop execute-dynamic-code` で取得する。 |

移行スキルはこれらを自動で書き換えず、移行候補として報告するだけです。スクリプトが何をしていたかによって適切な代替手段が変わるためです。

## Step 5: 一時スキルを削除する

ドキュメントとスクリプトの移行が終わったら、一時スキルを削除してください。画面にも明示されています。

> This skill is temporary. Remove it once your docs and scripts are migrated to V3 CLI syntax.

ウィザードの **`Remove Migration Skill`** を押すか、インストールした対象ごとにCLIから削除します。

```bash
uloop skills uninstall-v3-migration --claude
uloop skills uninstall-v3-migration --codex
```

## 動作確認

次の3点を確認してください。

```bash
# 1. プロジェクトがエラーなしでコンパイルできる
uloop compile

# 2. カスタムツールが登録され、呼び出せる
uloop list
```

- `uloop compile` はエラー0件になるはずです。移行と無関係な警告は問題ありません。
- `uloop list` には、アップグレード前に持っていたカスタムツールがすべて、同じツール名で表示されるはずです。移行で変わるのは名前空間と基底型であり、`ToolName` の値ではありません。
- 最後に、自作のスキルやスクリプトを実際に一通り実行し、中の `uloop` 呼び出しが期待どおり動くことを確認してください。オプションの書式エラーは、無言で無視されるのではなくコマンドの失敗として即座に現れます。

## 手動移行のリファレンス

手作業で移行したい場合のために、ウィザードが行う具体的な置き換えを示します。旧名前空間は `io.github.hatayama.uLoopMCP`、V3のツール契約名前空間は `io.github.hatayama.UnityCliLoop.ToolContracts` です。

| V2の型 | V3の型 |
|---|---|
| `AbstractUnityTool` | `UnityCliLoopTool` |
| `IUnityTool` | `IUnityCliLoopTool` |
| `BaseToolSchema` | `UnityCliLoopToolSchema` |
| `BaseToolResponse` | `UnityCliLoopToolResponse` |
| `McpToolAttribute`（`[McpTool]`） | `UnityCliLoopToolAttribute`（`[UnityCliLoopTool]`） |
| `McpConstants` | `UnityCliLoopConstants` |
| `SecuritySettings` | `UnityCliLoopSecuritySetting` |
| `ToolParameterSchemaGenerator` | `UnityCliLoopToolParameterSchemaGenerator` |
| `ParameterValidationException` | `UnityCliLoopToolParameterValidationException` |
| `CustomToolManager` | `UnityCliLoopToolRegistrar` |

| V2のアセンブリ参照 | V3のアセンブリ参照 |
|---|---|
| `uLoopMCP.Editor` | `UnityCLILoop.Application` |
| `uLoopMCP.Runtime` | `UnityCLILoop.Runtime` |

## トラブルシューティング

**Safe Modeに入ってしまった。** Safe Modeではパッケージのコードが動かないため、何も記録されておらず、失われたものもありません。Editorを再起動し、ダイアログで `Ignore` を押してください。そもそもダイアログが出なかった場合は、`Preferences > Asset Pipeline > Show Enter Safe Mode Dialog` をONに戻してから再起動してください。

**ウィザードが自動で開かない。** まず、EditorがV3のパッケージで起動し直されたかを確認してください。自動オープンは起動時、しかもメジャーバージョンがV2からV3になった最初の起動時に発火します。Unityを起動したままPackage Managerで更新したセッションは対象外なので、Editorを再起動してください。開かない場合は `Window > Unity CLI Loop > Custom Tool Migration` から手動で開いてください。手動で開いても、Step 2以降の手順はまったく同じです。

**`Migrate` 実行後もコンパイルエラーが残る。** まず残っているエラーの内容を読んでください。カスタムロジックとV2 API呼び出しが混在しているソースは、置換ルールでカバーできず手直しが必要な場合があります。VCSのdiffでウィザードが何を変更したかを確認し、やり直したい場合はcommitやバックアップから戻したうえで、上記の「手動移行のリファレンス」を使って残りを対処してください。

**AIエージェントから一時スキルが見えない。** このスキルは対象ごとにインストールされるため、実際に使っているエージェント向けにインストールしたか（`--claude`、`--codex` など）を確認してください。正しい対象を指定して `uloop skills install` を実行し直すと、インストール済みのコピーが更新されます。
