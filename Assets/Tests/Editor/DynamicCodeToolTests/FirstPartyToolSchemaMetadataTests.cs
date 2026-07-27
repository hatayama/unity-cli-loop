using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
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
            Type[] schemaTypes = FirstPartySchemaTypes();

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

        [Test]
        public void FirstPartySchemaEnumProperties_WhenLoaded_ShouldBeZeroBasedAndContiguous()
        {
            // Tests that every enum a first-party schema exposes can be resolved by its ordinal.
            // The schema cache stores an enum default as a number while listing the members by name,
            // so the CLI recovers the name shown in `--help` by indexing the name list with that
            // number. A member with an explicit value or a [Flags] enum would make the CLI print a
            // different member's name as the default.
            Type[] schemaTypes = FirstPartySchemaTypes();

            Assert.That(schemaTypes, Is.Not.Empty);

            int checkedEnumPropertyCount = 0;
            foreach (Type schemaType in schemaTypes)
            {
                PropertyInfo[] properties = schemaType.GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly);

                foreach (PropertyInfo property in properties)
                {
                    Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    if (!propertyType.IsEnum)
                    {
                        continue;
                    }

                    string location = $"{schemaType.FullName}.{property.Name} ({propertyType.FullName})";

                    Assert.That(
                        propertyType.GetCustomAttribute<FlagsAttribute>(),
                        Is.Null,
                        $"{location} is a [Flags] enum, which cannot be resolved by ordinal");

                    Array members = Enum.GetValues(propertyType);
                    for (int index = 0; index < members.Length; index++)
                    {
                        long value = Convert.ToInt64(members.GetValue(index));
                        Assert.That(
                            value,
                            Is.EqualTo((long)index),
                            $"{location} is not zero-based and contiguous at index {index}");
                    }

                    checkedEnumPropertyCount++;
                }
            }

            // Guards the guard: if schemas stop exposing enums the assertions above go unreached,
            // and this test would keep passing while checking nothing.
            Assert.That(checkedEnumPropertyCount, Is.GreaterThan(0));
        }

        private static Type[] FirstPartySchemaTypes()
        {
            return TypeCache.GetTypesDerivedFrom<UnityCliLoopToolSchema>()
                .Where(type => type.Assembly.GetName().Name.StartsWith(
                    "UnityCLILoop.FirstPartyTools",
                    StringComparison.Ordinal))
                .ToArray();
        }

        [Test]
        public void ExecuteDynamicCodeSchema_WhenCreated_ShouldNotWaitForDomainReloadByDefault()
        {
            // Tests that ordinary execute-dynamic-code calls keep the low-latency path by default.
            ExecuteDynamicCodeSchema schema = new();

            Assert.That(schema.WaitForDomainReload, Is.False);
        }
    }
}
