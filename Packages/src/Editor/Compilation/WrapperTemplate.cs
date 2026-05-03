using System.Collections.Generic;
using System.Text;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Builds the namespace/class/method wrapper for top-level user code.
    /// Method signatures are compatible with CommandRunner's FindExecuteAsyncMethod.
    /// </summary>
    internal static class WrapperTemplate
    {
        internal const string UserCodeStartMarker = "#line 1 \"user-snippet.cs\"";
        internal const string UserCodeEndMarker = "#line default";

        public static string Build(
            IReadOnlyList<string> usingDirectives,
            string namespaceName,
            string className,
            string body,
            IReadOnlyList<string> preambleLines = null)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("#pragma warning disable CS0162");
            sb.AppendLine("#pragma warning disable CS1998");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");

            foreach (string directive in usingDirectives)
            {
                sb.AppendLine(directive);
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        public async System.Threading.Tasks.Task<object> ExecuteAsync(");
            sb.AppendLine("            System.Collections.Generic.Dictionary<string, object> parameters = null,");
            sb.AppendLine("            System.Threading.CancellationToken ct = default)");
            sb.AppendLine("        {");

            if (preambleLines != null)
            {
                foreach (string preambleLine in preambleLines)
                {
                    sb.AppendLine($"            {preambleLine}");
                }
            }

            sb.AppendLine(UserCodeStartMarker);

            foreach (string line in body.Split('\n'))
            {
                sb.AppendLine($"            {line.TrimEnd('\r')}");
            }

            sb.AppendLine(UserCodeEndMarker);
            sb.AppendLine("#line hidden");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public object Execute(");
            sb.AppendLine("            System.Collections.Generic.Dictionary<string, object> parameters = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return ExecuteAsync(parameters, default).GetAwaiter().GetResult();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
