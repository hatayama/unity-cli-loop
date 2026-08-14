using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the added-field side table: instance CWT storage, static storage,
    /// type-change reinitialization, and Clear.
    /// </summary>
    public class HotReloadAddedFieldStoreTests
    {
        private const string HostTypeName = "Host";
        private const string FieldName = "count";

        [TearDown]
        public void TearDown()
        {
            HotReloadAddedFieldStore.Clear();
        }

        /// <summary>
        /// What: a missing instance field runs the initializer once and then returns the stored
        /// value without running it again.
        /// </summary>
        [Test]
        public void GetOrInit_MissingThenPresent_RunsInitializerOnce()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
            int calls = 0;

            int first = HotReloadAddedFieldStore.GetOrInit(host, key, () =>
            {
                calls++;
                return 3;
            });
            int second = HotReloadAddedFieldStore.GetOrInit(host, key, () =>
            {
                calls++;
                return 99;
            });

            Assert.That(first, Is.EqualTo(3));
            Assert.That(second, Is.EqualTo(3));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a null initializer stores default(T) so a field with no initializer reads as
        /// the type's default until Set.
        /// </summary>
        [Test]
        public void GetOrInit_NullInitializer_StoresDefault()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);

            int value = HotReloadAddedFieldStore.GetOrInit<int>(host, key, null);

            Assert.That(value, Is.EqualTo(0));
        }

        /// <summary>
        /// What: Set overwrites the stored value so a later GetOrInit returns it.
        /// </summary>
        [Test]
        public void Set_ThenGetOrInit_ReturnsStoredValue()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);

            HotReloadAddedFieldStore.Set(host, key, 8);
            int value = HotReloadAddedFieldStore.GetOrInit(host, key, () => 1);

            Assert.That(value, Is.EqualTo(8));
        }

        /// <summary>
        /// What: storing null for a reference field is a real value, not a miss that re-runs
        /// the initializer.
        /// </summary>
        [Test]
        public void Set_NullReference_GetOrInitDoesNotReinitialize()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, "label");
            int calls = 0;

            HotReloadAddedFieldStore.Set<string>(host, key, null);
            string value = HotReloadAddedFieldStore.GetOrInit(host, key, () =>
            {
                calls++;
                return "fresh";
            });

            Assert.That(value, Is.Null);
            Assert.That(calls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: storing null for Nullable&lt;T&gt; is a real value, not a miss that re-runs
        /// the initializer (boxed null is a null reference, unlike non-nullable value types).
        /// </summary>
        [Test]
        public void Set_NullNullableInt_GetOrInitDoesNotReinitialize()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
            int calls = 0;

            HotReloadAddedFieldStore.Set<int?>(host, key, null);
            int? value = HotReloadAddedFieldStore.GetOrInit<int?>(host, key, () =>
            {
                calls++;
                return 7;
            });

            Assert.That(value, Is.Null);
            Assert.That(calls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a stored value whose runtime type is not T is discarded and the initializer
        /// runs again (added-field type change).
        /// </summary>
        [Test]
        public void GetOrInit_TypeMismatch_DiscardsAndReinitializes()
        {
            StoreHost host = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
            int calls = 0;

            HotReloadAddedFieldStore.Set(host, key, "old");
            int value = HotReloadAddedFieldStore.GetOrInit(host, key, () =>
            {
                calls++;
                return 7;
            });

            Assert.That(value, Is.EqualTo(7));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: two host instances keep independent values for the same field key.
        /// </summary>
        [Test]
        public void GetOrInit_TwoInstances_AreIndependent()
        {
            StoreHost left = new StoreHost();
            StoreHost right = new StoreHost();
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);

            HotReloadAddedFieldStore.Set(left, key, 1);
            HotReloadAddedFieldStore.Set(right, key, 2);

            Assert.That(HotReloadAddedFieldStore.GetOrInit(left, key, () => 0), Is.EqualTo(1));
            Assert.That(HotReloadAddedFieldStore.GetOrInit(right, key, () => 0), Is.EqualTo(2));
        }

        /// <summary>
        /// What: static GetOrInit/Set share one table keyed only by fieldKey.
        /// </summary>
        [Test]
        public void GetOrInitStatic_SetStatic_RoundTrip()
        {
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, "seed");
            int calls = 0;

            int first = HotReloadAddedFieldStore.GetOrInitStatic(key, () =>
            {
                calls++;
                return 4;
            });
            HotReloadAddedFieldStore.SetStatic(key, 11);
            int second = HotReloadAddedFieldStore.GetOrInitStatic(key, () =>
            {
                calls++;
                return 99;
            });

            Assert.That(first, Is.EqualTo(4));
            Assert.That(second, Is.EqualTo(11));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a static type mismatch discards the old value and reinitializes.
        /// </summary>
        [Test]
        public void GetOrInitStatic_TypeMismatch_DiscardsAndReinitializes()
        {
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, "seed");
            HotReloadAddedFieldStore.SetStatic(key, "old");

            int value = HotReloadAddedFieldStore.GetOrInitStatic(key, () => 5);

            Assert.That(value, Is.EqualTo(5));
        }

        /// <summary>
        /// What: Clear drops both instance and static entries so the next read reinitializes.
        /// </summary>
        [Test]
        public void Clear_DropsInstanceAndStaticValues()
        {
            StoreHost host = new StoreHost();
            string instanceKey = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
            string staticKey = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, "seed");
            HotReloadAddedFieldStore.Set(host, instanceKey, 1);
            HotReloadAddedFieldStore.SetStatic(staticKey, 2);

            HotReloadAddedFieldStore.Clear();

            Assert.That(HotReloadAddedFieldStore.GetOrInit(host, instanceKey, () => 10), Is.EqualTo(10));
            Assert.That(HotReloadAddedFieldStore.GetOrInitStatic(staticKey, () => 20), Is.EqualTo(20));
        }

        /// <summary>
        /// What: FormatFieldKey joins type metadata name and field name with the store separator.
        /// </summary>
        [Test]
        public void FormatFieldKey_JoinsTypeAndField()
        {
            Assert.That(
                HotReloadAddedFieldStore.FormatFieldKey("Ns.Host", "count"),
                Is.EqualTo("Ns.Host" + HotReloadAddedFieldStore.FieldKeySeparator + "count"));
        }

        private sealed class StoreHost
        {
        }
    }
}
