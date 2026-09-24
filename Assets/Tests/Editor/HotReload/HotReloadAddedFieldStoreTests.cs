using System;
using System.Collections.Generic;

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
        /// What: reading or writing an instance field through a null receiver throws
        /// NullReferenceException, as compiled field access does, without running the
        /// initializer.
        /// </summary>
        [Test]
        public void GetOrInitAndSet_NullInstance_ThrowNullReferenceException()
        {
            string key = HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
            int calls = 0;

            Assert.Throws<NullReferenceException>(() => HotReloadAddedFieldStore.GetOrInit(null, key, () =>
            {
                calls++;
                return 3;
            }));
            Assert.Throws<NullReferenceException>(() => HotReloadAddedFieldStore.Set<int>(null, key, 8));

            Assert.That(calls, Is.EqualTo(0));
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

        /// <summary>
        /// What: a slot the host never had is filled from the restorer without running the
        /// initializer, which is how a wired value reaches the instance a scene reload made.
        /// </summary>
        [Test]
        public void GetOrInit_RestorerHasValue_ReturnsItWithoutRunningInitializer()
        {
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = new CountingRestorer(true, 5) };
            int initializerCalls = 0;

            int value = values.GetOrInit(new StoreHost(), FieldKey(), () =>
            {
                initializerCalls++;
                return 1;
            });

            Assert.That(value, Is.EqualTo(5));
            Assert.That(initializerCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a restored value of another type is replaced by the initializer's value rather
        /// than left in the slot for the next read.
        /// </summary>
        [Test]
        public void GetOrInit_RestoredValueOfAnotherType_IsOverwrittenByTheInitializer()
        {
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = new CountingRestorer(true, "text") };
            StoreHost host = new StoreHost();

            int value = values.GetOrInit(host, FieldKey(), () => 3);

            Assert.That(value, Is.EqualTo(3));
            Assert.That(values.TryGet(host, FieldKey(), typeof(int), out object stored), Is.True);
            Assert.That(stored, Is.EqualTo(3));
        }

        /// <summary>
        /// What: when the restorer has nothing, the initializer's value is what the field reads, and
        /// later reads only retry the restore instead of asking for a first restore again.
        /// </summary>
        [Test]
        public void GetOrInit_RestorerHasNothing_RunsInitializerAndDoesNotAskAgain()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();

            int first = values.GetOrInit(host, FieldKey(), () => 4);
            int second = values.GetOrInit(host, FieldKey(), () => 9);

            Assert.That(first, Is.EqualTo(4));
            Assert.That(second, Is.EqualTo(4));
            Assert.That(restorer.Calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a read that creates no slot still takes a restorable value, and keeps it, so the
        /// wiring read-back sees what the scene reload brought back.
        /// </summary>
        [Test]
        public void TryGet_NoSlotAndRestorerHasValue_ReturnsAndKeepsTheRestoredValue()
        {
            CountingRestorer restorer = new CountingRestorer(true, 6);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();

            Assert.That(values.TryGet(host, FieldKey(), typeof(int), out object first), Is.True);
            Assert.That(values.TryGet(host, FieldKey(), typeof(int), out object second), Is.True);

            Assert.That(first, Is.EqualTo(6));
            Assert.That(second, Is.EqualTo(6));
            Assert.That(restorer.Calls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a restored value the field's type cannot hold is not reported and leaves no slot,
        /// so the next GetOrInit runs the initializer instead of disagreeing with the read.
        /// </summary>
        [Test]
        public void TryGet_RestoredValueOfAnotherType_ReturnsFalseAndCreatesNoSlot()
        {
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = new CountingRestorer(true, "text") };
            StoreHost host = new StoreHost();

            bool read = values.TryGet(host, FieldKey(), typeof(int), out object stored);

            Assert.That(read, Is.False);
            Assert.That(stored, Is.Null);
            Assert.That(values.GetOrInit(host, FieldKey(), () => 8), Is.EqualTo(8));
        }

        /// <summary>
        /// What: a read whose restore failed keeps asking on later reads, so a value whose host
        /// came back reaches the field instead of the initializer staying for good.
        /// </summary>
        [Test]
        public void GetOrInit_RestoreFailsThenSucceeds_ReturnsTheRestoredValueOnTheLaterRead()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();

            int first = values.GetOrInit(host, FieldKey(), () => 1);
            restorer.RetryHasValue = true;
            restorer.RetryValue = 5;
            int second = values.GetOrInit(host, FieldKey(), () => 1);
            int third = values.GetOrInit(host, FieldKey(), () => 1);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(5));
            Assert.That(third, Is.EqualTo(5));
            Assert.That(restorer.RetryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// What: the store hands the restorer back the generation it stored on the last retry, so
        /// the restorer can skip a retry nothing has changed for.
        /// </summary>
        [Test]
        public void GetOrInit_OnAPendingSlot_PassesTheGenerationTheRestorerStoredLastTime()
        {
            CountingRestorer restorer = new CountingRestorer(false, null) { GenerationToStore = 7 };
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();

            values.GetOrInit(host, FieldKey(), () => 1);
            values.GetOrInit(host, FieldKey(), () => 1);
            values.GetOrInit(host, FieldKey(), () => 1);

            Assert.That(restorer.GenerationsSeen, Is.EqualTo(new[] { 0, 7 }));
        }

        /// <summary>
        /// What: a write to a field whose restore is pending settles it, so the written value is
        /// never replaced by a later restore.
        /// </summary>
        [Test]
        public void Set_OnAPendingSlot_StopsAskingTheRestorer()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();
            values.GetOrInit(host, FieldKey(), () => 1);

            values.Set(host, FieldKey(), 8);
            restorer.RetryHasValue = true;
            restorer.RetryValue = 5;
            int value = values.GetOrInit(host, FieldKey(), () => 1);

            Assert.That(value, Is.EqualTo(8));
            Assert.That(restorer.RetryCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the read-back of a field whose restore is pending retries it and reports the
        /// restored value, matching what the next GetOrInit returns.
        /// </summary>
        [Test]
        public void TryGet_OnAPendingSlot_RetriesAndReportsTheRestoredValue()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();
            values.GetOrInit(host, FieldKey(), () => 1);

            restorer.RetryHasValue = true;
            restorer.RetryValue = 5;
            bool read = values.TryGet(host, FieldKey(), typeof(int), out object stored);

            Assert.That(read, Is.True);
            Assert.That(stored, Is.EqualTo(5));
            Assert.That(values.GetOrInit(host, FieldKey(), () => 1), Is.EqualTo(5));
        }

        /// <summary>
        /// What: the read-back of a field whose restore is still pending reports the initializer's
        /// value, which is what the shim reads.
        /// </summary>
        [Test]
        public void TryGet_OnAPendingSlotStillUnrestored_ReportsTheInitializerValue()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();
            values.GetOrInit(host, FieldKey(), () => 1);

            bool read = values.TryGet(host, FieldKey(), typeof(int), out object stored);

            Assert.That(read, Is.True);
            Assert.That(stored, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a retry that brings back a value the field's type cannot hold keeps the
        /// initializer's value and settles the slot, so the restorer is not asked forever.
        /// </summary>
        [Test]
        public void GetOrInit_RestoredValueOfAnotherTypeOnAPendingSlot_KeepsTheInitializerAndStopsRetrying()
        {
            CountingRestorer restorer = new CountingRestorer(false, null);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = restorer };
            StoreHost host = new StoreHost();
            values.GetOrInit(host, FieldKey(), () => 1);

            restorer.RetryHasValue = true;
            restorer.RetryValue = "text";
            int second = values.GetOrInit(host, FieldKey(), () => 9);
            int third = values.GetOrInit(host, FieldKey(), () => 9);

            Assert.That(second, Is.EqualTo(1));
            Assert.That(third, Is.EqualTo(1));
            Assert.That(restorer.RetryCalls, Is.EqualTo(1));
        }

        private static string FieldKey()
        {
            return HotReloadAddedFieldStore.FormatFieldKey(HostTypeName, FieldName);
        }

        private sealed class CountingRestorer : IHotReloadWiredValuePersistence
        {
            private readonly bool _hasValue;
            private readonly object _value;

            internal CountingRestorer(bool hasValue, object value)
            {
                _hasValue = hasValue;
                _value = value;
            }

            internal int Calls { get; private set; }

            public void Record(object host, string storeFieldKey, object value)
            {
            }

            public bool TryRestore(object host, string storeFieldKey, out object value)
            {
                Calls++;
                value = _hasValue ? _value : null;
                return _hasValue;
            }

            internal bool RetryHasValue { get; set; }

            internal object RetryValue { get; set; }

            internal int GenerationToStore { get; set; }

            internal int RetryCalls { get; private set; }

            internal List<int> GenerationsSeen { get; } = new List<int>();

            public bool TryRestoreAgain(object host, string storeFieldKey, ref int lastAttemptGeneration, out object value)
            {
                RetryCalls++;
                GenerationsSeen.Add(lastAttemptGeneration);
                lastAttemptGeneration = GenerationToStore;
                value = RetryHasValue ? RetryValue : null;
                return RetryHasValue;
            }
        }

        private sealed class StoreHost
        {
        }
    }
}
