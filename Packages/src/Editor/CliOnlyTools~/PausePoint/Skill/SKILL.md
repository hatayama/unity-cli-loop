---
name: uloop-wait-for-debug-break
description: "Unityを一時停止し、停止したフレームで次のような事を実現する。(1)変数・Hierarchyの状態の調査 (2)スクリーンショットを確実に撮影する
---

## 手順

1. 調べたい状態にマーカーを追加する:

```csharp
using io.github.hatayama.UnityCliLoop.Runtime;

UnityCliLoopDebug.Break("player-jumped");
```

2. プロジェクトをコンパイルする。
3. 対象コードパスを発火させる前にマーカーを有効化する:

```bash
uloop enable-debug-break --id player-jumped --timeout-seconds 30
```

4. 必要ならマーカー状態を確認する:

```bash
uloop debug-break-status --id player-jumped
```

5. `simulate-keyboard`、`simulate-mouse-input`、UI操作、dynamic codeなどで挙動を発火させる。
6. マーカーを待つ:

```bash
uloop wait-for-debug-break --id player-jumped --timeout-seconds 30
```

このコマンドがタイムアウトした場合、コマンドの待機中にマーカー行へ到達していない。`error.details.status`、`hitCount`、`isPlaying`、`isPaused`、`elapsedSinceEnabledMilliseconds`、`remainingMilliseconds` を見て、入力が消費されていないのか、ゲームプレイ条件を満たしていないのか、idが一致していないのか、Unityがすでに一時停止しているのかを切り分ける。`elapsedSinceEnabledMilliseconds` は `wait-for-debug-break` からではなく、`enable-debug-break` から計測される。

7. Unityが一時停止している間に、`uloop get-logs`、`uloop get-hierarchy`、`uloop find-game-objects`、スクリーンショット、または `uloop execute-dynamic-code` で状態を調べる。
8. 待機をやめる場合はマーカーをクリアする:

```bash
uloop clear-debug-break --id player-jumped
```

## マーカー配置

- 入力が消費された後の自然なゲームプレイ地点や状態遷移地点を優先する。例として、ジャンプ速度や状態の変更後、物理接触後、ダメージ適用後など。
- フレーム固有のバグでは、疑わしい状態分岐、または止めて調べたい状態変更の直後にマーカーを置く。
- Domain Reloadによる消失やツールのBusy状態を避けるため、Play Mode実行後にマーカーを有効化し、発火させる入力コマンドがreturnできた後に到達するチェックポイントを優先する。
- その入力処理行自体を調べる必要がある場合を除き、疑似入力を発行した直後にマーカーを置くのは避ける。即時マーカーは、結果のゲームプレイ状態が安定する前に入力コマンドを中断することがある。
- 1つの広いマーカーを使い回すのではなく、厳密なフェーズごとに別々のidを使う。例: `jump-input-read`、`jump-velocity-applied`、`jump-landed`。

## 安全性

- custom asmdef内のコードで `UnityCliLoopDebug.Break` を使うには、`UnityCLILoop.PausePoints.Runtime` を参照する必要がある。
- id引数には副作用のある式を渡さない。安定した文字列idを使う。
- この機能はログや状態スナップショットを収集しない。Unityが一時停止した後、既存の調査コマンドを使う。
- PlayMode前に `enable-debug-break` がDomain Reloadについて警告する場合、PlayMode開始時にマーカーがクリアされる可能性がある。このworkflowにはDomain Reload無効が適している。有効な場合は、PlayMode開始後にもう一度有効化する。
