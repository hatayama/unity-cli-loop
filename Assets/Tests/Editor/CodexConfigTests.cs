using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    [TestFixture]
    public class CodexConfigTests
    {
        private static readonly Type CodexServiceType = typeof(CodexTomlConfigService);
        private static readonly Type PathResolverType = typeof(UnityMcpPathResolver);
        private static readonly Type ServerConfigFactoryType = typeof(McpServerConfigFactory);

        private static readonly BindingFlags PrivateStatic =
            BindingFlags.NonPublic | BindingFlags.Static;

        // ----------------------------------------------------------------
        // BuildBlock + ReadCurrentValues round-trip
        // ----------------------------------------------------------------

        [Test]
        public void Should_RoundTrip_RelativePath_WithForwardSlashes()
        {
            string inputPath = "Packages/src/TypeScriptServer~/dist/server.bundle.js";
            int inputPort = 12345;

            string toml = InvokeBuildBlock(inputPort, inputPath);
            (string arg0, int? port) = InvokeReadCurrentValues(toml);

            Assert.AreEqual(inputPath, arg0);
            Assert.AreEqual(inputPort, port);
        }

        [Test]
        public void Should_RoundTrip_RelativePath_WithBackslashes()
        {
            string inputPath = @"Packages\src\TypeScriptServer~\dist\server.bundle.js";
            int inputPort = 54321;

            string toml = InvokeBuildBlock(inputPort, inputPath);
            (string arg0, int? port) = InvokeReadCurrentValues(toml);

            Assert.AreEqual(inputPath, arg0);
            Assert.AreEqual(inputPort, port);
        }

        [Test]
        public void Should_RoundTrip_PortNumber()
        {
            string inputPath = "some/relative/path.js";
            int inputPort = 65535;

            string toml = InvokeBuildBlock(inputPort, inputPath);
            (string _, int? port) = InvokeReadCurrentValues(toml);

            Assert.AreEqual(inputPort, port);
        }

        // ----------------------------------------------------------------
        // NormalizeForCompare
        // ----------------------------------------------------------------

        [Test]
        public void NormalizeForCompare_Should_ConvertBackslashToForwardSlash()
        {
            string result = InvokeNormalizeForCompare(@"Packages\src\server.js");

            Assert.AreEqual("Packages/src/server.js", result);
        }

        [Test]
        public void NormalizeForCompare_Should_ReturnNull_WhenInputIsNull()
        {
            string result = InvokeNormalizeForCompare(null);

            Assert.IsNull(result);
        }

        [Test]
        public void NormalizeForCompare_Should_ReturnEmpty_WhenInputIsEmpty()
        {
            string result = InvokeNormalizeForCompare(string.Empty);

            Assert.AreEqual(string.Empty, result);
        }

        // ----------------------------------------------------------------
        // MakeRelativeToConfigurationRoot
        // ----------------------------------------------------------------

        [Test]
        public void MakeRelativeToConfigurationRoot_Should_ReturnRelativePath_WhenUnderRoot()
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            string absolutePath = System.IO.Path.Combine(projectRoot, "Packages", "src", "server.js");

            string result = UnityMcpPathResolver.MakeRelativeToConfigurationRoot(absolutePath);

            Assert.AreEqual("Packages/src/server.js", result);
        }

        [Test]
        public void MakeRelativeToConfigurationRoot_Should_ReturnAbsolutePath_WhenOutsideRoot()
        {
            string outsidePath = "/tmp/outside/path/server.js";

            string result = UnityMcpPathResolver.MakeRelativeToConfigurationRoot(outsidePath);

            Assert.AreEqual(outsidePath, result);
        }

        [Test]
        public void MakeRelativeToConfigurationRoot_Should_ReturnInput_WhenNullOrEmpty()
        {
            Assert.IsNull(UnityMcpPathResolver.MakeRelativeToConfigurationRoot(null));
            Assert.AreEqual(string.Empty, UnityMcpPathResolver.MakeRelativeToConfigurationRoot(string.Empty));
        }

        // ----------------------------------------------------------------
        // GetCodexConfigPath / GetCodexConfigDirectory
        // ----------------------------------------------------------------

        [Test]
        public void GetCodexConfigPath_Should_ResolveUnderProjectRoot()
        {
            string configPath = UnityMcpPathResolver.GetCodexConfigPath();
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            string expected = System.IO.Path.Combine(projectRoot, ".codex", "config.toml");

            Assert.AreEqual(expected, configPath);
        }

        [Test]
        public void GetCodexConfigDirectory_Should_ResolveUnderProjectRoot()
        {
            string configDir = UnityMcpPathResolver.GetCodexConfigDirectory();
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            string expected = System.IO.Path.Combine(projectRoot, ".codex");

            Assert.AreEqual(expected, configDir);
        }

        // ----------------------------------------------------------------
        // McpServerConfigFactory - Codex classification
        // ----------------------------------------------------------------

        [Test]
        public void GetServerPathForEditor_Should_ReturnRelativePath_ForCodex()
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            string absolutePath = System.IO.Path.Combine(projectRoot, "Packages", "src", "server.js");

            MethodInfo method = ServerConfigFactoryType.GetMethod("GetServerPathForEditor", PrivateStatic);
            Debug.Assert(method != null, "GetServerPathForEditor method not found");

            string result = (string)method.Invoke(null, new object[] { absolutePath, McpEditorType.Codex });

            Assert.AreNotEqual(absolutePath, result,
                "Codex should use relative path, not absolute");
            Assert.AreEqual("Packages/src/server.js", result);
        }

        // ----------------------------------------------------------------
        // RemoveLegacyEnvTable
        // ----------------------------------------------------------------

        // Verifies that the legacy [mcp_servers.uLoopMCP.env] table is removed
        // while the inline env line and unrelated sections are preserved.
        [Test]
        public void RemoveLegacyEnvTable_Should_RemoveOnlyLegacyTable_AndPreserveOtherSections()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.uLoopMCP.env]\n"
                + "UNITY_TCP_PORT = \"8800\"\n"
                + "\n"
                + "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            string result = InvokeRemoveLegacyEnvTable(toml);

            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            StringAssert.Contains("env = { \"UNITY_TCP_PORT\" = \"8800\" }", result);
            StringAssert.Contains("[mcp_servers.other]", result);
            StringAssert.Contains("python", result);
        }

        // Verifies that content without a legacy table is returned unchanged.
        [Test]
        public void RemoveLegacyEnvTable_Should_ReturnUnchanged_WhenLegacyTableAbsent()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            string result = InvokeRemoveLegacyEnvTable(toml);

            Assert.AreEqual(toml, result);
        }

        // Verifies that a legacy table at the end of the file (no following section) is removed.
        [Test]
        public void RemoveLegacyEnvTable_Should_RemoveTable_WhenAtEndOfFile()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.uLoopMCP.env]\n"
                + "UNITY_TCP_PORT = \"8800\"\n";

            string result = InvokeRemoveLegacyEnvTable(toml);

            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            StringAssert.Contains("env = { \"UNITY_TCP_PORT\" = \"8800\" }", result);
        }

        // Verifies that CRLF line endings in the surviving content are preserved (Windows compatibility guardrail).
        [Test]
        public void RemoveLegacyEnvTable_Should_PreserveCrlfLineEndings()
        {
            string toml = "[mcp_servers.uLoopMCP]\r\n"
                + "command = \"node\"\r\n"
                + "args = ['server.js']\r\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\r\n"
                + "\r\n"
                + "[mcp_servers.uLoopMCP.env]\r\n"
                + "UNITY_TCP_PORT = \"8800\"\r\n"
                + "\r\n"
                + "[mcp_servers.other]\r\n"
                + "command = \"python\"\r\n"
                + "args = ['app.py']\r\n";

            string result = InvokeRemoveLegacyEnvTable(toml);

            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            Assert.AreEqual(-1, result.Replace("\r\n", string.Empty).IndexOf('\n'),
                "Surviving content must not contain a bare LF outside of a CRLF pair");
        }

        // ----------------------------------------------------------------
        // BuildAutoConfiguredContent
        // ----------------------------------------------------------------

        // Verifies that running the write flow on content with both an inline env and a legacy
        // table produces a single env definition and preserves unrelated sections (regression test
        // for the duplicate-key bug fixed by removing the legacy table before rebuilding the block).
        [Test]
        public void BuildAutoConfiguredContent_Should_ProduceSingleEnvDefinition_WhenLegacyTableExists()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['old/server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.uLoopMCP.env]\n"
                + "UNITY_TCP_PORT = \"8800\"\n"
                + "\n"
                + "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            string result = InvokeBuildAutoConfiguredContent(toml, 9999, "new/server.js");

            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            Assert.AreEqual(1, CountOccurrences(result, "env ="));
            StringAssert.Contains("[mcp_servers.other]", result);
            StringAssert.Contains("python", result);
        }

        // ----------------------------------------------------------------
        // BuildDeletedContent
        // ----------------------------------------------------------------

        // Verifies that deleting the configuration removes the legacy [mcp_servers.uLoopMCP.env]
        // table as well as the uLoopMCP section, leaving unrelated sections intact.
        [Test]
        public void BuildDeletedContent_Should_RemoveLegacyTable_AndSection_PreservingOtherSections()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.uLoopMCP.env]\n"
                + "UNITY_TCP_PORT = \"8800\"\n"
                + "\n"
                + "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            (bool isChanged, string result) = InvokeBuildDeletedContent(toml);

            Assert.IsTrue(isChanged);
            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP]", result);
            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            StringAssert.Contains("[mcp_servers.other]", result);
            StringAssert.Contains("python", result);
        }

        // Verifies that a config holding no uLoopMCP entries is reported unchanged, so its blank
        // lines are never rewritten by the deletion path.
        [Test]
        public void BuildDeletedContent_Should_ReportUnchanged_WhenNoULoopMCPEntries()
        {
            string toml = "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n"
                + "\n"
                + "\n"
                + "\n"
                + "[mcp_servers.another]\n"
                + "command = \"node\"\n";

            (bool isChanged, string result) = InvokeBuildDeletedContent(toml);

            Assert.IsFalse(isChanged);
            Assert.AreEqual(toml, result);
        }

        // ----------------------------------------------------------------
        // BuildDevelopmentSettingsContent
        // ----------------------------------------------------------------

        // Verifies that updating development settings on a config that still holds a legacy table
        // produces a single env definition, so Codex no longer rejects the file as a duplicate key.
        [Test]
        public void BuildDevelopmentSettingsContent_Should_ProduceSingleEnvDefinition_WhenLegacyTableExists()
        {
            string toml = "[mcp_servers.uLoopMCP]\n"
                + "command = \"node\"\n"
                + "args = ['server.js']\n"
                + "env = { \"UNITY_TCP_PORT\" = \"8800\" }\n"
                + "\n"
                + "[mcp_servers.uLoopMCP.env]\n"
                + "UNITY_TCP_PORT = \"8800\"\n"
                + "\n"
                + "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            (bool hasSection, string result) = InvokeBuildDevelopmentSettingsContent(toml, 9999, true, true);

            Assert.IsTrue(hasSection);
            StringAssert.DoesNotContain("[mcp_servers.uLoopMCP.env]", result);
            Assert.AreEqual(1, CountOccurrences(result, "env ="));
            StringAssert.Contains("\"UNITY_TCP_PORT\" = \"9999\"", result);
            StringAssert.Contains("[mcp_servers.other]", result);
        }

        // Verifies that a config without the uLoopMCP section is reported as having no section, so
        // the caller creates one through AutoConfigure instead of writing a section-less file.
        [Test]
        public void BuildDevelopmentSettingsContent_Should_ReportNoSection_WhenSectionMissing()
        {
            string toml = "[mcp_servers.other]\n"
                + "command = \"python\"\n"
                + "args = ['app.py']\n";

            (bool hasSection, string result) = InvokeBuildDevelopmentSettingsContent(toml, 9999, true, true);

            Assert.IsFalse(hasSection);
            Assert.AreEqual(toml, result);
        }

        // ----------------------------------------------------------------
        // Reflection helpers
        // ----------------------------------------------------------------

        private static string InvokeBuildBlock(int port, string serverPath)
        {
            MethodInfo method = CodexServiceType.GetMethod("BuildBlock", PrivateStatic);
            Debug.Assert(method != null, "BuildBlock method not found");
            return (string)method.Invoke(null, new object[] { port, serverPath });
        }

        private static (string arg0, int? port) InvokeReadCurrentValues(string content)
        {
            MethodInfo method = CodexServiceType.GetMethod("ReadCurrentValues", PrivateStatic);
            Debug.Assert(method != null, "ReadCurrentValues method not found");
            object result = method.Invoke(null, new object[] { content });
            // ValueTuple<string, int?> deconstruction
            System.Runtime.CompilerServices.ITuple tuple = (System.Runtime.CompilerServices.ITuple)result;
            return ((string)tuple[0], (int?)tuple[1]);
        }

        private static string InvokeNormalizeForCompare(string path)
        {
            MethodInfo method = CodexServiceType.GetMethod("NormalizeForCompare", PrivateStatic);
            Debug.Assert(method != null, "NormalizeForCompare method not found");
            return (string)method.Invoke(null, new object[] { path });
        }

        private static string InvokeRemoveLegacyEnvTable(string content)
        {
            MethodInfo method = CodexServiceType.GetMethod("RemoveLegacyEnvTable", PrivateStatic);
            Debug.Assert(method != null, "RemoveLegacyEnvTable method not found");
            return (string)method.Invoke(null, new object[] { content });
        }

        private static string InvokeBuildAutoConfiguredContent(string content, int port, string relativeServerPath)
        {
            MethodInfo method = CodexServiceType.GetMethod("BuildAutoConfiguredContent", PrivateStatic);
            Debug.Assert(method != null, "BuildAutoConfiguredContent method not found");
            return (string)method.Invoke(null, new object[] { content, port, relativeServerPath });
        }

        private static (bool isChanged, string content) InvokeBuildDeletedContent(string content)
        {
            MethodInfo method = CodexServiceType.GetMethod("BuildDeletedContent", PrivateStatic);
            Debug.Assert(method != null, "BuildDeletedContent method not found");
            object result = method.Invoke(null, new object[] { content });
            System.Runtime.CompilerServices.ITuple tuple = (System.Runtime.CompilerServices.ITuple)result;
            return ((bool)tuple[0], (string)tuple[1]);
        }

        private static (bool hasSection, string content) InvokeBuildDevelopmentSettingsContent(
            string content, int port, bool developmentMode, bool enableMcpLogs)
        {
            MethodInfo method = CodexServiceType.GetMethod("BuildDevelopmentSettingsContent", PrivateStatic);
            Debug.Assert(method != null, "BuildDevelopmentSettingsContent method not found");
            object result = method.Invoke(null, new object[] { content, port, developmentMode, enableMcpLogs });
            System.Runtime.CompilerServices.ITuple tuple = (System.Runtime.CompilerServices.ITuple)result;
            return ((bool)tuple[0], (string)tuple[1]);
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }
    }
}
