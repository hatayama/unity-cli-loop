# Native CLI Simplification Plan

## 背景

`Packages/src/Cli~` は小さな Go CLI アプリケーションです。現在の構成は Clean Architecture の語彙に寄せて `presentation`、`application`、`ports`、`adapters` に分かれていますが、CLI の規模に対して少し抽象化が強くなっています。

次の整理では、Clean Architecture の層名を守ることよりも、CLI として実際に何をしているかが読み取りやすい構成を優先します。

## 現状の違和感

現在の `internal/presentation` は、実質的には CLI の入出力層です。ここには引数解釈、help、completion、stdout/stderr、tool list、skills、update、Unity RPC 実行の入口がまとまっています。

このまとまり自体は間違いではありませんが、`presentation` という名前だけでは terminal CLI の責務が伝わりづらく、さらに `skills` や `update` など独立した機能も同じ package に混ざっています。

また、`internal/application/tool_dispatcher.go` と `internal/ports/unity_bridge.go` は、現時点では抽象化のための抽象化に近くなっています。Unity 以外の送信先があるわけではないため、最初から port/interface を置くより、実際の痛みが出てから抽象化する方がシンプルです。

## 目指す構成

ゼロから設計するなら、抽象レイヤー名ではなく機能名で分けます。

```text
Packages/src/Cli~
├── cmd/uloop/
├── internal/
│   ├── cli/          # args, help, completion, stdout/stderr, command routing
│   ├── unityipc/     # TCP connection, framing, Unity RPC client
│   ├── project/      # project root, cache path, Unity project discovery
│   ├── skills/       # skill discovery, install, sync
│   ├── update/       # self update, installer URL, release tag selection
│   ├── catalog/      # default-tools.json, tool list/cache model
│   ├── version/      # semver compare
│   └── contract/     # contract.json, CLI version contract
└── dist/
```

この構成では、読み手は「CLI の入力処理」「Unity との IPC」「skill 管理」「update 処理」のように、実際の機能単位でコードを探せます。

## 段階的な進め方

一気に全部を動かすのではなく、小さい commit に分けて進めます。

1. `internal/presentation` を `internal/cli` に rename する。
2. `skills_*` 系の処理を `internal/skills` へ分離する。
3. `update.go` と installer release tag 選択を `internal/update` へ分離する。
4. `adapters/unity` と `adapters/framing` を `internal/unityipc` に寄せる。
5. `adapters/project` を `internal/project` に寄せる。
6. `default-tools.json` と tool catalog/cache 周辺を `internal/catalog` に分けるか判断する。
7. `internal/application` と `internal/ports` がまだ必要か見直し、不要なら削除する。

## やらないこと

この整理では、大きな設計変更や互換性維持用の抽象化は追加しません。

- Unity 以外の backend を想定した interface は先に作らない。
- HTTP/TUI など、存在しない presentation surface を想定した階層は作らない。
- pure value transform だけの小さな処理を無理に service 化しない。
- public command の挙動や出力形式は変えない。

## 検証方針

各ステップでは、少なくとも次を確認します。

```bash
scripts/check-go-cli.sh
Packages/src/Cli~/dist/darwin-arm64/uloop compile --wait-for-domain-reload
```

skill discovery や CLI-only skill source path を動かした場合は、`ToolSkillSynchronizerTests` の targeted EditMode test も実行します。

```bash
Packages/src/Cli~/dist/darwin-arm64/uloop run-tests --test-mode EditMode --filter-type regex --filter-value ToolSkillSynchronizerTests
```
