using System.Collections.Generic;
using System.ComponentModel;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Parameter schema for dynamic code execution tool
    /// Related classes: ExecuteDynamicCodeTool, ExecuteDynamicCodeResponse
    /// </summary>
    public class ExecuteDynamicCodeSchema : BaseToolSchema
    {
        /// <summary>C# code to execute</summary>
        [Description(@"Editor automation only — no file I/O, no script authoring.

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

Don’t:
- System.IO.* (File/Directory/Path)
- AssetDatabase.CreateFolder / file writes
- Create/edit .cs/.asmdef (use Terminal/IDE instead)

Need files/dirs? Run terminal commands.")]
        public string Code { get; set; } = "";
        
        /// <summary>Runtime parameters (advanced; usually unnecessary)</summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        [Description(@"Advanced, optional parameters passed to the snippet.

- For throwaway snippets, prefer defining locals directly in code
- Keys are not preserved; values map to parameters[""param0""], parameters[""param1""], ...
- Use when injecting UnityEngine.Object or sensitive values, or reusing a snippet with varying data
- Use object values supported by the Editor; prefer fully-qualified type names for ambiguous refs (e.g., UnityEngine.Object)

- Input type must be an OBJECT. Strings like ""{}"" are invalid.
- Do: omit 'Parameters' entirely, or use {}
- Don't: ""{}"" (string), ""{""a"":1}"" (string)

Examples:
OK:   ""Parameters"": {}
OK:   (omit the 'Parameters' property)
NG:   ""Parameters"": ""{}""")]
        public Dictionary<string, object> Parameters { get; set; } = new();
        
        /// <summary>Compile only (do not execute)</summary>
        [Description(@"Compile only (no execution).

- Uses Roslyn validation to surface diagnostics
- For new MonoBehaviours: create .cs → compile (mcp__uLoopMCP__compile with ForceRecompile=false) → ensure ErrorCount=0 → AddComponent")]
        public bool CompileOnly { get; set; } = false;

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public bool YieldToForegroundRequests { get; set; } = false;
    }
}
