# execute-dynamic-code Run Log

2026-04-16 にターミナルから実行した `uloop execute-dynamic-code` のコード一覧。

## Single Snippet Benchmark

最初の計測では、次のコードを 5 回連続で実行した。

```csharp
using UnityEngine; return Mathf.PI;
```

## Distinct Snippet Benchmark

次の 5 本のコードを 1 回ずつ実行する計測を行い、その後まったく同じ 5 本で再計測した。

### 1. PI

```csharp
using UnityEngine; return Mathf.PI;
```

### 2. Vector Magnitude

```csharp
using UnityEngine; Vector3 v = new Vector3(3f, 4f, 0f); return v.magnitude;
```

### 3. Unity Version

```csharp
using UnityEngine; return Application.unityVersion;
```

### 4. Play Mode State

```csharp
using UnityEditor; return EditorApplication.isPlaying;
```

### 5. Active Scene Name

```csharp
using UnityEditor.SceneManagement; return EditorSceneManager.GetActiveScene().name;
```

## Notes

- `Distinct Snippet Benchmark` の 5 本は 2 回連続で同じコードを使用した。
- `Unity Version` の戻り値は計測タイミングによって `6000.2.10f1` と `6000.3.13f1` の 2 パターンを確認した。
