# execute-dynamic-code Examples

`uloop execute-dynamic-code` をターミナルから試すためのサンプル集。

## Quick Start

まずは短いコードで疎通を確認する。

```sh
uloop execute-dynamic-code --code 'return "hello";'
```

実行速度を体感したい時は `time` を付けて複数回打つ。

```sh
time uloop execute-dynamic-code --code 'return "first";'
time uloop execute-dynamic-code --code 'return "second";'
time uloop execute-dynamic-code --code 'return "third";'
```

## Short Examples

単純な計算。

```sh
uloop execute-dynamic-code --code 'return 1 + 2 + 3;'
```

配列を組み立てて返す。

```sh
uloop execute-dynamic-code --code 'string[] names = { "A", "B", "C" }; return string.Join(", ", names);'
```

Unity バージョンを返す。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; return Application.unityVersion;'
```

現在の Scene 名を返す。

```sh
uloop execute-dynamic-code --code 'using UnityEngine.SceneManagement; return SceneManager.GetActiveScene().name;'
```

GameObject を作って名前を返す。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; GameObject go = new GameObject("SampleCube"); go.AddComponent<BoxCollider>(); return go.name;'
```

パラメータ付きで掛け算する。

```sh
uloop execute-dynamic-code --code 'int a = (int)parameters[0]; int b = (int)parameters[1]; return a * b;' --parameters '[6,7]'
```

`compile-only` を試す。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("compile only sample"); return 123;' --compile-only
```

## Medium Examples

Camera 情報を列挙する。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None); System.Text.StringBuilder sb = new System.Text.StringBuilder(); foreach (Camera cam in cameras) { sb.AppendLine(cam.name + " depth=" + cam.depth); } return sb.ToString();'
```

Renderer の数を集計する。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None); int enabledCount = 0; foreach (Renderer r in renderers) { if (r.enabled) { enabledCount++; } } return "renderers=" + renderers.Length + ", enabled=" + enabledCount;'
```

ルート GameObject 名を並べる。

```sh
uloop execute-dynamic-code --code 'using UnityEngine; using UnityEngine.SceneManagement; GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects(); System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>(); foreach (GameObject root in roots) { names.Add(root.name); } return string.Join(", ", names);'
```

## Long Examples

長いコードは変数に入れてから渡すと扱いやすい。

Scene 全体をざっと調べる例。

```sh
CODE=$(cat <<'EOF'
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;

Scene scene = SceneManager.GetActiveScene();
GameObject[] roots = scene.GetRootGameObjects();
StringBuilder sb = new StringBuilder();

sb.AppendLine("Scene: " + scene.name);
sb.AppendLine("Root count: " + roots.Length);

foreach (GameObject root in roots)
{
    sb.AppendLine("- " + root.name);

    Transform[] children = root.GetComponentsInChildren<Transform>(true);
    sb.AppendLine("  transforms=" + children.Length);

    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
    sb.AppendLine("  renderers=" + renderers.Length);
}

return sb.ToString();
EOF
)
uloop execute-dynamic-code --code "$CODE"
```

特定オブジェクトを探して移動する例。

```sh
CODE=$(cat <<'EOF'
using UnityEngine;

GameObject target = GameObject.Find("Main Camera");
if (target == null)
{
    return "Main Camera not found";
}

Vector3 before = target.transform.position;
target.transform.position = before + new Vector3(0f, 1f, 0f);
Vector3 after = target.transform.position;

return "moved: " + before + " -> " + after;
EOF
)
uloop execute-dynamic-code --code "$CODE"
```

Hierarchy をフラットに列挙する例。

```sh
CODE=$(cat <<'EOF'
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

List<string> lines = new List<string>();
GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

foreach (GameObject root in roots)
{
    Transform[] all = root.GetComponentsInChildren<Transform>(true);
    foreach (Transform t in all)
    {
        lines.Add(t.name + " | activeSelf=" + t.gameObject.activeSelf);
    }
}

return string.Join("\n", lines);
EOF
)
uloop execute-dynamic-code --code "$CODE"
```

コンポーネントを追加して設定する例。

```sh
CODE=$(cat <<'EOF'
using UnityEngine;

GameObject host = GameObject.Find("DynamicHost");
if (host == null)
{
    host = new GameObject("DynamicHost");
}

Rigidbody rb = host.GetComponent<Rigidbody>();
if (rb == null)
{
    rb = host.AddComponent<Rigidbody>();
}

rb.useGravity = false;
rb.mass = 2.5f;

return host.name + " mass=" + rb.mass + " useGravity=" + rb.useGravity;
EOF
)
uloop execute-dynamic-code --code "$CODE"
```

## Tips

- `launch -r` 直後は `prewarm` がまだ終わっていないことがある
- 速度比較をしたい時は、同じコマンドを `time` 付きで 2 回以上打つ
- 長いコードは `CODE=$(cat <<'EOF' ... EOF)` 形式が扱いやすい
