using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the validated way into a hot-reload-added field: which values it accepts,
    /// which it refuses before touching the store, and what it reads back.
    /// </summary>
    /// <remarks>
    /// The declaration lookup and the value table are both replaced here, so these tests describe
    /// the entry point's own contracts rather than any particular reload's ledger.
    /// </remarks>
    public class HotReloadAddedFieldWiringTests
    {
        private const string FieldName = "AddedValue";

        private FakeAddedFieldPort _port;
        private IHotReloadAddedFieldPort _previousPort;
        private HotReloadAddedFieldValues _previousValues;

        [SetUp]
        public void SetUp()
        {
            _previousPort = HotReloadAddedFieldCoordination.ActiveFields;
            _previousValues = HotReloadAddedFieldStore.Current;
            _port = new FakeAddedFieldPort();
            HotReloadAddedFieldCoordination.ActiveFields = _port;
            HotReloadAddedFieldStore.Current = new HotReloadAddedFieldValues();
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadAddedFieldCoordination.ActiveFields = _previousPort;
            HotReloadAddedFieldStore.Current = _previousValues;
        }

        /// <summary>
        /// What: a value of the declared type reaches the slot the reading shim reads, under the
        /// store key the worker formed.
        /// </summary>
        [Test]
        public void SetInstanceField_AssignableValue_ReachesTheSlotTheReaderReads()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(int));
            WiringHost host = new WiringHost();

            HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 7);

            Assert.That(
                HotReloadAddedFieldStore.GetOrInit(host, FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName), () => 0),
                Is.EqualTo(7));
        }

        /// <summary>
        /// What: a field name no active reload added is refused, and the message names the added
        /// fields the type does have so the caller can see the typo.
        /// </summary>
        [Test]
        public void SetInstanceField_UnknownFieldName_ThrowsNamingTheActiveFields()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(int));
            WiringHost host = new WiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, "AddedValu", 7));

            Assert.That(error.Message, Does.Contain("AddedValu"));
            Assert.That(error.Message, Does.Contain(FieldName));
        }

        /// <summary>
        /// What: on a type with no added fields at all, the refusal says so rather than printing
        /// an empty list, and points at the reload that has to run first.
        /// </summary>
        [Test]
        public void SetInstanceField_TypeWithNoAddedFields_ThrowsSayingThereAreNone()
        {
            WiringHost host = new WiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 7));

            Assert.That(error.Message, Does.Contain("no active added fields"));
            Assert.That(error.Message, Does.Contain("hot reload"));
            Assert.That(error.Message, Does.Contain("--revert-all"));
        }

        /// <summary>
        /// What: a name that is a real compiled field of the type's base chain is refused, on the
        /// write path, by saying it is an ordinary field rather than with the "no added fields"
        /// hint that would send the caller to re-run a hot reload.
        /// </summary>
        [Test]
        public void SetInstanceField_NameOfACompiledField_SaysItIsAnOrdinaryField()
        {
            DerivedWiringHost host = new DerivedWiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, nameof(WiringHost.CompiledValue), 7));

            Assert.That(error.Message, Is.EqualTo(CompiledFieldMessage()));
            Assert.That(host.CompiledValue, Is.EqualTo(1), "The refusal must not write the compiled field.");
        }

        /// <summary>
        /// What: the read path refuses a compiled field's name with the same message, which names
        /// no write-only step.
        /// </summary>
        [Test]
        public void TryReadInstanceField_NameOfACompiledField_SaysItIsAnOrdinaryField()
        {
            DerivedWiringHost host = new DerivedWiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.TryReadInstanceField(host, nameof(WiringHost.CompiledValue), out object _));

            Assert.That(error.Message, Is.EqualTo(CompiledFieldMessage()));
        }

        /// <summary>
        /// What: a compiled static field named through its type is refused with the same message,
        /// which offers SerializedObject only for a field Unity serializes, and its value stays.
        /// </summary>
        [Test]
        public void SetStaticField_NameOfACompiledStaticField_SaysItIsAnOrdinaryField()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetStaticField(typeof(DerivedWiringHost), nameof(WiringHost.CompiledStatic), 7));

            Assert.That(error.Message, Is.EqualTo(CompiledFieldMessage(nameof(WiringHost.CompiledStatic))));
            Assert.That(WiringHost.CompiledStatic, Is.EqualTo(1), "The refusal must not write the compiled field.");
        }

        private static string CompiledFieldMessage()
        {
            return CompiledFieldMessage(nameof(WiringHost.CompiledValue));
        }

        private static string CompiledFieldMessage(string fieldName)
        {
            return "'" + fieldName + "' is a compiled field of " + typeof(WiringHost).FullName
                + ", not one hot reload added, so this entry point does not serve it. If hot reload "
                + "added it earlier, a compile has since made it an ordinary field. Read or set it "
                + "like any other field (directly, by reflection, or through SerializedObject when "
                + "Unity serializes it); the added-field calls for it are no longer needed.";
        }

        /// <summary>
        /// What: a value the reading shim would reject is refused here instead, naming both types,
        /// and the value already stored survives the refusal.
        /// </summary>
        [Test]
        public void SetInstanceField_WideningNumericValue_ThrowsAndKeepsTheStoredValue()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(long));
            WiringHost host = new WiringHost();
            HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 5L);

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 7));

            Assert.That(error.Message, Does.Contain(typeof(long).FullName));
            Assert.That(error.Message, Does.Contain(typeof(int).FullName));
            Assert.That(error.Message, Does.Contain("even a widening numeric value is refused"));
            Assert.That(
                HotReloadAddedFieldStore.GetOrInit(host, FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName), () => 0L),
                Is.EqualTo(5L),
                "A refused write must leave the slot as it was.");
        }

        /// <summary>
        /// What: a non-numeric value for a numeric field names both types without the cast advice,
        /// since no cast turns that value into a number.
        /// </summary>
        [Test]
        public void SetInstanceField_TextForANumericField_OmitsTheCastAdvice()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(int));
            WiringHost host = new WiringHost();

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, "7"));

            Assert.That(
                error.Message,
                Is.EqualTo(
                    "'" + typeof(WiringHost).FullName + "." + FieldName + "' is declared System.Int32, "
                    + "and a System.String is not one."));
        }

        /// <summary>
        /// What: the GetComponent suggestion spells a nested generic component type the way C#
        /// source writes it, so it can be pasted.
        /// </summary>
        [Test]
        public void SetInstanceField_GameObjectForANestedGenericComponentField_SpellsTheTypeAsSource()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(GenericWiringComponent<WiringHost>));
            WiringHost host = new WiringHost();
            GameObject value = new GameObject("AddedFieldWiringValue");
            try
            {
                ArgumentException error = Assert.Throws<ArgumentException>(
                    () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, value));

                Assert.That(
                    error.Message,
                    Does.Contain(
                        "GetComponent<HotReloadAddedFieldWiringTests.GenericWiringComponent<"
                        + "HotReloadAddedFieldWiringTests.WiringHost>>()"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        /// <summary>
        /// What: a reference-type mismatch names the declared and actual types only, without the
        /// numeric cast advice that cannot apply to it.
        /// </summary>
        [Test]
        public void SetInstanceField_UnrelatedReferenceValue_NamesBothTypesOnly()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(string));
            WiringHost host = new WiringHost();

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, new Uri("http://localhost/")));

            Assert.That(
                error.Message,
                Is.EqualTo(
                    "'" + typeof(WiringHost).FullName + "." + FieldName + "' is declared System.String, "
                    + "and a System.Uri is not one."));
        }

        /// <summary>
        /// What: a GameObject passed for a Component-typed field is refused with the GetComponent
        /// call that yields the value the field accepts.
        /// </summary>
        [Test]
        public void SetInstanceField_GameObjectForAComponentField_SuggestsGetComponent()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(Transform));
            WiringHost host = new WiringHost();
            GameObject value = new GameObject("AddedFieldWiringValue");
            try
            {
                ArgumentException error = Assert.Throws<ArgumentException>(
                    () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, value));

                Assert.That(error.Message, Does.Contain("GetComponent<Transform>()"));
                Assert.That(error.Message, Does.Not.Contain("numeric"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        /// <summary>
        /// What: a Component passed for a GameObject-typed field is refused with the .gameObject
        /// access that yields the value the field accepts.
        /// </summary>
        [Test]
        public void SetInstanceField_ComponentForAGameObjectField_SuggestsGameObject()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(GameObject));
            WiringHost host = new WiringHost();
            GameObject owner = new GameObject("AddedFieldWiringValue");
            try
            {
                ArgumentException error = Assert.Throws<ArgumentException>(
                    () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, owner.transform));

                Assert.That(error.Message, Does.Contain(".gameObject"));
                Assert.That(error.Message, Does.Not.Contain("numeric"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        /// <summary>
        /// What: null is stored for a reference field and refused for a non-nullable value field.
        /// </summary>
        [Test]
        public void SetInstanceField_Null_IsAcceptedForReferenceFieldsOnly()
        {
            _port.AddInstanceField(typeof(WiringHost), "AddedText", typeof(string));
            _port.AddInstanceField(typeof(WiringHost), "AddedCount", typeof(int));
            WiringHost host = new WiringHost();

            HotReloadAddedFieldWiring.SetInstanceField(host, "AddedText", null);
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, "AddedCount", null));

            Assert.That(error.Message, Does.Contain(typeof(int).FullName));
            Assert.That(
                HotReloadAddedFieldWiring.TryReadInstanceField(host, "AddedText", out object stored),
                Is.True);
            Assert.That(stored, Is.Null);
        }

        /// <summary>
        /// What: a static added field cannot be wired through an instance, and an instance field
        /// cannot be wired through its type.
        /// </summary>
        [Test]
        public void SetField_StaticAndInstanceMixUp_IsRefusedBothWays()
        {
            _port.AddInstanceField(typeof(WiringHost), "AddedInstance", typeof(int));
            _port.AddStaticField(typeof(WiringHost), "AddedStatic", typeof(int));
            WiringHost host = new WiringHost();

            InvalidOperationException throughInstance = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, "AddedStatic", 1));
            InvalidOperationException throughType = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetStaticField(typeof(WiringHost), "AddedInstance", 1));

            Assert.That(throughInstance.Message, Does.Contain("static"));
            Assert.That(throughType.Message, Does.Contain("instance"));
        }

        /// <summary>
        /// What: a static added field wired through its type reaches the static slot, and reads
        /// back from it.
        /// </summary>
        [Test]
        public void SetStaticField_AssignableValue_ReachesTheStaticSlot()
        {
            _port.AddStaticField(typeof(WiringHost), FieldName, typeof(string));

            HotReloadAddedFieldWiring.SetStaticField(typeof(WiringHost), FieldName, "wired");

            Assert.That(
                HotReloadAddedFieldStore.GetOrInitStatic(
                    FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName),
                    () => string.Empty),
                Is.EqualTo("wired"));
            Assert.That(
                HotReloadAddedFieldWiring.TryReadStaticField(typeof(WiringHost), FieldName, out object stored),
                Is.True);
            Assert.That(stored, Is.EqualTo("wired"));
        }

        /// <summary>
        /// What: a field a base type declares is reached through a derived instance, and the key
        /// written is the base type's, which is the key the shim in the base type reads.
        /// </summary>
        [Test]
        public void SetInstanceField_FieldDeclaredOnABaseType_WritesTheBaseTypesKey()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(int));
            DerivedWiringHost host = new DerivedWiringHost();

            HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 3);

            Assert.That(
                HotReloadAddedFieldStore.GetOrInit(host, FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName), () => 0),
                Is.EqualTo(3));
        }

        /// <summary>
        /// What: a nested declaring type is looked up by its reflection name while the store key
        /// keeps the metadata spelling the worker gave it.
        /// </summary>
        [Test]
        public void SetInstanceField_NestedDeclaringType_KeepsTheMetadataFormStoreKey()
        {
            _port.AddInstanceField(typeof(WiringHost.NestedHost), FieldName, typeof(int));
            WiringHost.NestedHost host = new WiringHost.NestedHost();
            string metadataKey = FakeAddedFieldPort.KeyOf(typeof(WiringHost.NestedHost), FieldName);
            Assert.That(metadataKey, Does.Contain("/"), "The fake must hand out a metadata-form key.");

            HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 9);

            Assert.That(HotReloadAddedFieldStore.GetOrInit(host, metadataKey, () => 0), Is.EqualTo(9));
        }

        /// <summary>
        /// What: a declared type the worker could not name is refused rather than written
        /// unchecked.
        /// </summary>
        [Test]
        public void SetInstanceField_UnnameableDeclaredType_IsRefused()
        {
            _port.AddDeclaration(
                new HotReloadAddedFieldDeclaration(
                    FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName),
                    typeof(WiringHost).FullName,
                    FieldName,
                    string.Empty,
                    false));
            WiringHost host = new WiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 1));

            Assert.That(error.Message, Does.Contain(FieldName));
            Assert.That(error.Message, Does.Contain("hot reload could not name the declared type of"));
        }

        /// <summary>
        /// What: with no hot-reload domain installed the call is refused, rather than writing a
        /// value nothing would ever read.
        /// </summary>
        [Test]
        public void SetInstanceField_WithNoDomainInstalled_IsRefused()
        {
            HotReloadAddedFieldCoordination.ActiveFields = null;
            WiringHost host = new WiringHost();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 1));

            Assert.That(error.Message, Does.Contain("hot reload"));
        }

        /// <summary>
        /// What: reading a field nothing has written yet reports no stored value and leaves the
        /// slot empty, so the shim still runs the field's initializer.
        /// </summary>
        [Test]
        public void TryReadInstanceField_BeforeAnyWrite_ReportsNoValueAndCreatesNoSlot()
        {
            _port.AddInstanceField(typeof(WiringHost), FieldName, typeof(int));
            WiringHost host = new WiringHost();

            bool read = HotReloadAddedFieldWiring.TryReadInstanceField(host, FieldName, out object stored);

            Assert.That(read, Is.False);
            Assert.That(stored, Is.Null);
            int initializerRuns = 0;
            HotReloadAddedFieldStore.GetOrInit(
                host,
                FakeAddedFieldPort.KeyOf(typeof(WiringHost), FieldName),
                () =>
                {
                    initializerRuns++;
                    return 42;
                });
            Assert.That(initializerRuns, Is.EqualTo(1), "The read must not have filled the slot.");
        }

        /// <summary>
        /// What: a destroyed UnityEngine.Object is refused, even though it is not null to the
        /// plain reference check, because nothing would ever read the value.
        /// </summary>
        [Test]
        public void SetInstanceField_DestroyedUnityObject_IsRefused()
        {
            _port.AddInstanceField(typeof(GameObject), FieldName, typeof(int));
            GameObject host = new GameObject("AddedFieldWiringHost");
            UnityEngine.Object.DestroyImmediate(host);
            Assert.That(ReferenceEquals(host, null), Is.False, "The managed reference is still there.");

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(host, FieldName, 1));

            Assert.That(error.Message, Does.Contain("destroyed"));
            int initializerRuns = 0;
            HotReloadAddedFieldStore.GetOrInit(
                host,
                FakeAddedFieldPort.KeyOf(typeof(GameObject), FieldName),
                () =>
                {
                    initializerRuns++;
                    return 0;
                });
            Assert.That(initializerRuns, Is.EqualTo(1), "The refused write must not have filled the slot.");
        }

        /// <summary>
        /// What: a null instance is refused before anything else is looked up.
        /// </summary>
        [Test]
        public void SetInstanceField_NullInstance_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(
                () => HotReloadAddedFieldWiring.SetInstanceField(null, FieldName, 1));
        }

        /// <summary>
        /// The declaration lookup a hot-reload domain would answer, filled per test.
        /// </summary>
        private sealed class FakeAddedFieldPort : IHotReloadAddedFieldPort
        {
            private readonly Dictionary<string, HotReloadAddedFieldDeclaration> _byLookupKey =
                new Dictionary<string, HotReloadAddedFieldDeclaration>(StringComparer.Ordinal);

            internal static string KeyOf(Type declaringType, string fieldName)
            {
                return MetadataNameOf(declaringType) + HotReloadAddedFieldStore.FieldKeySeparator + fieldName;
            }

            internal void AddInstanceField(Type declaringType, string fieldName, Type declaredType)
            {
                AddDeclaration(CreateDeclaration(declaringType, fieldName, declaredType, false));
            }

            internal void AddStaticField(Type declaringType, string fieldName, Type declaredType)
            {
                AddDeclaration(CreateDeclaration(declaringType, fieldName, declaredType, true));
            }

            internal void AddDeclaration(HotReloadAddedFieldDeclaration declaration)
            {
                _byLookupKey[declaration.DeclaringTypeName + "." + declaration.FieldName] = declaration;
            }

            public bool TryGetDeclaration(
                string declaringTypeName,
                string fieldName,
                out HotReloadAddedFieldDeclaration declaration)
            {
                return _byLookupKey.TryGetValue(declaringTypeName + "." + fieldName, out declaration);
            }

            public IReadOnlyList<string> GetAddedFieldNames(string declaringTypeName)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<string, HotReloadAddedFieldDeclaration> pair in _byLookupKey)
                {
                    if (string.Equals(pair.Value.DeclaringTypeName, declaringTypeName, StringComparison.Ordinal))
                    {
                        names.Add(pair.Value.FieldName);
                    }
                }

                return names;
            }

            private static HotReloadAddedFieldDeclaration CreateDeclaration(
                Type declaringType,
                string fieldName,
                Type declaredType,
                bool isStatic)
            {
                return new HotReloadAddedFieldDeclaration(
                    KeyOf(declaringType, fieldName),
                    declaringType.FullName,
                    fieldName,
                    declaredType.AssemblyQualifiedName,
                    isStatic);
            }

            private static string MetadataNameOf(Type declaringType)
            {
                return declaringType.FullName.Replace('+', '/');
            }
        }

        private class WiringHost
        {
            internal int CompiledValue = 1;

            internal static int CompiledStatic = 1;

            internal class NestedHost
            {
            }
        }

        private sealed class DerivedWiringHost : WiringHost
        {
        }

        private sealed class GenericWiringComponent<T> : MonoBehaviour
        {
        }
    }
}
