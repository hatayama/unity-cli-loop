# V3の新機能

[English](whats-new-v3.md) | 日本語

V3では、AIエージェントからUnityを操作するのに、Node.jsのセットアップもポートの管理も不要になりました。接続の安定性も向上しました。

CLIのバージョン管理も不要になりました。V2ではパッケージを更新するたびに同じバージョンのCLIを手で入れ直す必要がありましたが、V3では一度インストールすれば以後の更新は自動で行われます。さらに、プロジェクトごとに必要なCLIバージョンを自動で使い分けるため、パッケージバージョンの異なる複数のプロジェクトを並行して触っても、CLIを切り替える作業は一切ありません。

機能面の目玉は `hot-reload` と `pause-point` です。`hot-reload` は、C#の編集（メソッド本体、追加したメンバー、新しい型）を、再コンパイルもDomain Reloadも挟まずに実行中のEditorへ即時反映します。`pause-point` は、ソースを編集することも再コンパイルすることもなく、任意の行でPlayModeを停止して、その瞬間の変数を読み取れます。特定の瞬間のスクリーンショットを撮ったり、特定の瞬間のHierarchyを調査したりするのにも便利です。ちらつきや1フレームだけの表示崩れのように、スクリーンショット1枚では分からないものは、`record-video` がGame Viewや任意のEditorウィンドウを録画し、エージェントが読めるフレーム画像で返します。

MCP対応は削除され、CLIとSkillsが唯一の連携方法になりました。それぞれの詳細は、以下の各セクションを参照してください。

## アップグレード方法

ほとんどのユーザーにとって、アップグレードは3ステップで完了します。Unityパッケージのバージョンを上げ、`Window > Unity Loop Harness > Settings` を開いて **Install CLI**（または **Update CLI**）を押し、古いnpm版CLIをネイティブ版の `uloop` コマンドに置き換えます。最後に同じウィンドウで **Install Skills**（または **Update Skills**）を押し、インストール済みのSkillsをV3の内容に更新してください。インストーラは古いnpmパッケージを `npm uninstall -g uloop-cli` で自動削除しようとし、削除できなかった場合は手動で実行するコマンドを表示します。

新しい `uloop` コマンドはV2プロジェクトとも互換性があります。まだV2パッケージのままのプロジェクトに対しては、対応するV2 CLIを自動で取得してコマンドを委譲します。そのため、CLIを先に入れ替えても、手元に残っているV2プロジェクトはそのまま動き続けます。

移行ガイドが必要なのは、独自の連携を作り込んでいる場合だけです。具体的には、V2の拡張APIで書いたC#カスタムツールがある場合か、`uloop` を呼び出す自作の `SKILL.md`・Markdownドキュメント・シェルスクリプト・PowerShellスクリプトがある場合です。該当する場合は、手作業で直し始める前に [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を読んでください。

## 新しいツール

### `hot-reload` — C#の編集を再コンパイルなしで即時反映

`hot-reload` は、編集した `.cs` ファイルを、再コンパイルもDomain Reloadも挟まずに実行中のEditor（EditMode / PlayMode）へ直接反映します。属性やソースマーカーは不要で、private / internalメンバーへのアクセス、staticメソッド、asyncメソッド、イテレータも動作します。PlayModeを止めずにゲームロジックを修正して、その場で挙動を確かめられます。

メソッド本体だけでなく、追加したメソッド・フィールド・プロパティ・フィールド形式のイベントも適用でき、トップレベルの新しいクラス・構造体・enum・インターフェースも導入できます。1回のリロードで同じアセンブリの複数ファイルを扱え、追加したメンバーはそのリロードで編集した他のファイルから参照できます。宣言したファイルと呼び出し側のファイルをまとめて渡してください。新しい型を他のアセンブリやリロード対象外のファイルから使う場合、リフレクション、シリアライズ、Unityメッセージの検出には、従来どおり `uloop compile` が必要です。コンストラクタ・演算子・構造体のメソッドなど反映できない編集は、理由とともに `Skipped` として報告されます。`Failed` のメソッドがあるとそのファイル全体が、`Failed` の型があるとそのアセンブリの全ファイルが反映されません。他のアセンブリのファイルは反映されます。

```bash
# 指定したファイルのメソッド本体を実行中のEditorへ適用
uloop hot-reload --files Assets/Scripts/Enemy.cs

# 引数なしで、最後のコンパイル以降に変更された.csを自動選択して適用
uloop hot-reload

# 適用中の変更一覧の確認と、適用した変更すべての解除
uloop hot-reload --status
uloop hot-reload --revert-all
```

hot reloadは探索フェーズ、`uloop compile` は着地フェーズとして使い分けてください。コンパイルやDomain Reloadが起きると、hot reloadで反映した変更はすべて解除されます（ソースの編集自体は残り、コンパイルで通常どおり反映されます）。反映できなかった編集が残ったときに同じコマンド内で `uloop compile` まで走らせるかは `--compile-on-skip` で決まります。`auto`（デフォルト）はEditModeのときだけコンパイルするのでPlayModeを止めることはなく、`off` はコンパイルしません。

### `pause-point` — 任意の行で止めてフレームを読む

`pause-point` は、コンパイル済みのメソッドをソースの `file:line` の位置でパッチし、実行がその行に到達したときにUnityを停止します。バグ調査のためにソースを編集する必要も、再コンパイルする必要もなく、PlayModeの実行中に後から仕掛けることもできます。ヒット時のレスポンスには `CapturedVariables` が含まれます。これはメソッドのローカル変数・引数・`this` のインスタンスフィールドを、対象行が実行される直前に取得したもので、IDEのブレークポイントとまったく同じタイミングです。値はライブ参照ではなく、その時点の文字列として記録されるため、Unityが再開した後も証拠として有効なまま残ります。

マーカーには3つのキャプチャモードがあります。`single-shot`（デフォルト）は最初のヒットで自動的に解除され、`continuous` はヒットのたびに停止して過去フレームの履歴を保持し、`trace` は停止せずにヒットだけを記録し続けます。watch式（`uloop enable-watch` / `uloop get-watch-values`）は、停止中のEditor Stepごとに自動で再評価されるため、値がフレーム単位でどう変化するかを追えます。`--snapshot-timing post-line` を付けると、対象行の実行直後の値を取得します。`--persist` を付けたマーカーはDomain Reload（コンパイルやPlayModeへの移行）の後に自動で仕掛け直され、watch式もDomain Reloadをまたいで残ります。パッケージ内のスクリプトにも仕掛けられます（`--file Packages/<package-id>/...`）。EditorのCode OptimizationがReleaseの場合は、有効化の時点でこのEditorセッションをDebugに切り替え、その旨を警告で返します。`uloop set-code-optimization debug --startup` を実行すると、以後のセッションもDebugで起動します。

```bash
# pause pointの仕掛け・入力の発火・ヒット待ちを1コマンドで実行
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 \
  --await --trigger "simulate-keyboard --action Press --key Space"

# 現在のマーカー状態を確認し、クリアする
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"
```

### `record-video` — スクリーンショット1枚では分からないものを見る

`record-video` は、PlayMode中のGame View、またはScene・Inspector・Consoleなど任意のEditorウィンドウを `.mp4`（Linuxでは `.webm`）に録画します。エージェントは動画を見られないため、レスポンスでは読める形の成果物も示します。印を付けた瞬間（印が無ければ最も大きく変化した箇所）の前後最大32フレームを並べた画像、録画全体のタイムライン画像、フレームごとの変化量です。ちらつき、カクつき、1フレームだけの表示崩れ、フェード、画面遷移の確認に使えます。`--seconds` を付けると、録画開始・`--trigger` のコマンド実行・その瞬間への印付け・待機・録画停止までを1コマンドで行います。

```bash
# キー入力1回の前後3秒のGame Viewを録画
uloop record-video --seconds 3 --trigger "simulate-keyboard --action Press --key Space"

# Editorウィンドウを録画（PlayModeは不要）
uloop record-video --window-name Inspector --seconds 5
```

### `game-view-mode` — 端末の枠でUIを確認する

`game-view-mode` は、GameタブがGame viewとDevice Simulatorのどちらを表示しているかを返し、その場で切り替えます。Simulatorに切り替えると、Player Settingsが許す範囲で、シミュレートする端末の向きをGame viewの向きに合わせます。`--device` で端末を名前で選べます（`--list-devices` で名前の一覧を返します）。

```bash
# シミュレートした端末でUIを表示し、元に戻す
uloop game-view-mode --device "iPhone 12"
uloop game-view-mode --to GameView
```

### `uloop status` と `uloop cancel` — Unityがコマンドを受け付けられるかを知る

`uloop status` は、副作用なしで、プロジェクトのEditorが今コマンドを受け付けられるか、それとも起動中・コンパイル中・アセットのインポート中・別のコマンドを実行中・起動していないのかを返します。実行中のコマンドが終わらないときは、`uloop cancel` でEditorが実行中のツールを止められます。そのツールを待っていたコマンドは `TOOL_CANCELLED` を返します。

> `set-game-view-size` もV3で追加されました。Game Viewのカスタム解像度を取得・設定でき（`uloop set-game-view-size --width 1920 --height 1080`）、`screenshot --capture-mode rendering` の座標系を実行ごとに安定させたいときに使えます。
>
> `simulate-mouse-input` には `--dry-run` が追加されました。マウス入力を注入せずに、Game View座標が3D物理で何にヒットするかを確認できます（`uloop simulate-mouse-input --dry-run --x 640 --y 360`）。クリックを送る前の着弾確認に使えます。

## 既存ツールの改善

- **`run-tests`** — `--filter-type class` で1つのクラスのテストだけを実行でき、`--rerun-failed` で前回失敗したテストだけを再実行できます。`--unsaved-changes` で、実行前に未保存のSceneとPrefab Stageの変更をどう扱うかを選べます。保存する（デフォルト）、実行を失敗させる、破棄する、の3つです。PlayModeテストでは、`--respect-enter-play-mode-settings` を付けると、Domain Reloadを強制的にオフにせず、プロジェクトのEnter Play Mode設定のまま実行します。
- **`screenshot` と `simulate-mouse-ui`** — `--annotate-elements` がuGUI要素に加えてUI Toolkitのコントロールにもラベルを付けるようになり、`simulate-mouse-ui` でそのラベルの座標にあるUI Toolkitのコントロールをクリック・ドラッグできます。`screenshot --prefab-path` は、PrefabやモデルのアセットをPrefab Modeで開かずに、隔離したプレビューシーンに単体で描画します。開いているシーンには触れません。
- **`launch`** — `uloop launch -r`（再起動）と `-q`（終了）は、まずUnity自身に終了を依頼するので、ウィンドウレイアウトが保たれます。未保存のシーンの変更は保存ダイアログを出さずに破棄されます。Unityを強制終了するのは、依頼を受け付けられないときと、30秒以内に終了しなかったときだけです。

## CLI・配布方法の変更

- **npmパッケージからネイティブバイナリへ** — CLIはnpm経由ではなく、プラットフォーム別のネイティブバイナリとして配布されます。V3プロジェクトを動かすのにNode.js 22以降は不要になりました。V2の `uloop-cli` npmパッケージは不要になったので、インストーラが削除できなかった場合は `npm uninstall -g uloop-cli` で削除してください。
- **Linux対応** — Linux（x86_64）でも、インストール・自動更新・project runnerのダウンロードが動作します。Linuxでは `focus-window` は使えません。
- **Homebrewとwinget** — インストールスクリプトのほかに、macOSでは `brew install hatayama/tap/uloop`、Windowsでは `winget install --id hatayama.uloop` でもCLIを入れられます。どちらかで入れたCLIは、自分で更新せず、そのパッケージマネージャー経由で更新します（`brew upgrade uloop` / `winget upgrade --id hatayama.uloop`）。
- **Gitリポジトリのルートへのスキル導入** — `uloop skills install --claude --root git` は、Unityプロジェクトを含むGitリポジトリのルートにスキルを入れます。Unityプロジェクトがリポジトリのサブフォルダにある場合に使います。
- **ポート管理の廃止** — CLIはUnixドメインソケット（macOS/Linux）または名前付きパイプ（Windows）でUnityに接続します。設定すべきポートは存在せず、他のEditorインスタンスとポートが衝突することもありません。
- **2層構成、runnerはUnityパッケージに自動追随** — `PATH` 上に置かれるグローバルな `uloop` コマンド（dispatcher）が1つあり、プロジェクトごとの `uloop-project-runner` に処理を委譲します。runnerのバージョンは各プロジェクトの `.uloop/project-runner-pin.json` から決まり、バージョン別のユーザーキャッシュへ自動的にダウンロードされます。Unityパッケージの更新にrunnerが自動で追随するため、プロジェクトごとにCLIのバージョンを合わせる作業は不要で、バージョンの異なる複数プロジェクトも1台のマシンで共存できます。
- **インストーラの真正性検証** — リリース成果物にはsigstoreのattestationが付いています。インストーラは、ダウンロードしたアーカイブをリポジトリに固定されたダイジェストと照合してから実行します。
- **ランタイム出力の上限** — `.uloop/outputs/` の各サブフォルダは20ファイルまでに制限され、古いものから削除されます。スクリーンショット・テスト結果・ヒエラルキーダンプが無制限に溜まり続けることはなくなりました。
- **V2プロジェクトへの自動委譲** — V3 dispatcherは、プロジェクトがまだV2パッケージに解決されていることを検出すると、対応するV2 `uloop-cli` リリースをバージョン別キャッシュへ導入し、コマンドを転送します。V2とV3のプロジェクトを併用する場合は、V3 dispatcherをインストールしたままにしておくのが正しい運用です。ただし、**V2プロジェクトを使い続ける場合に限り**、委譲がnpmを経由するためNode.js 22以降が必要です。V3プロジェクトだけになれば、Node.jsは一切不要です。

## V3で削除されたもの

- **MCP接続** — 削除されました。CLIとバンドルSkillsを組み合わせて使ってください。MCPで公開していた機能はすべて `uloop` コマンドから利用できます。
- **`get-version`, `get-project-info`** — ユーザー向けコマンドとしては削除されました。どちらも内部診断用でエージェント向けSkillとしては配布されていませんでしたが、V2ではスクリプトから呼び出せていました。CLIのバージョンは `uloop --version`、Unityやプロジェクトの情報は `execute-dynamic-code` で取得してください。
- **`record-input`** — 削除されました。入力記録は Unity Editor の **Window > Unity Loop Harness > Recordings** で行い、作成したJSONを `replay-input` で再生してください。

## 破壊的変更

- **booleanオプションが値を取らなくなりました。** V2では `--flag true` や `--flag=false` を受け付けていましたが、V3では値なしのフラグ形式です。デフォルトがtrueのオプションには、代わりに否定形が用意されました。たとえば `uloop compile --wait-for-domain-reload false` は `uloop compile --no-wait-for-domain-reload` になり、`--wait-for-domain-reload true` は単に削除するだけです。各フラグのデフォルトは `uloop <command> --help` で確認できます（`default: enabled` / `default: disabled`）。
- **コマンドの削除。** 上記で挙げたコマンドは廃止されました。それらを呼び出しているスクリプトやスキルは、代替手段に合わせて書き換える必要があります。
- **カスタムツールAPIのnamespaceと型名が変わりました。** カスタムツールは `io.github.hatayama.UnityCliLoop.ToolContracts` 名前空間の `UnityCliLoopTool<TSchema, TResponse>` を継承する形になりました。V2 APIで書いたC#カスタムツールがプロジェクトにある場合、V3に上げると**必ずコンパイルエラーが発生します**。これは想定内の挙動で、内蔵の移行ウィザードが該当ファイルを自動で書き換えるので、手作業で直し始めないでください。詳細は [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を参照してください。
