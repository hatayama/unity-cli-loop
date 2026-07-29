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
            // so the CLI recovers the name shown in `--help` by indexing the name list with that
            // number. Same-value aliases are allowed after the canonical name; gaps, negative
            // values, and a [Flags] enum would make the CLI print a different member's name.
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

                    // Why allow same-value aliases: CaptureMode.GameView shares rendering's
                    // ordinal so agents can pass GameView. CLI default lookup still indexes the
                    // name list by the numeric default, so the canonical name must stay at that
                    // index and aliases must be declared after it.
                    string[] names = Enum.GetNames(propertyType);
                    HashSet<long> distinctValues = new();
                    long maxValue = -1;
                    foreach (object member in Enum.GetValues(propertyType))
                    {
                        long value = Convert.ToInt64(member);
                        Assert.That(
                            value,
                            Is.GreaterThanOrEqualTo(0),
                            $"{location} has a negative member value {value}");
                        distinctValues.Add(value);
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
                        long nameValue = Convert.ToInt64(Enum.Parse(propertyType, names[value]));
                        Assert.That(
                            nameValue,
                            Is.EqualTo(value),
                            $"{location} canonical name at index {value} is '{names[value]}' with value {nameValue}");
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
