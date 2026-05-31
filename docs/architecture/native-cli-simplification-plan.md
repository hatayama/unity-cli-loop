# Native CLI Simplification Plan

## 背景

`cli` は小さな Go CLI アプリケーションです。以前の構成は Clean Architecture の語彙に寄せて `presentation`、`application`、`ports`、`adapters` に分かれていましたが、CLI の規模に対して抽象化が強くなっていました。

この整理では、Clean Architecture の層名を守ることよりも、CLI として実際に何をしているかが読み取りやすい構成を優先します。ただし SOLID、デメテルの法則、凝集度は維持し、特に機能的凝集を優先します。

## 現状の違和感

旧 `internal/presentation` は、実質的には CLI の入出力層でした。ここには引数解釈、help、completion、stdout/stderr、tool list、skills、update、Unity RPC 実行の入口がまとまっていました。

このまとまり自体は間違いではありませんが、`presentation` という名前だけでは terminal CLI の責務が伝わりづらく、さらに `tools` や `update` など独立した機能も同じ package に混ざっていました。

また、`internal/application/tool_dispatcher.go` と `internal/ports/unity_bridge.go` は、抽象化のための抽象化に近い状態でした。Unity 以外の送信先があるわけではないため、最初から port/interface を置くより、実際の痛みが出てから抽象化する方がシンプルです。

## 目指す構成

ゼロから設計するなら、抽象レイヤー名ではなく機能名で分けます。

```text
cli
├── cmd/uloop/
├── internal/
│   ├── cli/          # args, help, completion, stdout/stderr, command routing
│   ├── unityipc/     # connection, framing, Unity RPC client
│   ├── project/      # project root, cache path, Unity project discovery
│   ├── skills/       # CLI-only skill sources and skill file-layout knowledge
│   ├── update/       # self update, installer URL, release tag selection
│   ├── tools/        # default-tools.json, tool list/cache DTO and loading
│   └── version/      # semver compare
├── contract.json     # CLI version contract exposed by root package
└── dist/           # ignored local build and release output
```

この構成では、読み手は「CLI の入力処理」「Unity との IPC」「tool catalog」「update 処理」のように、実際の機能単位でコードを探せます。

## 段階的な進め方

一気に全部を動かすのではなく、小さい commit に分けて進めます。

1. `Core~`、`Dispatcher~`、`Shared~` に分かれていた Go modules を `cli` に統合する。
2. `internal/presentation` を `internal/cli` に rename する。
3. `internal/application` と `internal/ports` を削除し、`cli` から `unityipc.Client` を直接呼ぶ。
4. `adapters/unity` と `adapters/framing` を `internal/unityipc` に寄せる。
5. `adapters/project` を `internal/project` に寄せる。
6. `default-tools.json` と tool catalog/cache DTO を `internal/tools` へ移す。
7. CLI-only skill source path を `internal/skills` 配下へ移し、CLI が直接 file layout を持たないようにする。
8. update installer URL と release tag 選択を `internal/update` に寄せる。

## やらないこと

この整理では、大きな設計変更や互換性維持用の抽象化は追加しません。

- Unity 以外の backend を想定した interface は先に作らない。
- HTTP/TUI など、存在しない presentation surface を想定した階層は作らない。
- pure value transform だけの小さな処理を無理に service 化しない。
- public command の挙動や出力形式は変えない。
- package 間の情報の受け渡しでは、できるだけ意味のある DTO を使い、生成後に書き換えない前提で扱う。

## 検証方針

各ステップでは、少なくとも次を確認します。

```bash
scripts/check-go-cli.sh
cli/dist/darwin-arm64/uloop compile --wait-for-domain-reload
```

skill discovery や CLI-only skill source path を動かした場合は、`ToolSkillSynchronizerTests` の targeted EditMode test も実行します。

```bash
cli/dist/darwin-arm64/uloop run-tests --test-mode EditMode --filter-type regex --filter-value ToolSkillSynchronizerTests
```
