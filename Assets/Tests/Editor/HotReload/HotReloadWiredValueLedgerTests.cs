using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the table of wired values: one entry per host and field, the last
    /// wiring wins, and clearing forgets everything.
    /// </summary>
    public class HotReloadWiredValueLedgerTests
    {
        private const string HostIdentity = "scene:Main|path:Root[0]";
        private const string FieldKey = "Ns.Host::target";
        private const string OtherFieldKey = "Ns.Host::speed";

        /// <summary>
        /// What: wiring the same field of the same host again replaces the earlier value.
        /// </summary>
        [Test]
        public void Record_SameHostAndField_KeepsTheLastValue()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);

            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), false);
            ledger.Record(new HotReloadWiredValueHostKey(HostIdentity, FieldKey), HotReloadWiredValueDescriptor.Plain(2), false);

            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.TryGet(key, out HotReloadWiredValueDescriptor descriptor), Is.True);
            Assert.That(descriptor.PlainValue, Is.EqualTo(2));
        }

        /// <summary>
        /// What: two fields of one host are separate entries, and only a wired field reports
        /// that it has a value.
        /// </summary>
        [Test]
        public void Record_OtherField_IsIndependent()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();

            ledger.Record(new HotReloadWiredValueHostKey(HostIdentity, FieldKey), HotReloadWiredValueDescriptor.Plain(1), false);

            Assert.That(ledger.TryGet(new HotReloadWiredValueHostKey(HostIdentity, OtherFieldKey), out _), Is.False);
            Assert.That(ledger.HasAnyForField(FieldKey), Is.True);
            Assert.That(ledger.HasAnyForField(OtherFieldKey), Is.False);
        }

        /// <summary>
        /// What: Clear forgets every entry.
        /// </summary>
        [Test]
        public void Clear_AfterRecord_ForgetsEverything()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), false);

            ledger.Clear();

            Assert.That(ledger.Count, Is.EqualTo(0));
            Assert.That(ledger.TryGet(key, out _), Is.False);
            Assert.That(ledger.HasAnyForField(FieldKey), Is.False);
        }

        /// <summary>
        /// What: clearing the Play mark of one key keeps its value and leaves other keys marked.
        /// </summary>
        [Test]
        public void ClearPlayMark_KeepsTheValueAndOtherMarks()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);
            HotReloadWiredValueHostKey otherKey = new HotReloadWiredValueHostKey(HostIdentity, OtherFieldKey);
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), true);
            ledger.Record(otherKey, HotReloadWiredValueDescriptor.Plain(2), true);

            ledger.ClearPlayMark(key);

            Assert.That(ledger.WasWiredWhilePlaying(key), Is.False);
            Assert.That(ledger.WasWiredWhilePlaying(otherKey), Is.True);
            Assert.That(ledger.TryGet(key, out HotReloadWiredValueDescriptor descriptor), Is.True);
            Assert.That(descriptor.PlainValue, Is.EqualTo(1));
        }

        /// <summary>
        /// What: wiring a key again outside Play Mode drops the mark that it was wired during Play.
        /// </summary>
        [Test]
        public void Record_InEditMode_ClearsThePlayMark()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), true);
            Assert.That(ledger.WasWiredWhilePlaying(key), Is.True);

            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(2), false);

            Assert.That(ledger.WasWiredWhilePlaying(key), Is.False);
        }

        /// <summary>
        /// What: Remove drops both the entry and its Play mark, leaving other entries alone.
        /// </summary>
        [Test]
        public void Remove_DropsBothTheEntryAndThePlayMark()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);
            HotReloadWiredValueHostKey otherKey = new HotReloadWiredValueHostKey(HostIdentity, OtherFieldKey);
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), true);
            ledger.Record(otherKey, HotReloadWiredValueDescriptor.Plain(2), true);

            ledger.Remove(key);

            Assert.That(ledger.TryGet(key, out _), Is.False);
            Assert.That(ledger.WasWiredWhilePlaying(key), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.WasWiredWhilePlaying(otherKey), Is.True);
        }

        /// <summary>
        /// What: Clear drops the Play marks together with the entries.
        /// </summary>
        [Test]
        public void Clear_DropsThePlayMarks()
        {
            HotReloadWiredValueLedger ledger = new HotReloadWiredValueLedger();
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(HostIdentity, FieldKey);
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1), true);

            ledger.Clear();

            Assert.That(ledger.WasWiredWhilePlaying(key), Is.False);
        }
    }
}
