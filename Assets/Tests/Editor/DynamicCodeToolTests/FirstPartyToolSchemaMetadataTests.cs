using System;
using System.Collections.Generic;
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
            // so the CLI recovers the name shown in `--help` / `uloop list` by indexing that name
            // list (Go enumValueAtIndex). Same-value aliases are allowed only after the canonical
            // name: names[ordinal] must be the first declaration of that value (MetadataToken
            // order). Gaps, negative values, and a [Flags] enum would make the CLI print the wrong
            // default member name.
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

                    // Why MetadataToken order: GetFields does not guarantee declaration order, and
                    // the CLI indexes Enum.GetNames by the numeric default. The first same-value
                    // member in declaration order is the canonical name that must occupy that index.
                    FieldInfo[] memberFields = propertyType.GetFields(
                            BindingFlags.Public | BindingFlags.Static)
                        .OrderBy(field => field.MetadataToken)
                        .ToArray();

                    string[] names = Enum.GetNames(propertyType);
                    Dictionary<long, string> canonicalNamesByValue = new();
                    HashSet<long> distinctValues = new();
                    long maxValue = -1;
                    foreach (FieldInfo memberField in memberFields)
                    {
                        long value = Convert.ToInt64(memberField.GetRawConstantValue());
                        Assert.That(
                            value,
                            Is.GreaterThanOrEqualTo(0),
                            $"{location} has a negative member value {value}");
                        distinctValues.Add(value);
                        if (!canonicalNamesByValue.ContainsKey(value))
                        {
                            canonicalNamesByValue[value] = memberField.Name;
                        }

                        if (value > maxValue)
                        {
                            maxValue = value;
                        }
                    }

                    Assert.That(
                        distinctValues.Count,
                        Is.EqualTo(maxValue + 1),
                        $"{location} has gaps in its ordinal values");

                    for (long value = 0; value <= maxValue; value++)
                    {
                        Assert.That(
                            names.Length,
                            Is.GreaterThan((int)value),
                            $"{location} is missing a canonical name at index {value}");
                        string canonicalName = canonicalNamesByValue[value];
                        Assert.That(
                            names[value],
                            Is.EqualTo(canonicalName),
                            $"{location} names[{value}] is '{names[value]}' but canonical is '{canonicalName}'");
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
