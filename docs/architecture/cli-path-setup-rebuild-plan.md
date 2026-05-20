# CLI PATH Setup Rebuild Plan

## Goal

Unity UI から CLI を入れる初心者向けフローでは、インストール後に fresh login interactive shell で `uloop` が解決できるかを実測し、見えない場合はユーザー操作を追加で求めずに shell 設定ファイルへ最小の PATH 行を追記する。

コマンドライン installer から入れる上級者向けフローでは、shell 設定ファイルを変更せず、`~/.local/bin` が `PATH` に無い場合だけ、ユーザーが自分で追記できる短い案内を表示する。

## Current Branch Reference

参考実装は `/Users/masamichi/work/unity-cli-loop2` の `codex/fix-v3-cli-path-setup-ui` ブランチに置かれている。

そこから再利用する考え方は次の通り。

- Unity の起動時に持っていた stale `PATH` を信用しない。
- fresh login interactive shell で `command -v uloop` と `uloop -v` を実行して判断する。
- CLI installer は shell profile を勝手に変更しない。
- UI からのインストールでは、shell profile 変更を初心者向け自動セットアップの一部として扱う。
- zsh / bash / fish 以外は自動追記しない。
- 追記前には既存設定を確認し、ファイル末尾に改行が無い場合でも行を連結しない。

捨てる複雑さは次の通り。

- shell profile 全体を静的解析して PATH の実効値を推測しない。
- zsh / bash / fish のあらゆる PATH 記法を解釈しようとしない。
- `ZDOTDIR` や login hook の推測を installer script 側へ過剰に持ち込まない。
- UI と Setup Wizard の PATH repair 手順を別々に増やさない。

## Design

### 1. Terminal visibility is authoritative

CLI が terminal から使えるかどうかは、常に shell 実行結果で判断する。

POSIX ではユーザー shell を `-l -i -c` で起動し、`command -v uloop` と `uloop -v` を marker 付きで取得する。Unity プロセスが保持している `PATH` から package install directory を一時的に除外し、Unity 起動時点の stale `PATH` で誤判定しないようにする。

fish の `$status` だけは POSIX shell と異なるため、shell kind ごとの小さい command builder を用意する。

### 2. Profile writes are intentionally boring

自動追記する行は canonical line だけにする。

- zsh / bash: `export PATH="$HOME/.local/bin:$PATH"`
- fish: `fish_add_path "$HOME/.local/bin"`

既存設定判定は次の順で十分とする。

1. 自分たちが書く canonical line がコメントでない行として存在する。
2. コメントでない行に install directory の literal path または `$HOME` 形式が PATH 設定として含まれる。

この判定は「重複追記を避けるための軽い確認」であり、正しさの最終判断には使わない。最終判断は追記後の fresh shell visibility check で行う。

### 3. Profile target selection is simple

自動追記先は shell kind ごとに限定する。

- zsh: `$ZDOTDIR/.zshrc` が使えるならそれを使う。`ZDOTDIR` が無ければ `$HOME/.zshrc`。
- bash: 既存の `$HOME/.bash_profile`、`$HOME/.bash_login`、`$HOME/.profile` の順で選び、無ければ `$HOME/.bash_profile`。
- fish: `${XDG_CONFIG_HOME:-$HOME/.config}/fish/config.fish`。

`ZDOTDIR` を login shell から探りに行く処理は採用しない。必要なら将来、実際の不具合が出た時に小さく追加する。

### 4. One application flow owns PATH setup

UI と Setup Wizard は、それぞれが shell profile の詳細を知らない。

共通の application service が次を行う。

1. CLI install または repair ボタン押下を受ける。
2. fresh shell visibility check を実行する。
3. 見えなければ shell kind に応じた profile plan を作る。
4. 対応 shell なら profile に追記する。
5. もう一度 fresh shell visibility check を実行する。
6. 成功、未対応 shell、書き込み失敗、追記済みだが未解決、の結果を UI に返す。

### 5. Command installer remains read-only for profiles

`scripts/install.sh` は `~/.local/bin` へ CLI を置く。`PATH` に含まれていない場合は案内だけ出す。

案内は zsh / bash / fish ごとに最低限の追記コマンドを表示する。設定ファイルの探索や推測は UI 側ほど行わない。

## ToDo

- [ ] 参照 checkout の差分から必要な挙動だけを抽出する。
- [ ] 方針ファイルを先に commit する。
- [ ] `CliPathSetupPlan` 系 DTO を小さく作る。
- [ ] `CliPathSetupProfileResolver` を作り、shell kind と設定ファイルだけを決める。
- [ ] `CliPathSetupWriter` を作り、canonical line の重複防止と newline-safe append を行う。
- [ ] `CliTerminalVisibilityChecker` を作り、fresh shell 実測を担当させる。
- [ ] `CliSetupApplicationService` に install / repair 後の PATH setup flow を集約する。
- [ ] Settings Window と Setup Wizard は共通 flow の結果表示だけに寄せる。
- [ ] `scripts/install.sh` を profile 非変更、案内のみの実装へ更新する。
- [ ] C# unit tests を Red-Green-Refactor で追加する。
- [ ] shell script tests を追加し、profile を作成・変更しないことを確認する。
- [ ] `sh -n scripts/install.sh` を通す。
- [ ] `scripts/test-install-release-filter.sh` を通す。
- [ ] focused EditMode tests を通す。
- [ ] `Packages/src/Cli~/dist/darwin-arm64/uloop compile --project-path /Users/masamichi/work/unity-cli-loop` を通す。
- [ ] 関心ごとごとに commit する。
- [ ] commit 後に difit を表示する。

## Completion Criteria

- 新ブランチが `origin/v3-beta` 由来である。
- この方針ファイルが実装前の判断材料として残っている。
- UI install / repair は stale Unity `PATH` を信用せず、fresh shell で `uloop` が見えない時だけ PATH setup を実行する。
- UI 自動追記は zsh / bash / fish のみで行う。
- 未対応 shell は自動追記せず、manual command を表示できる。
- command installer は shell profile を作成・変更しない。
- profile 追記は末尾改行なしファイルでも安全に行う。
- 重複判定は canonical line または明確な install directory PATH 設定だけを対象にする。
- 参照 checkout にあった巨大な shell profile 静的解析を持ち込まない。
- 検証コマンドが通り、commit が作成されている。
