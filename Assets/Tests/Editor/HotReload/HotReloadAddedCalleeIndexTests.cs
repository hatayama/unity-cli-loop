using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for how a group's worker entries name the added members they call: the wire
    /// keys in calledAddedMethodKeys are turned into the labels the added-member ledger uses, with
    /// the file that declared each member.
    /// </summary>
    public class HotReloadAddedCalleeIndexTests
    {
        private const string HostPath = "Assets/Host.cs";
        private const string CallerPath = "Assets/Caller.cs";

        /// <summary>
        /// What: a call is recorded with the label the ledger registers the added member under,
        /// nested types in their reflection spelling, and with the file that declared the member.
        /// The keys are written as the worker emits them, so a key format that drifts from the
        /// worker's fails here.
        /// </summary>
        [TestCase("Ns.Outer/Inner", "Bar", new[] { "System.Int32" }, "Ns.Outer/Inner::Bar(System.Int32)", "Ns.Outer+Inner.Bar(System.Int32)")]
        [TestCase("Ns.Host", "get_Speed", new string[0], "Ns.Host::get_Speed()", "Ns.Host.get_Speed()")]
        public void Resolve_CallToAnAddedMember_RecordsItsLedgerLabelAndDeclaringFile(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            string wireKey,
            string expectedLabel)
        {
            TransformWorkerEntryDto caller = BuildCallerEntry(wireKey);
            HotReloadAddedCalleeIndex index = new HotReloadAddedCalleeIndex(
                new[]
                {
                    BuildAddedEntry(typeMetadataName, methodName, parameterTypeFullNames),
                    caller
                });

            (IReadOnlyList<HotReloadCalledAddedMember> callees, string errorMessage) = index.Resolve(caller);

            Assert.That(errorMessage, Is.Null);
            Assert.That(callees.Count, Is.EqualTo(1));
            Assert.That(callees[0].Label, Is.EqualTo(expectedLabel));
            Assert.That(callees[0].DeclaringFilePath, Is.EqualTo(HostPath));
        }

        /// <summary>
        /// What: an entry that calls no added member records an empty list rather than null, so a
        /// caller never has to tell "calls none" from "not recorded".
        /// </summary>
        [Test]
        public void Resolve_EntryWithoutCalls_RecordsNone()
        {
            TransformWorkerEntryDto caller = BuildCallerEntry();
            caller.calledAddedMethodKeys = null;
            HotReloadAddedCalleeIndex index = new HotReloadAddedCalleeIndex(new[] { caller });

            (IReadOnlyList<HotReloadCalledAddedMember> callees, string errorMessage) = index.Resolve(caller);

            Assert.That(errorMessage, Is.Null);
            Assert.That(callees, Is.Empty);
        }

        /// <summary>
        /// What: a call whose key names no added entry of the group is refused with an error naming
        /// the key, whether no entry has that key at all or only an entry that patches an existing
        /// method does (that entry is not an added member, so its label must not be recorded).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Resolve_CallNamingNoAddedEntry_ReportsTheKey(bool groupHasExistingMethodEntryWithThatKey)
        {
            const string wireKey = "Ns.Host::Bar(System.Int32)";
            TransformWorkerEntryDto caller = BuildCallerEntry(wireKey);
            List<TransformWorkerEntryDto> entries = new List<TransformWorkerEntryDto> { caller };
            if (groupHasExistingMethodEntryWithThatKey)
            {
                TransformWorkerEntryDto existing = BuildAddedEntry("Ns.Host", "Bar", new[] { "System.Int32" });
                existing.patchKind = null;
                entries.Add(existing);
            }

            HotReloadAddedCalleeIndex index = new HotReloadAddedCalleeIndex(entries.ToArray());

            (IReadOnlyList<HotReloadCalledAddedMember> callees, string errorMessage) = index.Resolve(caller);

            Assert.That(callees, Is.Null);
            Assert.That(errorMessage, Does.Contain(wireKey));
        }

        private static TransformWorkerEntryDto BuildAddedEntry(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = HostPath,
                typeMetadataName = typeMetadataName,
                methodName = methodName,
                parameterTypeFullNames = parameterTypeFullNames,
                patchKind = HotReloadConstants.PatchKindAddedMethod
            };
        }

        private static TransformWorkerEntryDto BuildCallerEntry(params string[] calledAddedMethodKeys)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = CallerPath,
                typeMetadataName = "Ns.Caller",
                methodName = "Call",
                parameterTypeFullNames = new string[0],
                calledAddedMethodKeys = calledAddedMethodKeys
            };
        }
    }
}
