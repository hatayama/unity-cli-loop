# Custom Tool Development Guide

English | [日本語](custom-tools_ja.md)

Unity CLI Loop enables efficient development of project-specific tools without requiring changes to the core package.
The type-safe design allows for reliable custom tool implementation in minimal time.
(If you ask AI, they should be able to make it for you soon ✨)

You can publish your extension tools on GitHub and reuse them across other projects.

## Implementation Guide

**Step 1: Create Schema Class** (define parameters):
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

**Step 2: Create Response Class** (define return data):
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

    // Required parameterless constructor
    public MyCustomResponse() { }
}
```

**Step 3: Create Tool Class**:
```csharp
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public class MyCustomTool : UnityCliLoopTool<MyCustomSchema, MyCustomResponse>
{
    public override string ToolName => "my-custom-tool";

    // Executed on main thread
    protected override Task<MyCustomResponse> ExecuteAsync(MyCustomSchema parameters, CancellationToken ct)
    {
        // Type-safe parameter access
        string param = parameters.MyParameter;
        MyEnum enumValue = parameters.EnumParameter;

        // Check for cancellation before long-running operations
        ct.ThrowIfCancellationRequested();

        // Implement custom logic here
        string result = ProcessCustomLogic(param, enumValue);
        bool success = !string.IsNullOrEmpty(result);

        // For long-running operations, periodically check for cancellation
        // ct.ThrowIfCancellationRequested();

        return Task.FromResult(new MyCustomResponse(result, success));
    }

    private string ProcessCustomLogic(string input, MyEnum enumValue)
    {
        // Implement custom logic
        return $"Processed '{input}' with enum '{enumValue}'";
    }
}
```

Please also refer to [Custom Tool Samples](../../../Assets/Editor/CustomCommandSamples).

## Custom Skills for Your Tools

When you create a custom tool, you can create a `Skill/` subfolder within the tool folder and place a `SKILL.md` file there. This allows LLM tools to automatically discover and use your custom tool through the Skills system.

**How it works:**
1. Create a `Skill/` subfolder in your custom tool's folder
2. Place `SKILL.md` inside the `Skill/` folder
3. Run `uloop skills install --claude` to install all skills (bundled + project)
4. LLM tools will automatically recognize your custom skill

**Directory structure:**
```text
Assets/Editor/CustomTools/MyTool/
├── MyTool.cs           # Tool implementation
└── Skill/
    ├── SKILL.md        # Skill definition (required)
    └── references/     # Additional files (optional)
        └── usage.md
```

**SKILL.md format:**
```markdown
---
name: uloop-my-custom-tool
description: "Description of what the tool does and when to use it."
---

# uloop my-custom-tool

Detailed documentation for the tool...
```

**Scanned locations** (searches for `Skill/SKILL.md` files):
- `Assets/**/Editor/<ToolFolder>/Skill/SKILL.md`
- `Packages/*/Editor/<ToolFolder>/Skill/SKILL.md`
- `Library/PackageCache/*/Editor/<ToolFolder>/Skill/SKILL.md`

> [!TIP]
> - Add `internal: true` to the frontmatter to exclude a skill from installation (useful for internal/debug tools)
> - Additional files in the `Skill/` folder (such as `references/`, `scripts/`, `assets/`) are also copied during installation

See [HelloWorld sample](../../../Assets/Editor/CustomCommandSamples/HelloWorld/Skill/SKILL.md) for a complete example.

> [!IMPORTANT]
> **If you built custom tools or custom skills on V2**, upgrading to V3 *will* produce compile errors, because the extension API moved to a new namespace with new type names. This is expected — the built-in migration wizard rewrites the affected files for you. Before fixing anything by hand, see [Migrating Custom Tools and Skills to V3](migration-v2-to-v3.md).
