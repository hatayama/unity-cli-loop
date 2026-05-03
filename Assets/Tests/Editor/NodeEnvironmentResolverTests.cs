using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop
{
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
    }
}
