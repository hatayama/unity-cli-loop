using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ToolParameterInfo = io.github.hatayama.UnityCliLoop.ToolContracts.ParameterInfo;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the skill parameter tables document every parameter the tools
    /// actually accept.
    /// Why this lives in C#: `--help`, `uloop list` and the embedded catalog all render from those
    /// tables now, so a parameter with no table row is a parameter no agent can discover - and the set
    /// of accepted parameters is decided by the C# schema types, which no Go test can see. The reverse
    /// direction (a row for an option no tool accepts) is caught by the catalog generator instead.
    /// </summary>
    public sealed class SkillParameterTableCoverageTests
    {
        private const string DefaultToolsPath = "cli/common/tools/default-tools.json";
        private const string PackageEditorPath = "Packages/src/Editor";
        private const string SkillFileName = "SKILL.md";
        private const string SkillDirectoryName = "Skill";
        private const string SkillNamePrefix = "uloop-";
        private const string ParametersSectionHeading = "## Parameters";
        private const string SectionHeadingPrefix = "## ";
        private const string SubsectionHeadingPrefix = "### ";
        private const string FrontmatterFence = "---";

        // Why: the package keeps first-party tool skills and CLI-only command skills in two containers,
        // and the tilde suffix hides the second one from Unity's asset database (so those files need no
        // .meta). Both are read here because Unity-side tools are documented in both: the pause-point
        // commands live under CliOnlyTools~ even though Unity accepts their parameters.
        private static readonly string[] SkillContainerDirectories = { "FirstPartyTools", "CliOnlyTools~" };

        private static readonly string[] StandardParameterTableCells = { "Parameter", "Type", "Default", "Description" };

        /// <summary>
        /// Verifies every parameter a live tool accepts has a matching row in its skill's parameter
        /// table, so adding a schema property without documenting it fails here instead of silently
        /// producing an option that help cannot describe.
        /// </summary>
        [Test]
        public void EverySchemaParameter_HasASkillTableRow()
        {
            Dictionary<string, SkillDocumentation> skills = ReadSkillDocumentation();
            HashSet<string> hiddenProperties = ReadHiddenPropertyKeys();
            HashSet<string> firstPartyToolNames = new(ReadCatalogToolNames(), StringComparer.Ordinal);
            List<string> problems = new();

            foreach (ToolInfo tool in ReadLiveTools())
            {
                // Why the catalog defines the scope: this development project also registers the custom
                // command samples and test fixtures under Assets/, which are project-local tools with no
                // skill by design. The catalog is exactly the set of commands the CLI ships.
                if (!firstPartyToolNames.Contains(tool.Name))
                {
                    continue;
                }

                if (!skills.TryGetValue(tool.Name, out SkillDocumentation documentation))
                {
                    problems.Add(tool.Name + ": no skill documents this tool");
                    continue;
                }

                foreach (KeyValuePair<string, ToolParameterInfo> property in
                    tool.ParameterSchema.Properties.OrderBy(property => property.Key, StringComparer.Ordinal))
                {
                    if (hiddenProperties.Contains(HiddenPropertyKey(tool.Name, property.Key)))
                    {
                        continue;
                    }

                    string optionName = OptionNameForProperty(tool.Name, property.Key, property.Value);
                    if (documentation.DocumentsOption(optionName))
                    {
                        continue;
                    }

                    problems.Add(tool.Name + " --" + optionName + ": no row in " + documentation.SkillRelativePath);
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        /// <summary>
        /// Verifies every command in the embedded catalog has a non-empty tool description in a skill,
        /// which is what `uloop list` and `--help` print as the command's one-line summary.
        /// </summary>
        [Test]
        public void EveryCatalogTool_HasASkillDescription()
        {
            Dictionary<string, SkillDocumentation> skills = ReadSkillDocumentation();
            List<string> problems = new();

            foreach (string toolName in ReadCatalogToolNames())
            {
                if (!skills.TryGetValue(toolName, out SkillDocumentation documentation))
                {
                    problems.Add(toolName + ": no skill documents this tool");
                    continue;
                }

                if (string.IsNullOrEmpty(documentation.Description))
                {
                    problems.Add(toolName + ": skill " + documentation.SkillRelativePath + " has an empty description");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        private static ToolInfo[] ReadLiveTools()
        {
            UnityCliLoopToolRegistry registry = new UnityCliLoopToolRegistry(
                new AlwaysEnabledToolSettingsPort(),
                internalToolNameProvider: null,
                toolDiscovery: UnityCliLoopToolDiscovery.DiscoverTools);

            return registry.GetRegisteredTools()
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] ReadCatalogToolNames()
        {
            JArray tools = ReadCatalog()["tools"] as JArray ?? new JArray();
            return tools
                .OfType<JObject>()
                .Select(tool => tool["name"]?.ToString() ?? "")
                .Where(name => name.Length > 0)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        // Why the hidden set is read from the catalog rather than from a list in this file: whether an
        // option reaches the command line is a CLI-side decision recorded by the catalog's "hidden"
        // flag, and the catalog generator skips exactly those properties. Reading the same flag keeps
        // one source for it; a second list here could disagree with the generator.
        private static HashSet<string> ReadHiddenPropertyKeys()
        {
            HashSet<string> hiddenKeys = new(StringComparer.Ordinal);
            JArray tools = ReadCatalog()["tools"] as JArray ?? new JArray();
            foreach (JObject tool in tools.OfType<JObject>())
            {
                string toolName = tool["name"]?.ToString() ?? "";
                JObject properties = tool["inputSchema"]?["properties"] as JObject ?? new JObject();
                foreach (JProperty property in properties.Properties())
                {
                    if (property.Value["hidden"]?.Value<bool>() == true)
                    {
                        hiddenKeys.Add(HiddenPropertyKey(toolName, property.Name));
                    }
                }
            }
            return hiddenKeys;
        }

        private static JObject ReadCatalog()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return JObject.Parse(File.ReadAllText(Path.Combine(projectRoot, DefaultToolsPath)));
        }

        private static string HiddenPropertyKey(string toolName, string propertyName)
        {
            return toolName + "/" + propertyName;
        }

        // Why this duplicates cli/common/tooldocs.OptionNameForProperty instead of sharing it: the rule
        // has to run on both sides of the language boundary, and the two directions guard each other -
        // a divergence here makes this test demand a row that does not exist, and a divergence there
        // makes the catalog generator reject a row it cannot match.
        private static string OptionNameForProperty(string toolName, string propertyName, ToolParameterInfo property)
        {
            string kebabName = PascalToKebab(propertyName);
            if (!IsNegatedBooleanProperty(property))
            {
                return kebabName;
            }

            // This flag reads as an action rather than as the negation of a property name.
            if (toolName == "compile" && propertyName == "ReloadExternalSceneChanges")
            {
                return "stop-on-external-scene-changes";
            }
            return "no-" + kebabName;
        }

        private static bool IsNegatedBooleanProperty(ToolParameterInfo property)
        {
            return string.Equals(property.Type, "boolean", StringComparison.OrdinalIgnoreCase) &&
                property.DefaultValue is bool defaultValue &&
                defaultValue;
        }

        private static string PascalToKebab(string value)
        {
            System.Text.StringBuilder builder = new();
            for (int index = 0; index < value.Length; index++)
            {
                if (index > 0 && value[index] >= 'A' && value[index] <= 'Z')
                {
                    builder.Append('-');
                }
                builder.Append(value[index]);
            }
            return builder.ToString().ToLowerInvariant();
        }

        private static Dictionary<string, SkillDocumentation> ReadSkillDocumentation()
        {
            Dictionary<string, SkillDocumentation> documentation = new(StringComparer.Ordinal);
            foreach (string skillPath in EnumerateSkillFiles())
            {
                string relativePath = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(skillPath))) +
                    "/" + SkillDirectoryName + "/" + SkillFileName;
                foreach (KeyValuePair<string, SkillDocumentation> entry in ParseSkill(File.ReadAllText(skillPath), relativePath))
                {
                    documentation[entry.Key] = entry.Value;
                }
            }
            return documentation;
        }

        private static string[] EnumerateSkillFiles()
        {
            string editorRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), PackageEditorPath);
            return SkillContainerDirectories
                .Select(container => Path.Combine(editorRoot, container))
                .Where(Directory.Exists)
                .SelectMany(container => Directory.GetDirectories(container))
                .Select(toolDirectory => Path.Combine(toolDirectory, SkillDirectoryName, SkillFileName))
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        // Why the parsing here is deliberately smaller than the Go parser in cli/common/skilldocs: this
        // guard only asks whether a row for an option exists, so it never interprets a description's
        // text. That keeps the duplicated understanding of the file format to the two things a missing
        // row depends on - where the table is and what its first column says.
        private static Dictionary<string, SkillDocumentation> ParseSkill(string content, string relativePath)
        {
            string[] lines = content
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');
            string[] parametersSection = ReadParametersSection(lines);
            if (parametersSection.Any(line => line.TrimStart().StartsWith(SubsectionHeadingPrefix, StringComparison.Ordinal)))
            {
                return ParseMultiToolSkill(parametersSection, relativePath);
            }
            return ParseSingleToolSkill(lines, relativePath);
        }

        private static Dictionary<string, SkillDocumentation> ParseSingleToolSkill(string[] lines, string relativePath)
        {
            Dictionary<string, string> frontmatter = ReadFrontmatter(lines);
            string toolName = SingleSkillToolName(frontmatter);
            Dictionary<string, SkillDocumentation> documentation = new(StringComparer.Ordinal);
            if (toolName.Length == 0)
            {
                return documentation;
            }

            documentation[toolName] = new SkillDocumentation(
                frontmatter.TryGetValue("description", out string description) ? description : "",
                ReadTableOptionNames(lines),
                relativePath);
            return documentation;
        }

        private static Dictionary<string, SkillDocumentation> ParseMultiToolSkill(string[] sectionLines, string relativePath)
        {
            Dictionary<string, SkillDocumentation> documentation = new(StringComparer.Ordinal);
            for (int index = 0; index < sectionLines.Length; index++)
            {
                string line = sectionLines[index].Trim();
                if (!line.StartsWith(SubsectionHeadingPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string toolName = line.Substring(SubsectionHeadingPrefix.Length).Trim();
                if (toolName.Length == 0)
                {
                    continue;
                }

                string[] blockLines = ReadSubsection(sectionLines, index);
                documentation[toolName] = new SkillDocumentation(
                    FirstProseLine(blockLines),
                    ReadTableOptionNames(blockLines),
                    relativePath);
            }
            return documentation;
        }

        private static string[] ReadParametersSection(string[] lines)
        {
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Trim() != ParametersSectionHeading)
                {
                    continue;
                }

                for (int end = index + 1; end < lines.Length; end++)
                {
                    if (lines[end].TrimStart().StartsWith(SectionHeadingPrefix, StringComparison.Ordinal))
                    {
                        return lines.Skip(index + 1).Take(end - index - 1).ToArray();
                    }
                }
                return lines.Skip(index + 1).ToArray();
            }
            return Array.Empty<string>();
        }

        private static string[] ReadSubsection(string[] sectionLines, int headingIndex)
        {
            for (int end = headingIndex + 1; end < sectionLines.Length; end++)
            {
                if (sectionLines[end].TrimStart().StartsWith(SubsectionHeadingPrefix, StringComparison.Ordinal))
                {
                    return sectionLines.Skip(headingIndex + 1).Take(end - headingIndex - 1).ToArray();
                }
            }
            return sectionLines.Skip(headingIndex + 1).ToArray();
        }

        private static string FirstProseLine(string[] lines)
        {
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    continue;
                }
                return trimmed;
            }
            return "";
        }

        private static Dictionary<string, string> ReadFrontmatter(string[] lines)
        {
            Dictionary<string, string> frontmatter = new(StringComparer.Ordinal);
            if (lines.Length == 0 || lines[0].Trim() != FrontmatterFence)
            {
                return frontmatter;
            }

            for (int index = 1; index < lines.Length && lines[index].Trim() != FrontmatterFence; index++)
            {
                int separatorIndex = lines[index].IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = lines[index].Substring(0, separatorIndex).Trim();
                string value = lines[index].Substring(separatorIndex + 1).Trim().Trim('"');
                frontmatter[key] = value;
            }
            return frontmatter;
        }

        // Why the skill name is a fallback: toolName is authoritative when present, and every skill in
        // this package is named "uloop-<tool-name>" for the tools that declare no toolName.
        private static string SingleSkillToolName(Dictionary<string, string> frontmatter)
        {
            if (frontmatter.TryGetValue("toolName", out string toolName) && toolName.Length > 0)
            {
                return toolName;
            }
            if (!frontmatter.TryGetValue("name", out string name) || !name.StartsWith(SkillNamePrefix, StringComparison.Ordinal))
            {
                return "";
            }
            return name.Substring(SkillNamePrefix.Length);
        }

        // Only the first standard-header table counts, matching the Go parser: help renders that one
        // table, so a row placed in a second table would be documentation this guard accepts and help
        // never shows.
        private static HashSet<string> ReadTableOptionNames(string[] lines)
        {
            HashSet<string> optionNames = new(StringComparer.Ordinal);
            for (int index = 0; index < lines.Length; index++)
            {
                if (!IsStandardParameterTableHeader(lines[index]))
                {
                    continue;
                }

                for (int row = index + 1; row < lines.Length && lines[row].TrimStart().StartsWith("|", StringComparison.Ordinal); row++)
                {
                    // The separator row's cells are dashes only, which leaves no option name behind.
                    string optionName = OptionNameFromCell(SplitTableRow(lines[row]).FirstOrDefault() ?? "");
                    if (optionName.Length > 0)
                    {
                        optionNames.Add(optionName);
                    }
                }
                return optionNames;
            }
            return optionNames;
        }

        private static bool IsStandardParameterTableHeader(string line)
        {
            string[] cells = SplitTableRow(line);
            return cells.Length == StandardParameterTableCells.Length &&
                cells.SequenceEqual(StandardParameterTableCells, StringComparer.Ordinal);
        }

        // The first column never contains an escaped pipe, so splitting the row structurally is enough
        // to read it; only descriptions carry "\|" and this guard never looks at them.
        // Why not handle the escape: nothing here reads a description. Extending this to read one needs
        // the same escape handling cli/common/skilldocs.splitTableRow does, or cells will be truncated.
        private static string[] SplitTableRow(string line)
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                return Array.Empty<string>();
            }

            return trimmed
                .Trim('|')
                .Split('|')
                .Select(cell => cell.Trim())
                .ToArray();
        }

        private static string OptionNameFromCell(string cell)
        {
            string name = cell.Replace("`", "").Trim();
            string[] fields = name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                return "";
            }
            return fields[0].TrimStart('-');
        }

        /// <summary>
        /// One tool's documentation as this guard needs it: the description shown in help and the set
        /// of options the parameter table has a row for.
        /// </summary>
        private sealed class SkillDocumentation
        {
            public readonly string Description;
            public readonly string SkillRelativePath;
            private readonly HashSet<string> optionNames;

            public SkillDocumentation(string description, HashSet<string> optionNames, string skillRelativePath)
            {
                Description = description;
                SkillRelativePath = skillRelativePath;
                this.optionNames = optionNames;
            }

            public bool DocumentsOption(string optionName)
            {
                return optionNames.Contains(optionName);
            }
        }
    }
}
