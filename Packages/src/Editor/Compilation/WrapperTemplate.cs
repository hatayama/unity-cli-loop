using System.Collections.Generic;
using System.Text;

namespace io.github.hatayama.uLoopMCP
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
            if (!HasObjectAlias(usingDirectives))
            {
                sb.AppendLine("using Object = UnityEngine.Object;");
            }

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

        private static bool HasObjectAlias(IReadOnlyList<string> usingDirectives)
        {
            if (usingDirectives == null)
            {
                return false;
            }

            for (int index = 0; index < usingDirectives.Count; index++)
            {
                string directive = usingDirectives[index]?.TrimStart();
                if (string.IsNullOrEmpty(directive))
                {
                    continue;
                }

                if (IsObjectAliasDirective(directive))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsObjectAliasDirective(string directive)
        {
            int usingPosition = 0;
            if (SourceShaper.StartsWithKeyword(directive, usingPosition, "global"))
            {
                usingPosition = SkipWhitespaceAndComments(directive, usingPosition + "global".Length);
            }

            if (!SourceShaper.StartsWithKeyword(directive, usingPosition, "using"))
            {
                return false;
            }

            int aliasStart = SkipWhitespaceAndComments(directive, usingPosition + "using".Length);
            if (!SourceShaper.StartsWithKeyword(directive, aliasStart, "Object"))
            {
                return false;
            }

            int equalsPosition = SkipWhitespaceAndComments(directive, aliasStart + "Object".Length);
            return equalsPosition < directive.Length && directive[equalsPosition] == '=';
        }

        private static int SkipWhitespaceAndComments(string source, int position)
        {
            int currentPosition = SourceShaper.SkipWhitespace(source, position);
            while (IsCommentStart(source, currentPosition))
            {
                int nextPosition = SourceShaper.AdvanceOneTokenPublic(source, currentPosition);
                currentPosition = SourceShaper.SkipWhitespace(source, nextPosition);
            }

            return currentPosition;
        }

        private static bool IsCommentStart(string source, int position)
        {
            return position + 1 < source.Length &&
                   source[position] == '/' &&
                   (source[position + 1] == '/' || source[position + 1] == '*');
        }
    }
}
