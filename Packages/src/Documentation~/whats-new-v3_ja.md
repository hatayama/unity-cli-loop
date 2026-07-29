# V3の新機能

[English](whats-new-v3.md)

V3では、npmで配布していたCLIをネイティブのGoバイナリに置き換え、通信方式もTCPポート管理からOSネイティブのIPCに変えました。AIエージェントからUnityを操作するのに、Node.jsのセットアップもポートの管理も不要になります。

機能面の目玉は `pause-point` です。ソースを編集することも再コンパイルすることもなく、任意の行でPlayModeを停止して、その瞬間の変数を読み取れます。

MCP対応とシェル補完は削除され、CLIとSkillsが唯一の連携方法になりました。それぞれの詳細は、以下の各セクションを参照してください。

## アップグレード方法

ほとんどのユーザーにとって、アップグレードは3ステップで完了します。Unityパッケージのバージョンを上げ、`Window > Unity CLI Loop > Settings` を開いて **Install CLI**（または **Update CLI**）を押し、古いnpm版CLIをネイティブ版の `uloop` コマンドに置き換えます。最後に同じウィンドウで **Install Skills**（または **Update Skills**）を押し、インストール済みのSkillsをV3の内容に更新してください。インストーラは古いnpmパッケージを `npm uninstall -g uloop-cli` で自動削除しようとし、削除できなかった場合は手動で実行するコマンドを表示します。

新しい `uloop` コマンドはV2プロジェクトとも互換性があります。まだV2パッケージのままのプロジェクトに対しては、対応するV2 CLIを自動で取得してコマンドを委譲します。そのため、CLIを先に入れ替えても、手元に残っているV2プロジェクトはそのまま動き続けます。

移行ガイドが必要なのは、独自の連携を作り込んでいる場合だけです。具体的には、V2の拡張APIで書いたC#カスタムツールがある場合か、`uloop` を呼び出す自作の `SKILL.md`・Markdownドキュメント・シェルスクリプト・PowerShellスクリプトがある場合です。該当する場合は、手作業で直し始める前に [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を読んでください。

## 新しいツール

### `pause-point` — 任意の行で止めてフレームを読む

`pause-point` は、コンパイル済みのメソッドをソースの `file:line` の位置でパッチし、実行がその行に到達したときにUnityを停止します。バグ調査のためにソースを編集する必要も、再コンパイルする必要もなく、PlayModeの実行中に後から仕掛けることもできます。ヒット時のレスポンスには `CapturedVariables` が含まれます。これはメソッドのローカル変数・引数・`this` のインスタンスフィールドを、対象行が実行される直前に取得したもので、IDEのブレークポイントとまったく同じタイミングです。値はライブ参照ではなく、その時点の文字列として記録されるため、Unityが再開した後も証拠として有効なまま残ります。

マーカーには3つのキャプチャモードがあります。`single-shot`（デフォルト）は最初のヒットで自動的に解除され、`continuous` はヒットのたびに停止して過去フレームの履歴を保持し、`trace` は停止せずにヒットだけを記録し続けます。watch式（`uloop enable-watch` / `uloop get-watch-values`）は、停止中のEditor Stepごとに自動で再評価されるため、値がフレーム単位でどう変化するかを追えます。なお、EditorのCode OptimizationモードはDebugである必要があります。Releaseの場合は、対処方法を示したメッセージとともに有効化が拒否されます。

```bash
# pause pointの仕掛け・入力の発火・ヒット待ちを1コマンドで実行
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 \
  --await --trigger "simulate-keyboard --action Press --key Space"

# 現在のマーカー状態を確認し、クリアする
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"
```

> `set-game-view-size` もV3で追加されました。Game Viewのカスタム解像度を取得・設定でき（`uloop set-game-view-size --width 1920 --height 1080`）、`screenshot --capture-mode rendering` の座標系を実行ごとに安定させたいときに使えます。

## CLI・配布方法の変更

- **npmパッケージからネイティブバイナリへ** — CLIはnpm経由ではなく、プラットフォーム別のネイティブバイナリとして配布されます。V3プロジェクトを動かすのにNode.js 22以降は不要になりました。V2の `uloop-cli` npmパッケージは不要になったので、インストーラが削除できなかった場合は `npm uninstall -g uloop-cli` で削除してください。
- **ポート管理の廃止** — CLIはUnixドメインソケット（macOS/Linux）または名前付きパイプ（Windows）でUnityに接続します。設定すべきポートは存在せず、他のEditorインスタンスとポートが衝突することもありません。
- **2層構成、runnerはUnityパッケージに自動追随** — `PATH` 上に置かれるグローバルな `uloop` コマンド（dispatcher）が1つあり、プロジェクトごとの `uloop-project-runner` に処理を委譲します。runnerのバージョンは各プロジェクトの `.uloop/project-runner-pin.json` から決まり、バージョン別のユーザーキャッシュへ自動的にダウンロードされます。Unityパッケージの更新にrunnerが自動で追随するため、プロジェクトごとにCLIのバージョンを合わせる作業は不要で、バージョンの異なる複数プロジェクトも1台のマシンで共存できます。
- **インストーラの真正性検証** — リリース成果物にはsigstoreのattestationが付いています。ドキュメント化されたインストール手順では、インストーラを実行する前に `gh attestation verify` で署名ワークフローとリリースタグのコミットに対して検証します。
- **ランタイム出力の上限** — `.uloop/outputs/` の各サブフォルダは20ファイルまでに制限され、古いものから削除されます。スクリーンショット・テスト結果・ヒエラルキーダンプが無制限に溜まり続けることはなくなりました。
- **V2プロジェクトへの自動委譲** — V3 dispatcherは、プロジェクトがまだV2パッケージに解決されていることを検出すると、対応するV2 `uloop-cli` リリースをバージョン別キャッシュへ導入し、コマンドを転送します。V2とV3のプロジェクトを併用する場合は、V3 dispatcherをインストールしたままにしておくのが正しい運用です。ただし、**V2プロジェクトを使い続ける場合に限り**、委譲がnpmを経由するためNode.js 22以降が必要です。V3プロジェクトだけになれば、Node.jsは一切不要です。

## V3で削除されたもの

- **MCP接続** — 削除されました。CLIとバンドルSkillsを組み合わせて使ってください。MCPで公開していた機能はすべて `uloop` コマンドから利用できます。
- **シェル補完** — 削除されました。既存のシェル設定がエラーにならないよう、`uloop completion` は何もしないスタブとしてのみ残っています。
- **`get-version`, `get-project-info`** — ユーザー向けコマンドとしては削除されました。どちらも内部診断用でエージェント向けSkillとしては配布されていませんでしたが、V2ではスクリプトから呼び出せていました。CLIのバージョンは `uloop --version`、Unityやプロジェクトの情報は `execute-dynamic-code` で取得してください。

## 破壊的変更

- **booleanオプションが値を取らなくなりました。** V2では `--flag true` や `--flag=false` を受け付けていましたが、V3では値なしのフラグ形式です。デフォルトがtrueのオプションには、代わりに否定形が用意されました。たとえば `uloop compile --wait-for-domain-reload false` は `uloop compile --no-wait-for-domain-reload` になり、`--wait-for-domain-reload true` は単に削除するだけです。各フラグのデフォルトは `uloop <command> --help` で確認できます（`default: enabled` / `default: disabled`）。
- **コマンドの削除。** 上記で挙げたコマンドは廃止されました。それらを呼び出しているスクリプトやスキルは、代替手段に合わせて書き換える必要があります。
- **カスタムツールAPIのnamespaceと型名が変わりました。** カスタムツールは `io.github.hatayama.UnityCliLoop.ToolContracts` 名前空間の `UnityCliLoopTool<TSchema, TResponse>` を継承する形になりました。V2 APIで書いたC#カスタムツールがプロジェクトにある場合、V3に上げると**必ずコンパイルエラーが発生します**。これは想定内の挙動で、内蔵の移行ウィザードが該当ファイルを自動で書き換えるので、手作業で直し始めないでください。詳細は [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を参照してください。
