using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Node Environment Resolver behavior.
    /// </summary>
    [TestFixture]
    public class NodeEnvironmentResolverTests
    {
        // --- ExtractBetweenMarkers ---

        [Test]
        public void ExtractBetweenMarkers_NullInput_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(null, "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_EmptyInput_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers("", "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_NoMarkers_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "/usr/local/bin/node", "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_OnlyStartMarker_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "__START__/usr/local/bin/node", "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_OnlyEndMarker_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "/usr/local/bin/node__END__", "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_ReversedMarkers_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "__END__/usr/local/bin/node__START__", "__START__", "__END__");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractBetweenMarkers_EndMarkerInBannerBeforeStartMarker_ReturnsValueBetween()
        {
            string output = "__END__ banner text\n__START__/usr/local/bin/node__END__";

            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(output, "__START__", "__END__");

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractBetweenMarkers_ValidMarkers_ReturnsValueBetween()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "__START__/usr/local/bin/node__END__", "__START__", "__END__");

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractBetweenMarkers_WithBannerBeforeMarkers_ReturnsValueBetween()
        {
            string output = "Last login: Mon Feb 24 10:00:00 on ttys001\n" +
                            "Welcome to zsh!\n" +
                            "__START__/usr/local/bin/node__END__\n";

            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(output, "__START__", "__END__");

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractBetweenMarkers_WithBannerAfterMarkers_ReturnsValueBetween()
        {
            string output = "__START__/usr/local/bin__END__\nsome trailing output";

            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(output, "__START__", "__END__");

            Assert.AreEqual("/usr/local/bin", result);
        }

        [Test]
        public void ExtractBetweenMarkers_ValueWithWhitespace_ReturnsTrimmedValue()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "__START__  /usr/local/bin/node  __END__", "__START__", "__END__");

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractBetweenMarkers_EmptyValueBetweenMarkers_ReturnsEmptyString()
        {
            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                "__START____END__", "__START__", "__END__");

            Assert.AreEqual("", result);
        }

        [Test]
        public void ExtractBetweenMarkers_PathStyleValue_ExtractsCorrectly()
        {
            string pathValue = "/usr/local/bin:/usr/bin:/bin:/opt/homebrew/bin";
            string output = "__PATH_START__" + pathValue + "__PATH_END__";

            string result = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output, "__PATH_START__", "__PATH_END__");

            Assert.AreEqual(pathValue, result);
        }

        // --- ExtractAbsolutePathLine ---

        [Test]
        public void ExtractAbsolutePathLine_NullInput_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(null);

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractAbsolutePathLine_EmptyInput_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine("");

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractAbsolutePathLine_SingleAbsolutePath_ReturnsPath()
        {
            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine("/usr/local/bin/node");

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractAbsolutePathLine_AliasTextBeforeAbsolutePath_ReturnsAbsolutePath()
        {
            string block = "node: aliased to /usr/local/bin/node\n/usr/local/bin/node";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractAbsolutePathLine_OnlyAliasText_ReturnsNull()
        {
            string block = "node: aliased to /usr/local/bin/node";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractAbsolutePathLine_MultipleAbsolutePaths_ReturnsFirst()
        {
            string block = "/opt/homebrew/bin/node\n/usr/local/bin/node";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.AreEqual("/opt/homebrew/bin/node", result);
        }

        [Test]
        public void ExtractAbsolutePathLine_EmptyLinesBeforePath_IgnoresEmptyLines()
        {
            string block = "\n\n\n/usr/local/bin/node\n";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractAbsolutePathLine_RelativePathOnly_ReturnsNull()
        {
            string block = "relative/path/to/node";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.IsNull(result);
        }

        [Test]
        public void ExtractAbsolutePathLine_PathWithWhitespace_ReturnsTrimmedPath()
        {
            string block = "  /usr/local/bin/node  ";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractAbsolutePathLine_BannerThenAliasTextThenPath_ReturnsPath()
        {
            string block = "some banner text\n" +
                           "node: aliased to something\n" +
                           "/usr/local/bin/node";

            string result = NodeEnvironmentResolver.ExtractAbsolutePathLine(block);

            Assert.AreEqual("/usr/local/bin/node", result);
        }

        [Test]
        public void ExtractDirectoryServiceUserShell_WhenUserShellLineExists_ReturnsShellPath()
        {
            // Verifies that macOS directory service output can recover the user's login shell.
            string output = "UserShell: /bin/zsh\n";

            string result = NodeEnvironmentResolver.ExtractDirectoryServiceUserShell(output);

            Assert.AreEqual("/bin/zsh", result);
        }

        [Test]
        public void SelectUserShell_WhenEnvironmentShellIsMissing_UsesDirectoryServiceShell()
        {
            // Verifies that GUI-launched Unity can still resolve terminal PATH through the user's login shell.
            string result = NodeEnvironmentResolver.SelectUserShell(
                null,
                "/bin/zsh",
                path => path == "/bin/zsh");

            Assert.AreEqual("/bin/zsh", result);
        }

        [Test]
        public void SelectUserShell_WhenEnvironmentShellExists_PrefersEnvironmentShell()
        {
            // Verifies that an inherited SHELL remains authoritative when it points to a real shell.
            string result = NodeEnvironmentResolver.SelectUserShell(
                "/bin/bash",
                "/bin/zsh",
                path => path == "/bin/bash" || path == "/bin/zsh");

            Assert.AreEqual("/bin/bash", result);
        }

        [Test]
        public void SelectUserShell_WhenNoCandidateExists_ReturnsPosixFallbackShell()
        {
            // Verifies that shell resolution keeps a deterministic fallback when no user shell is available.
            string result = NodeEnvironmentResolver.SelectUserShell(
                null,
                null,
                path => false);

            Assert.AreEqual("/bin/sh", result);
        }

        /// <summary>
        /// Verifies that missing directory-service output yields no shell instead of failing.
        /// </summary>
        [Test]
        public void ExtractDirectoryServiceUserShell_WhenOutputIsNull_ReturnsNull()
        {
            string result = NodeEnvironmentResolver.ExtractDirectoryServiceUserShell(null);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that lines before the UserShell attribute are skipped until the shell line is found.
        /// </summary>
        [Test]
        public void ExtractDirectoryServiceUserShell_WhenOtherAttributesPrecedeShell_ReturnsShellPath()
        {
            string output = "RecordName: <USER_NAME>\nUniqueID: 501\nUserShell: /bin/zsh\n";

            string result = NodeEnvironmentResolver.ExtractDirectoryServiceUserShell(output);

            Assert.That(result, Is.EqualTo("/bin/zsh"));
        }

        /// <summary>
        /// Verifies that output without a UserShell attribute yields no shell.
        /// </summary>
        [Test]
        public void ExtractDirectoryServiceUserShell_WhenNoUserShellLineExists_ReturnsNull()
        {
            string output = "No such key: UserShell\nRecordName: <USER_NAME>\n";

            string result = NodeEnvironmentResolver.ExtractDirectoryServiceUserShell(output);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies a .cmd or .exe entry is chosen over an earlier extensionless entry, ignoring extension case.
        /// </summary>
        [TestCase(@"C:\tools\uloop.cmd")]
        [TestCase(@"C:\tools\uloop.EXE")]
        public void SelectWindowsExecutable_WhenLaunchableEntryFollowsExtensionlessEntry_ReturnsLaunchableEntry(
            string launchablePath)
        {
            string[] paths = { @"C:\tools\uloop", launchablePath };

            string result = NodeEnvironmentResolver.SelectWindowsExecutable(paths);

            Assert.That(result, Is.EqualTo(launchablePath));
        }

        /// <summary>
        /// Verifies the first entry is chosen when no entry has a .cmd or .exe extension.
        /// </summary>
        [Test]
        public void SelectWindowsExecutable_WhenNoLaunchableEntryExists_ReturnsFirstEntry()
        {
            string[] paths = { @"C:\tools\uloop", @"C:\tools\uloop.ps1" };

            string result = NodeEnvironmentResolver.SelectWindowsExecutable(paths);

            Assert.That(result, Is.EqualTo(@"C:\tools\uloop"));
        }

        /// <summary>
        /// Verifies that missing or empty where results select nothing.
        /// </summary>
        [Test]
        public void SelectWindowsExecutable_WhenNoPathsExist_ReturnsNull()
        {
            Assert.That(NodeEnvironmentResolver.SelectWindowsExecutable(null), Is.Null);
            Assert.That(NodeEnvironmentResolver.SelectWindowsExecutable(Array.Empty<string>()), Is.Null);
        }

        /// <summary>
        /// Verifies CRLF where output is split into trimmed paths without carriage returns or blank lines.
        /// </summary>
        [Test]
        public void ParseWhereOutput_WhenOutputUsesCrlf_ReturnsTrimmedPaths()
        {
            string output = "C:\\tools\\uloop\r\n\r\n  C:\\tools\\uloop.cmd  \r\n";

            string[] result = NodeEnvironmentResolver.ParseWhereOutput(output);

            Assert.That(result, Is.EqualTo(new[] { @"C:\tools\uloop", @"C:\tools\uloop.cmd" }));
        }

        /// <summary>
        /// Verifies LF where output is split into one path per line.
        /// </summary>
        [Test]
        public void ParseWhereOutput_WhenOutputUsesLf_ReturnsPaths()
        {
            string output = "C:\\tools\\uloop\nC:\\tools\\uloop.exe";

            string[] result = NodeEnvironmentResolver.ParseWhereOutput(output);

            Assert.That(result, Is.EqualTo(new[] { @"C:\tools\uloop", @"C:\tools\uloop.exe" }));
        }

        /// <summary>
        /// Verifies that missing output, or output with only blank lines, yields no paths.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \r\n\r\n ")]
        public void ParseWhereOutput_WhenOutputHasNoPaths_ReturnsNull(string output)
        {
            string[] result = NodeEnvironmentResolver.ParseWhereOutput(output);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies user names made of letters, digits, underscores, hyphens, and dots are accepted for the directory lookup.
        /// </summary>
        [TestCase("user_name-1.test")]
        [TestCase("a")]
        public void IsSafeDirectoryServiceUserName_WhenNameUsesAllowedCharacters_ReturnsTrue(string userName)
        {
            Assert.That(NodeEnvironmentResolver.IsSafeDirectoryServiceUserName(userName), Is.True);
        }

        /// <summary>
        /// Verifies empty names and names with characters that could change the lookup path are rejected.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("user name")]
        [TestCase("../user")]
        [TestCase("user;id")]
        public void IsSafeDirectoryServiceUserName_WhenNameIsEmptyOrUnsafe_ReturnsFalse(string userName)
        {
            Assert.That(NodeEnvironmentResolver.IsSafeDirectoryServiceUserName(userName), Is.False);
        }
    }
}
