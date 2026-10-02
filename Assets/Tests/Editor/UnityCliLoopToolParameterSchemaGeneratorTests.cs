using System;
using System.Collections.Generic;
using System.ComponentModel;

using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how tool DTO properties become CLI parameter schema entries: which properties are
    /// exposed, under which name, with which type, description, default value, and enum values.
    /// </summary>
    public sealed class UnityCliLoopToolParameterSchemaGeneratorTests
    {
        /// <summary>
        /// Verifies each property type maps to the CLI parameter type the schema advertises for it.
        /// </summary>
        [TestCase("Text", "string")]
        [TestCase("Flag", "boolean")]
        [TestCase("OptionalFlag", "boolean")]
        [TestCase("IntValue", "number")]
        [TestCase("LongValue", "number")]
        [TestCase("FloatValue", "number")]
        [TestCase("DoubleValue", "number")]
        [TestCase("DecimalValue", "number")]
        [TestCase("OptionalInt", "number")]
        [TestCase("Names", "array")]
        [TestCase("Numbers", "array")]
        [TestCase("Mode", "string")]
        [TestCase("ReadOnlyMap", "object")]
        [TestCase("InterfaceMap", "object")]
        [TestCase("DerivedMap", "object")]
        [TestCase("Nested", "string")]
        public void FromDto_WhenPropertyHasType_MapsToSchemaType(string propertyName, string expectedType)
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<TypedDto>();

            Assert.That(schema.Properties[propertyName].Type, Is.EqualTo(expectedType));
        }

        /// <summary>
        /// Verifies a get-only property and a property with a private setter are not exposed as parameters.
        /// </summary>
        [Test]
        public void FromDto_WhenPropertyHasNoPublicSetter_OmitsIt()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<VisibilityDto>();

            Assert.That(schema.Properties.ContainsKey("GetOnly"), Is.False);
            Assert.That(schema.Properties.ContainsKey("PrivateSetter"), Is.False);
            Assert.That(schema.Properties.ContainsKey("Visible"), Is.True);
        }

        /// <summary>
        /// Verifies a property marked Browsable(false) is hidden while Browsable(true) stays exposed.
        /// </summary>
        [Test]
        public void FromDto_WhenBrowsableAttributeSet_HidesOnlyNonBrowsableProperties()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<VisibilityDto>();

            Assert.That(schema.Properties.ContainsKey("Hidden"), Is.False);
            Assert.That(schema.Properties.ContainsKey("ExplicitlyBrowsable"), Is.True);
        }

        /// <summary>
        /// Verifies a JsonProperty rename is used both as the parameter key and in the placeholder description.
        /// </summary>
        [Test]
        public void FromDto_WhenJsonPropertyRenames_UsesJsonNameForKeyAndPlaceholder()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<DescribedDto>();

            Assert.That(schema.Properties.ContainsKey("RenamedValue"), Is.False);
            Assert.That(schema.Properties["renamed-value"].Description, Is.EqualTo("Parameter: renamed-value"));
        }

        /// <summary>
        /// Verifies a Description attribute supplies the parameter description verbatim.
        /// </summary>
        [Test]
        public void FromDto_WhenDescriptionAttributePresent_UsesItsText()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<DescribedDto>();

            Assert.That(schema.Properties["Documented"].Description, Is.EqualTo("How many items to return."));
        }

        /// <summary>
        /// Verifies a property without Description gets the placeholder built from its own name.
        /// </summary>
        [Test]
        public void FromDto_WhenNoDescriptionAttribute_UsesPlaceholderWithPropertyName()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<DescribedDto>();

            Assert.That(schema.Properties["Plain"].Description, Is.EqualTo("Parameter: Plain"));
        }

        /// <summary>
        /// Verifies default values are read from a freshly constructed DTO instance.
        /// </summary>
        [Test]
        public void FromDto_WhenDtoHasInitializers_UsesThemAsDefaultValues()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<DescribedDto>();

            Assert.That(schema.Properties["Documented"].DefaultValue, Is.EqualTo(25));
            Assert.That(schema.Properties["Plain"].DefaultValue, Is.EqualTo("start"));
            Assert.That(schema.Properties["renamed-value"].DefaultValue, Is.Null);
        }

        /// <summary>
        /// Verifies enum and nullable enum properties list the enum member names, and other types list none.
        /// </summary>
        [Test]
        public void FromDto_WhenPropertyIsEnum_ListsEnumNames()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<TypedDto>();

            Assert.That(schema.Properties["Mode"].Enum, Is.EqualTo(new[] { "Fast", "Thorough" }));
            Assert.That(schema.Properties["OptionalMode"].Enum, Is.EqualTo(new[] { "Fast", "Thorough" }));
            Assert.That(schema.Properties["Text"].Enum, Is.Null);
        }

        /// <summary>
        /// Verifies generated schemas never mark any parameter as required.
        /// </summary>
        [Test]
        public void FromDto_WhenGenerated_HasNoRequiredParameters()
        {
            ToolParameterSchema schema = UnityCliLoopToolParameterSchemaGenerator.FromDto<DescribedDto>();

            Assert.That(schema.Required, Is.Empty);
        }

        public enum SampleMode
        {
            Fast,
            Thorough
        }

        public sealed class NestedOptions
        {
            public int Depth { get; set; }
        }

        public sealed class DerivedStringMap : Dictionary<string, string>
        {
        }

        public sealed class TypedDto
        {
            public string Text { get; set; } = string.Empty;
            public bool Flag { get; set; }
            public bool? OptionalFlag { get; set; }
            public int IntValue { get; set; }
            public long LongValue { get; set; }
            public float FloatValue { get; set; }
            public double DoubleValue { get; set; }
            public decimal DecimalValue { get; set; }
            public int? OptionalInt { get; set; }
            public string[] Names { get; set; } = new string[0];
            public List<int> Numbers { get; set; } = new List<int>();
            public SampleMode Mode { get; set; }
            public SampleMode? OptionalMode { get; set; }
            public IReadOnlyDictionary<string, int> ReadOnlyMap { get; set; }
            public IDictionary<string, int> InterfaceMap { get; set; }
            public DerivedStringMap DerivedMap { get; set; }
            public NestedOptions Nested { get; set; }
        }

        public sealed class VisibilityDto
        {
            public string GetOnly => "fixed";
            public string PrivateSetter { get; private set; } = "fixed";
            public string Visible { get; set; } = string.Empty;

            [Browsable(false)]
            public string Hidden { get; set; } = string.Empty;

            [Browsable(true)]
            public string ExplicitlyBrowsable { get; set; } = string.Empty;
        }

        public sealed class DescribedDto
        {
            [System.ComponentModel.Description("How many items to return.")]
            public int Documented { get; set; } = 25;

            public string Plain { get; set; } = "start";

            [JsonProperty("renamed-value")]
            public string RenamedValue { get; set; }
        }
    }
}
