using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterization tests for Tool Settings list layout signatures.
    /// </summary>
    public sealed class ToolSettingsSectionLayoutSignatureTests
    {
        [Test]
        public void Create_WhenGroupsHaveTools_IncludesGroupMarkersAndToolFields()
        {
            // Pins the rebuild signature format used to decide whether the ListView must rebuild.
            ToolSettingsSectionData data = new(
                showToolSettings: true,
                builtInTools: new[]
                {
                    new ToolToggleItem("compile", true, false, "Compile the project")
                },
                thirdPartyTools: new[]
                {
                    new ToolToggleItem("vendor.tool", false, true, "Vendor tool")
                },
                isRegistryAvailable: true);

            string signature = ToolSettingsSectionLayoutSignature.Create(data);

            Assert.That(signature, Is.EqualTo("B:compile|Compile the project|;T:vendor.tool|Vendor tool|;"));
        }

        [Test]
        public void Create_WhenGroupsAreEmpty_KeepsEmptyGroupMarkers()
        {
            // Pins empty-group signatures so collapsed/empty catalogs stay stable across refreshes.
            ToolSettingsSectionData data = new(
                showToolSettings: true,
                builtInTools: System.Array.Empty<ToolToggleItem>(),
                thirdPartyTools: System.Array.Empty<ToolToggleItem>(),
                isRegistryAvailable: true);

            string signature = ToolSettingsSectionLayoutSignature.Create(data);

            Assert.That(signature, Is.EqualTo("B:;T:;"));
        }
    }
}
