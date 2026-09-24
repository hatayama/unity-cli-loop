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

            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1));
            ledger.Record(new HotReloadWiredValueHostKey(HostIdentity, FieldKey), HotReloadWiredValueDescriptor.Plain(2));

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

            ledger.Record(new HotReloadWiredValueHostKey(HostIdentity, FieldKey), HotReloadWiredValueDescriptor.Plain(1));

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
            ledger.Record(key, HotReloadWiredValueDescriptor.Plain(1));

            ledger.Clear();

            Assert.That(ledger.Count, Is.EqualTo(0));
            Assert.That(ledger.TryGet(key, out _), Is.False);
            Assert.That(ledger.HasAnyForField(FieldKey), Is.False);
        }
    }
}
