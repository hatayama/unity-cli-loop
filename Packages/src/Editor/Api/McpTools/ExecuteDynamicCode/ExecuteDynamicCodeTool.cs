using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Dynamic C# code execution tool exposed through the CLI.
    /// Delegates workflow orchestration to the dedicated execute-dynamic-code use case.
    /// </summary>
    [McpTool(Description = @"Execute C# code dynamically in Unity Editor for editor automation.

Use find-game-objects first for basic selected GameObject discovery or property inspection. Use this after built-in inspection tools are not enough or when you need to modify Unity state.

Direct statements only (no classes/namespaces/methods); return is optional (auto 'return null;' if omitted).

You may include using directives at the top; they are hoisted above the wrapper.
Example:
  using UnityEngine;
  var x = Mathf.PI;
  return x;

Do:
- Prefab/material wiring (PrefabUtility)
- AddComponent + reference wiring (SerializedObject)
- Scene/hierarchy edits

Don't:
- System.IO.* (File/Directory/Path)
- AssetDatabase.CreateFolder / file writes
- Create/edit .cs/.asmdef (use Terminal/IDE instead)

Need files/dirs? Run terminal commands.

See examples at {project_root}/.claude/skills/unity-cli-loop/uloop-execute-dynamic-code/examples/")]
    public class ExecuteDynamicCodeTool : AbstractUnityTool<ExecuteDynamicCodeSchema, ExecuteDynamicCodeResponse>
    {
        public override string ToolName => "execute-dynamic-code";

        protected override async Task<ExecuteDynamicCodeResponse> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters, 
            CancellationToken cancellationToken)
        {
            IExecuteDynamicCodeUseCase useCase = await DynamicCodeServices.GetExecuteDynamicCodeUseCaseAsync();
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
}
