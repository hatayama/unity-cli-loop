using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for which serialized added fields the domain reports as new: each field is
    /// told apart by where it is declared, not by how its name reads.
    /// </summary>
    public class HotReloadSerializedAddedFieldRecordTests
    {
        private const string FirstAssemblyFile = "Assets/FirstModule/Host.cs";

        private const string SecondAssemblyFile = "Assets/SecondModule/Host.cs";

        private HotReloadDomainTestScope _scope;
        private HotReloadDomainTestAccess _access;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _access = new HotReloadDomainTestAccess();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: a field with the same type and field names as one already reported, declared in
        /// a file of another assembly, is reported on its first run instead of being taken for
        /// the one already reported.
        /// </summary>
        [Test]
        public void Take_SameNamesInAnotherAssemblysFile_ReportsTheSecondOnItsFirstRun()
        {
            _access.ReplaceSerializedAddedFields(FirstAssemblyFile, new[] { Field("Game.Host", "count") });
            Assert.That(
                _access.Domain.TakeUnreportedSerializedAddedFields(),
                Is.EqualTo(new[] { "Game.Host.count" }));

            _access.ReplaceSerializedAddedFields(SecondAssemblyFile, new[] { Field("Game.Host", "count") });

            Assert.That(
                _access.Domain.TakeUnreportedSerializedAddedFields(),
                Is.EqualTo(new[] { "Game.Host.count" }));
        }

        /// <summary>
        /// What: a nested type and a namespace that read the same in C# are two fields, and both
        /// are listed even though their names print alike.
        /// </summary>
        [Test]
        public void Take_NestedTypeAndNamespaceThatReadAlike_ListsBoth()
        {
            _access.ReplaceSerializedAddedFields(
                FirstAssemblyFile,
                new[] { Field("Game.Outer/Inner", "count"), Field("Game.Outer.Inner", "count") });

            Assert.That(
                _access.Domain.TakeUnreportedSerializedAddedFields(),
                Is.EqualTo(new[] { "Game.Outer.Inner.count", "Game.Outer.Inner.count" }));
        }

        /// <summary>
        /// What: a run that keeps a reported field and adds others reports only the new ones, all
        /// in the one list a single warning line is built from.
        /// </summary>
        [Test]
        public void Take_KeptFieldPlusNewOnes_ReportsOnlyTheNewOnesTogether()
        {
            _access.ReplaceSerializedAddedFields(FirstAssemblyFile, new[] { Field("Game.Host", "kept") });
            _access.Domain.TakeUnreportedSerializedAddedFields();

            _access.ReplaceSerializedAddedFields(
                FirstAssemblyFile,
                new[] { Field("Game.Host", "kept"), Field("Game.Host", "second") });
            _access.ReplaceSerializedAddedFields(SecondAssemblyFile, new[] { Field("Game.Other", "third") });

            Assert.That(
                _access.Domain.TakeUnreportedSerializedAddedFields(),
                Is.EqualTo(new[] { "Game.Host.second", "Game.Other.third" }));
        }

        private static HotReloadSerializedAddedField Field(string metadataTypeName, string fieldName)
        {
            return new HotReloadSerializedAddedField(new HotReloadMetadataTypeName(metadataTypeName), fieldName);
        }
    }
}
