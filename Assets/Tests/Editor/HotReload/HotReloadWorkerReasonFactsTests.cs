using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the facts a worker reason keeps beside its sentence and how two of them compare.
    /// </summary>
    public class HotReloadWorkerReasonFactsTests
    {
        /// <summary>
        /// What: From copies the code, values, and declaring files of the reason and of every
        /// detail it composes.
        /// </summary>
        [Test]
        public void From_ReasonWithDetail_CopiesTheWholeChain()
        {
            TransformWorkerReasonDto dto = Reason(
                HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                new[] { "Host.Callee()" },
                Reason(HotReloadWorkerReasonCode.AddedMethodBodyUnbound, new[] { "CS0103: missing" }, null));
            dto.declaringFiles = new[] { "Assets/Host.cs" };

            HotReloadWorkerReasonFacts facts = HotReloadWorkerReasonFacts.From(dto);

            Assert.That(facts.Code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall));
            Assert.That(facts.Args, Is.EqualTo(new[] { "Host.Callee()" }));
            Assert.That(facts.DeclaringFiles, Is.EqualTo(new[] { "Assets/Host.cs" }));
            Assert.That(facts.Detail.Code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyUnbound));
            Assert.That(facts.Detail.Args, Is.EqualTo(new[] { "CS0103: missing" }));
            Assert.That(facts.Detail.DeclaringFiles, Is.Empty);
            Assert.That(facts.Detail.Detail, Is.Null);
        }

        /// <summary>
        /// What: reasons with the same code, values, and detail match.
        /// </summary>
        [Test]
        public void Matches_SameCodeValuesAndDetail_IsTrue()
        {
            HotReloadWorkerReasonFacts left = HotReloadWorkerReasonFacts.From(WithDetail("CS0103: a"));
            HotReloadWorkerReasonFacts right = HotReloadWorkerReasonFacts.From(WithDetail("CS0103: a"));

            Assert.That(left.Matches(right), Is.True);
        }

        /// <summary>
        /// What: reasons that differ only in one value do not match.
        /// </summary>
        [Test]
        public void Matches_DifferentValue_IsFalse()
        {
            HotReloadWorkerReasonFacts left = HotReloadWorkerReasonFacts.From(
                Reason(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall, new[] { "Host.A()" }, null));
            HotReloadWorkerReasonFacts right = HotReloadWorkerReasonFacts.From(
                Reason(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall, new[] { "Host.B()" }, null));

            Assert.That(left.Matches(right), Is.False);
        }

        /// <summary>
        /// What: reasons that differ only in their detail do not match, and neither does a reason
        /// with a detail against one without.
        /// </summary>
        [Test]
        public void Matches_DifferentOrMissingDetail_IsFalse()
        {
            HotReloadWorkerReasonFacts left = HotReloadWorkerReasonFacts.From(WithDetail("CS0103: a"));
            HotReloadWorkerReasonFacts right = HotReloadWorkerReasonFacts.From(WithDetail("CS0103: b"));
            HotReloadWorkerReasonFacts noDetail = HotReloadWorkerReasonFacts.From(
                Reason(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall, new[] { "Host.Callee()" }, null));

            Assert.That(left.Matches(right), Is.False);
            Assert.That(left.Matches(noDetail), Is.False);
            Assert.That(noDetail.Matches(left), Is.False);
        }

        /// <summary>
        /// What: a null value matches an empty one, as the sentence renders both as nothing.
        /// </summary>
        [Test]
        public void Matches_NullAgainstEmptyValue_IsTrue()
        {
            HotReloadWorkerReasonFacts left = HotReloadWorkerReasonFacts.From(
                Reason(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall, new string[] { null }, null));
            HotReloadWorkerReasonFacts right = HotReloadWorkerReasonFacts.From(
                Reason(HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall, new[] { string.Empty }, null));

            Assert.That(left.Matches(right), Is.True);
        }

        /// <summary>
        /// What: declaring files are not compared, since the values already name the types.
        /// </summary>
        [Test]
        public void Matches_DifferentDeclaringFilesOnly_IsTrue()
        {
            TransformWorkerReasonDto leftDto = WithDetail("CS0103: a");
            leftDto.declaringFiles = new[] { "Assets/A.cs" };
            TransformWorkerReasonDto rightDto = WithDetail("CS0103: a");

            Assert.That(
                HotReloadWorkerReasonFacts.From(leftDto).Matches(HotReloadWorkerReasonFacts.From(rightDto)),
                Is.True);
        }

        private static TransformWorkerReasonDto WithDetail(string detailValue)
        {
            return Reason(
                HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                new[] { "Host.Callee()" },
                Reason(HotReloadWorkerReasonCode.AddedMethodBodyUnbound, new[] { detailValue }, null));
        }

        private static TransformWorkerReasonDto Reason(
            HotReloadWorkerReasonCode code,
            string[] args,
            TransformWorkerReasonDto detail)
        {
            return new TransformWorkerReasonDto { code = code, args = args, detail = detail };
        }
    }
}
