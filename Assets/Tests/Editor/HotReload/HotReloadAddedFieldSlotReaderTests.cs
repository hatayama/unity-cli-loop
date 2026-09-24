using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the slot reader a status refresh uses: which added-field declaration it
    /// matches a store key to, and that a match reads the slot through the store's restore path.
    /// </summary>
    public class HotReloadAddedFieldSlotReaderTests
    {
        private const string FieldName = "target";
        private const string IntTypeName = "System.Int32";

        private FakeFieldPort _port;
        private StubRestorer _restorer;
        private HotReloadAddedFieldValues _values;
        private HotReloadAddedFieldSlotReader _reader;

        [SetUp]
        public void SetUp()
        {
            _port = new FakeFieldPort();
            _restorer = new StubRestorer(7);
            _values = new HotReloadAddedFieldValues { Restorer = _restorer };
            _reader = new HotReloadAddedFieldSlotReader(_port, _values);
        }

        /// <summary>
        /// What: a declaration whose key matches reads the restored value into the slot, so the
        /// store then reads it as stored.
        /// </summary>
        [Test]
        public void TryRead_MatchingDeclaration_RestoresTheValueIntoTheSlot()
        {
            string key = StoreKey(typeof(SlotReaderOuter).FullName);
            _port.Add(typeof(SlotReaderOuter).FullName, Declaration(key, IntTypeName, false));
            SlotReaderOuter host = new SlotReaderOuter();

            Assert.That(_reader.TryRead(host, key), Is.True);

            Assert.That(_values.TryGet(host, key, typeof(int), out object value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(_restorer.Calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a field name no active reload added reads nothing and never asks the restorer.
        /// </summary>
        [Test]
        public void TryRead_UnknownFieldName_ReturnsFalseWithoutRestoring()
        {
            string key = StoreKey(typeof(SlotReaderOuter).FullName);

            Assert.That(_reader.TryRead(new SlotReaderOuter(), key), Is.False);

            Assert.That(_restorer.Calls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a static added field is not an instance slot of the host, so it is not read.
        /// </summary>
        [Test]
        public void TryRead_StaticDeclaration_ReturnsFalse()
        {
            string key = StoreKey(typeof(SlotReaderOuter).FullName);
            _port.Add(typeof(SlotReaderOuter).FullName, Declaration(key, IntTypeName, true));

            Assert.That(_reader.TryRead(new SlotReaderOuter(), key), Is.False);

            Assert.That(_restorer.Calls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the same field name declared under another store key is another slot, so a key
        /// that names a different declaring type is not read.
        /// </summary>
        [Test]
        public void TryRead_SameFieldNameUnderAnotherKey_ReturnsFalse()
        {
            string declaredKey = StoreKey(typeof(SlotReaderOuter).FullName);
            _port.Add(typeof(SlotReaderOuter).FullName, Declaration(declaredKey, IntTypeName, false));

            Assert.That(_reader.TryRead(new SlotReaderOuter(), StoreKey("Other.Type")), Is.False);

            Assert.That(_restorer.Calls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a field a base type declares is found through a derived host.
        /// </summary>
        [Test]
        public void TryRead_FieldDeclaredOnABaseType_IsReadThroughTheDerivedHost()
        {
            string key = StoreKey(typeof(SlotReaderBase).FullName);
            _port.Add(typeof(SlotReaderBase).FullName, Declaration(key, IntTypeName, false));

            Assert.That(_reader.TryRead(new SlotReaderDerived(), key), Is.True);
        }

        /// <summary>
        /// What: a nested declaring type is asked for by its reflection name ('+') while the store
        /// key keeps the metadata name ('/'), and the two still match.
        /// </summary>
        [Test]
        public void TryRead_NestedDeclaringType_MatchesTheMetadataNameKey()
        {
            string key = StoreKey(typeof(SlotReaderOuter).FullName + "/" + nameof(SlotReaderOuter.Inner));
            _port.Add(typeof(SlotReaderOuter.Inner).FullName, Declaration(key, IntTypeName, false));

            Assert.That(_reader.TryRead(new SlotReaderOuter.Inner(), key), Is.True);

            Assert.That(_port.AskedTypeNames, Does.Contain(typeof(SlotReaderOuter).FullName + "+" + nameof(SlotReaderOuter.Inner)));
        }

        /// <summary>
        /// What: a declared field type that no longer resolves reads nothing.
        /// </summary>
        [Test]
        public void TryRead_UnresolvableFieldType_ReturnsFalse()
        {
            string key = StoreKey(typeof(SlotReaderOuter).FullName);
            _port.Add(typeof(SlotReaderOuter).FullName, Declaration(key, "No.Such.Type, NoSuchAssembly", false));

            Assert.That(_reader.TryRead(new SlotReaderOuter(), key), Is.False);

            Assert.That(_restorer.Calls, Is.EqualTo(0));
        }

        private static string StoreKey(string declaringTypeMetadataName)
        {
            return declaringTypeMetadataName + HotReloadAddedFieldStore.FieldKeySeparator + FieldName;
        }

        private static HotReloadAddedFieldDeclaration Declaration(string storeFieldKey, string fieldTypeName, bool isStatic)
        {
            return new HotReloadAddedFieldDeclaration(storeFieldKey, "unused", FieldName, fieldTypeName, isStatic);
        }

        private sealed class FakeFieldPort : IHotReloadAddedFieldPort
        {
            private readonly Dictionary<(string TypeFullName, string FieldName), HotReloadAddedFieldDeclaration> _declarations =
                new Dictionary<(string TypeFullName, string FieldName), HotReloadAddedFieldDeclaration>();

            internal List<string> AskedTypeNames { get; } = new List<string>();

            internal void Add(string typeFullName, HotReloadAddedFieldDeclaration declaration)
            {
                _declarations[(typeFullName, declaration.FieldName)] = declaration;
            }

            public bool TryGetDeclaration(string declaringTypeName, string fieldName, out HotReloadAddedFieldDeclaration declaration)
            {
                AskedTypeNames.Add(declaringTypeName);
                return _declarations.TryGetValue((declaringTypeName, fieldName), out declaration);
            }

            public IReadOnlyList<string> GetAddedFieldNames(string declaringTypeName)
            {
                return new List<string>();
            }
        }

        private sealed class StubRestorer : IHotReloadWiredValuePersistence
        {
            private readonly object _value;

            internal StubRestorer(object value)
            {
                _value = value;
            }

            internal int Calls { get; private set; }

            public void Record(object host, string storeFieldKey, object value)
            {
            }

            public bool TryRestore(object host, string storeFieldKey, out object value)
            {
                Calls++;
                value = _value;
                return true;
            }

            public bool TryRestoreAgain(object host, string storeFieldKey, ref int lastAttemptGeneration, out object value)
            {
                return TryRestore(host, storeFieldKey, out value);
            }
        }
    }

    // Top-level on purpose, so Inner is exactly one level of nesting and its names differ only in
    // the nesting separator.
    internal class SlotReaderOuter
    {
        internal sealed class Inner
        {
        }
    }

    internal class SlotReaderBase
    {
    }

    internal sealed class SlotReaderDerived : SlotReaderBase
    {
    }
}
