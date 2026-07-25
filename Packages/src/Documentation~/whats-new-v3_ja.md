# V3の新機能

[English](whats-new-v3.md)

V3では、npmで配布していたCLIをネイティブのGoバイナリに置き換えました。AIエージェントからUnityを操作するのに、Node.jsは不要になりました。通信方式もTCPポート管理から、macOS/LinuxではUnixドメインソケット、Windowsでは名前付きパイプに変わり、ポートの設定もポートの衝突もなくなりました。目玉となる新機能は `pause-point` です。ソースを編集することも再コンパイルすることもなく、任意の `file:line` でPlayModeを停止し、その瞬間のローカル変数・引数・インスタンスフィールドを取得できます。MCP対応とシェル補完は削除され、CLIとSkillsが唯一の連携方法になりました。

## アップグレード方法

ほとんどのユーザーにとって、アップグレードは2ステップで完了します。Unityパッケージのバージョンを上げ、`Window > Unity CLI Loop > Settings` を開いて **Install CLI**（または **Update CLI**）を押し、古いnpm版CLIをネイティブdispatcherに置き換えるだけです。インストーラは古いnpmパッケージを `npm uninstall -g uloop-cli` で自動削除しようとし、削除できなかった場合は手動で実行するコマンドを表示します。

移行ガイドが必要なのは、独自の連携を作り込んでいる場合だけです。具体的には、V2の拡張APIで書いたC#カスタムツールがある場合か、`uloop` を呼び出す自作の `SKILL.md`・Markdownドキュメント・シェルスクリプト・PowerShellスクリプトがある場合です。該当する場合は、手作業で直し始める前に [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を読んでください。

## ハイライト

- **ネイティブGo CLI、Node.js不要** — `uloop` はnpmパッケージではなくプラットフォーム別バイナリとして配布されます。V3プロジェクトを動かすのにNode.js 22以降は不要になりました。
- **`pause-point` — コードに触れずにブレークポイント調査** — 任意のソース行でPlayModeを停止し、そのフレームの変数を読み取れます。`Debug.Log` を仕込む必要も再コンパイルも不要で、PlayMode実行中に仕掛けることもできます。
- **ポート管理の廃止** — CLIはUnixドメインソケット（macOS/Linux）または名前付きパイプ（Windows）でUnityに接続します。設定すべきポートは存在せず、他のEditorインスタンスとポートが衝突することもありません。
- **バージョン互換性はprotocol versionで保証** — CLIとUnityパッケージは整数のprotocol versionで合意します。組み合わせが不一致なら、実行時に不可解な動作をするのではなく、明確なメッセージで即座に失敗します。
- **プロジェクト単位のCLIバージョン解決** — 各プロジェクトが必要なrunnerのバージョンを `.uloop/project-runner-pin.json` にpinするため、異なるバージョンの複数プロジェクトを1台のマシンで共存させられます。

## 新しいツール

### `pause-point` — 任意の行で止めてフレームを読む

`pause-point` は、コンパイル済みのメソッドをソースの `file:line` の位置でパッチし、実行がその行に到達したときにUnityを停止します。バグ調査のためにソースを編集する必要も、再コンパイルする必要もありません。ヒット時のレスポンスには `CapturedVariables` が含まれます。これはメソッドのローカル変数・引数・`this` のインスタンスフィールドを、対象行が実行される直前に取得したもので、IDEのブレークポイントとまったく同じタイミングです。値はライブ参照ではなく、その時点の文字列として記録されるため、Unityが再開した後も証拠として有効なまま残ります。

マーカーには3つのキャプチャモードがあります。`single-shot`（デフォルト）は最初のヒットで自動的に解除され、`continuous` はヒットのたびに停止して過去フレームの履歴を保持し、`trace` は停止せずにヒットだけを記録し続けます。watch式（`uloop enable-watch` / `uloop get-watch-values`）は、停止中のEditor Stepごとに自動で再評価されるため、値がフレーム単位でどう変化するかを追えます。なお、EditorのCode OptimizationモードはDebugである必要があります。Releaseの場合は、対処方法を示したメッセージとともに有効化が拒否されます。

```bash
# pause pointの仕掛け・入力の発火・ヒット待ちを1コマンドで実行
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 \
  --await --trigger "simulate-keyboard --action Press --key Space"

# 現在のマーカー状態を確認し、クリアする
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"
```

### `raycast` — Game View座標に何が当たるかを調べる

`raycast` は `Camera.main` からGame Viewの座標へレイを飛ばし、3D物理で何にヒットするかを返します。座標系は `simulate-mouse-ui` と同じ左上原点なので、注釈付きスクリーンショットの座標をそのまま渡して、クリックする前に対象を確認できます。レスポンスには、ヒットしたGameObjectの名前とパス、レイヤー、距離、ヒット位置、ヒット法線が含まれます。

ヒットした場合もしなかった場合も `CameraName` と `CameraPath` が返るので、想定外の `No physics hit` を診断するときはここを最初に見るのが近道です。シーン内の別のカメラに `MainCamera` タグが付いていると、そちらが `Camera.main` の解決に勝ってしまい、意図した視点からレイが飛んでいない場合があります。なお、これはUnity Physicsのレイキャストであり、UI EventSystemのレイキャストではありません。

```bash
uloop raycast --x 960 --y 540
uloop raycast --x 960 --y 540 --layer-mask 1
```

> `set-game-view-size` もV3で追加されました。Game Viewのカスタム解像度を取得・設定でき（`uloop set-game-view-size --width 1920 --height 1080`）、`screenshot --capture-mode rendering` の座標系を実行ごとに安定させたいときに使えます。

## CLI・配布方法の変更

- **npmパッケージからネイティブバイナリへ** — CLIはnpm経由ではなく、署名済みのプラットフォーム別バイナリとして配布されます。V2の `uloop-cli` npmパッケージは不要になったので、インストーラが削除できなかった場合は `npm uninstall -g uloop-cli` で削除してください。
- **2層構成** — `PATH` 上に置かれるグローバルな `uloop` dispatcherが1つあり、プロジェクトごとの `uloop-project-runner` に処理を委譲します。runnerのバージョンは各プロジェクトの `.uloop/project-runner-pin.json` から決まり、バージョン別のユーザーキャッシュへ自動的にダウンロードされます。そのため、あるプロジェクトを更新しても他のプロジェクトには影響しません。
- **インストーラの真正性検証** — リリース成果物にはsigstoreのattestationが付いています。ドキュメント化されたインストール手順では、インストーラを実行する前に `gh attestation verify` で署名ワークフローとリリースタグのコミットに対して検証します。
- **ランタイム出力の上限** — `.uloop/outputs/` の各サブフォルダは20ファイルまでに制限され、古いものから削除されます。スクリーンショット・テスト結果・ヒエラルキーダンプが無制限に溜まり続けることはなくなりました。
- **V2プロジェクトへの自動委譲** — V3 dispatcherは、プロジェクトがまだV2パッケージに解決されていることを検出すると、対応するV2 `uloop-cli` リリースをバージョン別キャッシュへ導入し、コマンドを転送します。V2とV3のプロジェクトを併用する場合は、V3 dispatcherをインストールしたままにしておくのが正しい運用です。ただし委譲そのものはnpmを経由するため、Node.js 22以降が必要です。

## V3で削除されたもの

- **MCP接続** — 削除されました。CLIとバンドルSkillsを組み合わせて使ってください。MCPで公開していた機能はすべて `uloop` コマンドから利用できます。
- **シェル補完** — 削除されました。既存のシェル設定がエラーにならないよう、`uloop completion` は何もしないスタブとしてのみ残っています。
- **`capture-window`** — `screenshot` を使ってください。役割が統合され、さらにGame Viewのレンダリングキャプチャや要素の注釈機能が追加されています。
- **`unity-search`, `get-unity-search-providers`, `get-provider-details`** — 削除されました。Unity Search APIが必要な場合は `execute-dynamic-code` から直接呼び出すか、通常のシーン内検索なら `find-game-objects` を使ってください。
- **`execute-menu-item`, `get-menu-items`** — 削除されました。`execute-dynamic-code` で `EditorApplication.ExecuteMenuItem(...)` を呼び出してください。

## 破壊的変更

- **booleanオプションが値を取らなくなりました。** V2では `--flag true` や `--flag=false` を受け付けていましたが、V3では値なしのフラグ形式です。デフォルトがtrueのオプションには、代わりに否定形が用意されました。たとえば `uloop compile --wait-for-domain-reload false` は `uloop compile --no-wait-for-domain-reload` になり、`--wait-for-domain-reload true` は単に削除するだけです。各フラグのデフォルトは `uloop <command> --help` で確認できます（`default: enabled` / `default: disabled`）。
- **コマンドの削除。** 上記で挙げたコマンドは廃止されました。それらを呼び出しているスクリプトやスキルは、代替手段に合わせて書き換える必要があります。
- **カスタムツールAPIのnamespaceと型名が変わりました。** カスタムツールは `io.github.hatayama.UnityCliLoop.ToolContracts` 名前空間の `UnityCliLoopTool<TSchema, TResponse>` を継承する形になりました。V2 APIで書いたC#カスタムツールがプロジェクトにある場合、V3に上げると**必ずコンパイルエラーが発生します**。これは想定内の挙動で、内蔵の移行ウィザードが該当ファイルを自動で書き換えるので、手作業で直し始めないでください。詳細は [カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を参照してください。
