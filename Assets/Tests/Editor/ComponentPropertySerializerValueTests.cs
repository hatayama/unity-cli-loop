using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the component property serializer renders each Inspector-visible value type, including
    /// empty and missing object references, on a temporary GameObject.
    /// </summary>
    public sealed class ComponentPropertySerializerValueTests
    {
        private GameObject _host;
        private GameObject _referenced;
        private ComponentPropertySerializerValueFixture _fixture;
        private ComponentPropertySerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SerializerHost");
            _referenced = new GameObject("ReferencedObject");
            _fixture = _host.AddComponent<ComponentPropertySerializerValueFixture>();
            _serializer = new ComponentPropertySerializer();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            if (_referenced != null)
            {
                Object.DestroyImmediate(_referenced);
            }
        }

        /// <summary>
        /// Verifies a missing component yields an empty property list instead of throwing.
        /// </summary>
        [Test]
        public void SerializeProperties_WithoutAComponent_ReturnsNoProperties()
        {
            Assert.That(_serializer.SerializeProperties(null), Is.Empty);
        }

        /// <summary>
        /// Verifies the internal script reference is hidden and the fields follow their declaration order.
        /// </summary>
        [Test]
        public void SerializeProperties_SkipsTheScriptReferenceAndKeepsInspectorOrder()
        {
            ComponentPropertyInfo[] properties = _serializer.SerializeProperties(_fixture);

            Assert.That(
                properties.Select(property => property.Name).ToArray(),
                Is.EqualTo(new[]
                {
                    "Label", "Enabled Flag", "Mode", "Tint", "Offset", "Weights", "Area", "Extent", "Target"
                }));
        }

        /// <summary>
        /// Verifies strings and booleans keep their values and enums are rendered by member name.
        /// </summary>
        [Test]
        public void SerializeProperties_RendersPrimitiveAndEnumValues()
        {
            _fixture.label = "hello";
            _fixture.enabledFlag = true;
            _fixture.mode = ComponentPropertySerializerValueFixture.FixtureMode.Second;

            Dictionary<string, ComponentPropertyInfo> properties = SerializeByName();

            Assert.That(properties["Label"].Value, Is.EqualTo("hello"));
            Assert.That(properties["Label"].Type, Is.EqualTo("String"));
            Assert.That(properties["Enabled Flag"].Value, Is.EqualTo(true));
            Assert.That(properties["Mode"].Value, Is.EqualTo("Second"));
            Assert.That(properties["Mode"].Type, Is.EqualTo("Enum"));
        }

        /// <summary>
        /// Verifies colors, vectors, rects, and bounds are rendered as objects with named components.
        /// </summary>
        [Test]
        public void SerializeProperties_RendersUnityStructsAsNamedComponents()
        {
            _fixture.tint = new Color(0.25f, 0.5f, 0.75f, 1f);
            _fixture.offset = new Vector2(1f, 2f);
            _fixture.weights = new Vector4(1f, 2f, 3f, 4f);
            _fixture.area = new Rect(1f, 2f, 3f, 4f);
            _fixture.extent = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f));

            Dictionary<string, ComponentPropertyInfo> properties = SerializeByName();

            AssertJson(properties["Tint"], "{r:0.25,g:0.5,b:0.75,a:1.0}");
            AssertJson(properties["Offset"], "{x:1.0,y:2.0}");
            AssertJson(properties["Weights"], "{x:1.0,y:2.0,z:3.0,w:4.0}");
            AssertJson(properties["Area"], "{x:1.0,y:2.0,width:3.0,height:4.0}");
            AssertJson(properties["Extent"], "{center:{x:1.0,y:2.0,z:3.0},size:{x:4.0,y:6.0,z:8.0}}");
        }

        /// <summary>
        /// Verifies an unassigned object reference is rendered as None with a zero identifier.
        /// </summary>
        [Test]
        public void SerializeProperties_WithAnEmptyReference_RendersNone()
        {
            Dictionary<string, ComponentPropertyInfo> properties = SerializeByName();

            AssertJson(properties["Target"], "{name:'None',type:'None',entityId:'0'}");
        }

        /// <summary>
        /// Verifies an assigned object reference is rendered by the referenced object's name and type.
        /// </summary>
        [Test]
        public void SerializeProperties_WithALiveReference_RendersItsNameAndType()
        {
            _fixture.target = _referenced;

            JObject target = ToJson(SerializeByName()["Target"]);

            Assert.That((string)target["name"], Is.EqualTo("ReferencedObject"));
            Assert.That((string)target["type"], Is.EqualTo("GameObject"));
            Assert.That((string)target["entityId"], Is.Not.EqualTo("0"));
        }

        /// <summary>
        /// Verifies a reference whose object was destroyed is rendered as Missing with its stored identifier.
        /// </summary>
        [Test]
        public void SerializeProperties_WithADestroyedReference_RendersMissing()
        {
            _fixture.target = _referenced;
            Object.DestroyImmediate(_referenced);

            JObject target = ToJson(SerializeByName()["Target"]);

            Assert.That((string)target["name"], Is.EqualTo("Missing"));
            Assert.That((string)target["type"], Is.EqualTo("Missing"));
            Assert.That((string)target["entityId"], Is.Not.EqualTo("0"));
        }

        private Dictionary<string, ComponentPropertyInfo> SerializeByName()
        {
            return _serializer.SerializeProperties(_fixture).ToDictionary(property => property.Name);
        }

        private static void AssertJson(ComponentPropertyInfo property, string expectedJson)
        {
            // Why DeepEquals with decimal literals: both NUnit and JToken keep integer and float JSON values apart.
            JObject actual = ToJson(property);
            Assert.That(JToken.DeepEquals(actual, JObject.Parse(expectedJson)), Is.True, actual.ToString());
        }

        private static JObject ToJson(ComponentPropertyInfo property)
        {
            return JObject.FromObject(property.Value);
        }
    }

    /// <summary>
    /// Component with one serialized field per value type the property serializer renders.
    /// </summary>
    internal sealed class ComponentPropertySerializerValueFixture : MonoBehaviour
    {
        internal enum FixtureMode
        {
            First,
            Second
        }

        public string label = string.Empty;
        public bool enabledFlag;
        public FixtureMode mode;
        public Color tint;
        public Vector2 offset;
        public Vector4 weights;
        public Rect area;
        public Bounds extent;
        public GameObject target;
    }
}
