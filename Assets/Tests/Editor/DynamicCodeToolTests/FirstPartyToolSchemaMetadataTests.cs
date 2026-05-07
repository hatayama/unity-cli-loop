using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;
using ComponentModelDescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies first-party tool schema metadata stays CLI-focused.
    /// </summary>
    [TestFixture]
    public class FirstPartyToolSchemaMetadataTests
    {
        [Test]
        public void FirstPartySchemaProperties_WhenLoaded_ShouldNotExposeDescriptionAttributes()
        {
            // Tests that long-form agent guidance stays in skill files instead of runtime schema metadata.
            Type[] schemaTypes = TypeCache.GetTypesDerivedFrom<UnityCliLoopToolSchema>()
                .Where(type => type.Assembly.GetName().Name.StartsWith(
                    "UnityCLILoop.FirstPartyTools",
                    StringComparison.Ordinal))
                .ToArray();

            Assert.That(schemaTypes, Is.Not.Empty);

            foreach (Type schemaType in schemaTypes)
            {
                PropertyInfo[] properties = schemaType.GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly);

                foreach (PropertyInfo property in properties)
                {
                    ComponentModelDescriptionAttribute attribute =
                        property.GetCustomAttribute<ComponentModelDescriptionAttribute>();

                    Assert.That(attribute, Is.Null, $"{schemaType.FullName}.{property.Name}");
                }
            }
        }
    }
}
