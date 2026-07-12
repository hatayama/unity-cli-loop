using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    [TestFixture]
    public sealed class DynamicCodeDiagnosticColumnMapperTests
    {
        [Test]
        public void MapWrappedColumnToUserColumn_SubtractsWrapperIndentAndClampsToOne()
        {
            // Verifies wrapped physical columns are rebased to user-snippet columns.
            Assert.That(DynamicCodeDiagnosticColumnMapper.MapWrappedColumnToUserColumn(20), Is.EqualTo(8));
            Assert.That(DynamicCodeDiagnosticColumnMapper.MapWrappedColumnToUserColumn(5), Is.EqualTo(1));
            Assert.That(DynamicCodeDiagnosticColumnMapper.MapWrappedColumnToUserColumn(0), Is.EqualTo(0));
        }
    }
}
