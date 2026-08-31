# カスタムツール開発ガイド

[English](custom-tools.md) | 日本語

Unity CLI Loopはコアパッケージへの変更を必要とせず、プロジェクト固有のツールを効率的に開発できます。
型安全な設計により、信頼性の高いカスタムツールを短時間で実装可能です。
(AIに依頼すればすぐに作ってくれるはずです✨)

開発した拡張ツールはGitHubで公開し、他のプロジェクトでも再利用できます。

## 実装ガイド

**ステップ1: スキーマクラスの作成**（パラメータを定義）：
```csharp
using io.github.hatayama.UnityCliLoop.ToolContracts;

public class MyCustomSchema : UnityCliLoopToolSchema
{
    public string MyParameter { get; set; } = "default_value";

    public MyEnum EnumParameter { get; set; } = MyEnum.Option1;
}

public enum MyEnum
{
    Option1 = 0,
    Option2 = 1,
    Option3 = 2
}
```

**ステップ2: レスポンスクラスの作成**（返却データを定義）：
```csharp
using io.github.hatayama.UnityCliLoop.ToolContracts;

public class MyCustomResponse : UnityCliLoopToolResponse
{
    public string Result { get; set; }
    public bool Success { get; set; }

    public MyCustomResponse(string result, bool success)
    {
        Result = result;
        Success = success;
    }

    // 必須のパラメータなしコンストラクタ
    public MyCustomResponse() { }
}
```

**ステップ3: ツールクラスの作成**：
```csharp
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public class MyCustomTool : UnityCliLoopTool<MyCustomSchema, MyCustomResponse>
{
    public override string ToolName => "my-custom-tool";

    // メインスレッドで実行されます
    protected override Task<MyCustomResponse> ExecuteAsync(MyCustomSchema parameters, CancellationToken ct)
    {
        // 型安全なパラメータアクセス
        string param = parameters.MyParameter;
        MyEnum enumValue = parameters.EnumParameter;

        // 長時間実行される処理の前にキャンセレーションをチェック
        ct.ThrowIfCancellationRequested();

        // カスタムロジックをここに実装
        string result = ProcessCustomLogic(param, enumValue);
        bool success = !string.IsNullOrEmpty(result);

        // 長時間実行される処理では定期的にキャンセレーションをチェック
        // ct.ThrowIfCancellationRequested();

        return Task.FromResult(new MyCustomResponse(result, success));
    }

    private string ProcessCustomLogic(string input, MyEnum enumValue)
    {
        // カスタムロジックを実装
        return $"Processed '{input}' with enum '{enumValue}'";
    }
}
```

[カスタムツールのサンプル](../../../Assets/Editor/CustomCommandSamples)も参考にして下さい。

## カスタムツール用 Skills

カスタムツールを作成した際、ツールフォルダ内に `Skill/` サブフォルダを作成し、`SKILL.md` ファイルを配置することで、LLMツールがSkillsシステムを通じて自動的にカスタムツールを認識・使用できるようになります。

**仕組み:**
1. カスタムツールのフォルダ内に `Skill/` サブフォルダを作成
2. `Skill/` フォルダ内に `SKILL.md` ファイルを配置
3. `uloop skills install --claude` を実行（バンドル + プロジェクトのSkillsをまとめてインストール）
4. LLMツールがカスタムSkillを自動認識

**ディレクトリ構造:**
```text
Assets/Editor/CustomTools/MyTool/
├── MyTool.cs           # ツール実装
└── Skill/
    ├── SKILL.md        # スキル定義（必須）
    └── references/     # 追加ファイル（オプション）
        └── usage.md
```

**SKILL.md のフォーマット:**
```markdown
---
name: uloop-my-custom-tool
description: "ツールの説明と使用タイミング"
---

# uloop my-custom-tool

ツールの詳細ドキュメント...
```

**スキャン対象**（`Skill/SKILL.md` ファイルを検索）:
- `Assets/**/Editor/<ToolFolder>/Skill/SKILL.md`
- `Packages/*/Editor/<ToolFolder>/Skill/SKILL.md`
- `Library/PackageCache/*/Editor/<ToolFolder>/Skill/SKILL.md`

> [!TIP]
> - フロントマターに `internal: true` を追加すると、インストール対象から除外されます（内部ツールやデバッグ用ツールに便利）
> - `Skill/` フォルダ内の追加ファイル（`references/`、`scripts/`、`assets/` など）もインストール時に一緒にコピーされます

完全な例は [HelloWorld サンプル](../../../Assets/Editor/CustomCommandSamples/HelloWorld/Skill/SKILL.md) を参照してください。

> [!IMPORTANT]
> **V2でカスタムツールやカスタムスキルを作っていた場合**、V3に上げると拡張APIの名前空間と型名が変わるため、**必ずコンパイルエラーが発生します**。これは想定内の挙動で、内蔵の移行ウィザードが該当ファイルを自動で書き換えます。手作業で直し始める前に、[カスタムツール／スキルのV3移行ガイド](migration-v2-to-v3_ja.md) を参照してください。
