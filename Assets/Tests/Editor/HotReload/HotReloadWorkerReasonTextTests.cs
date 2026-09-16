using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the one place that turns a worker reason code into English. The
    /// expected sentences are literal copies of what the worker used to send over the wire, so a
    /// failure here means a response string changed for the user.
    /// </summary>
    public class HotReloadWorkerReasonTextTests
    {
        /// <summary>
        /// What: every reason code has a byte-match case below, so a code added without a
        /// sentence - or a case left behind after a code was removed - fails here.
        /// </summary>
        [Test]
        public void RenderCases_CoverEveryReasonCode()
        {
            HashSet<string> declared = new HashSet<string>(
                Enum.GetNames(typeof(HotReloadWorkerReasonCode)),
                StringComparer.Ordinal);
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (TestCaseData testCase in RenderCases())
            {
                covered.Add((string)testCase.Arguments[0]);
            }

            List<string> missing = new List<string>(declared);
            missing.RemoveAll(covered.Contains);
            missing.Sort(StringComparer.Ordinal);
            List<string> unknown = new List<string>(covered);
            unknown.RemoveAll(declared.Contains);
            unknown.Sort(StringComparer.Ordinal);

            Assert.That(
                missing,
                Is.Empty,
                "reason codes with no expected sentence: " + string.Join(", ", missing));
            Assert.That(
                unknown,
                Is.Empty,
                "expected sentences for codes that no longer exist: " + string.Join(", ", unknown));
        }

        /// <summary>
        /// What: each reason code renders the exact sentence the worker used to send.
        /// </summary>
        [TestCaseSource(nameof(RenderCases))]
        public void Render_ReasonCode_ProducesTheWireSentence(
            string codeName,
            string[] args,
            string expected)
        {
            // The code is passed by name because the enum is internal to the package and a
            // public test signature cannot name it.
            HotReloadWorkerReasonCode code =
                (HotReloadWorkerReasonCode)Enum.Parse(typeof(HotReloadWorkerReasonCode), codeName);
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto { code = code, args = args };

            Assert.That(HotReloadWorkerReasonText.Render(reason), Is.EqualTo(expected));
        }

        /// <summary>
        /// What: rendering a null reason fails fast instead of producing an empty sentence.
        /// </summary>
        [Test]
        public void Render_NullReason_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => HotReloadWorkerReasonText.Render(null));
        }

        /// <summary>
        /// What: a reason whose argument count does not match its sentence fails fast.
        /// </summary>
        [Test]
        public void Render_ArgumentCountMismatch_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                args = new[] { "A0", "A1" }
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: a detail attached to a reason that does not compose fails fast rather than
        /// being appended to a sentence that has no place for it.
        /// </summary>
        [Test]
        public void Render_DetailOnANonComposingCode_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeChanged,
                args = new[] { "Example.Type" },
                detail = new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved
                }
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: a sentence that takes no values renders the same whether args is null or empty.
        /// </summary>
        [Test]
        public void Render_NullArgs_IsTreatedAsNoArguments()
        {
            TransformWorkerReasonDto withNull = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved
            };
            TransformWorkerReasonDto withEmpty = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                args = Array.Empty<string>()
            };

            Assert.That(
                HotReloadWorkerReasonText.Render(withNull),
                Is.EqualTo(HotReloadWorkerReasonText.Render(withEmpty)));
        }

        private static IEnumerable<TestCaseData> RenderCases()
        {
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                new string[0],
                "Could not resolve a declared type symbol.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                new[] { "Example.Type" },
                "Generic introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypePartial,
                new[] { "Example.Type" },
                "Partial introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeRecord,
                new[] { "Example.Type" },
                "Record introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNonPublic,
                new[] { "Example.Type" },
                "Non-public introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeRefLike,
                new[] { "Example.Type" },
                "Ref-like introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnsafe,
                new[] { "Example.Type" },
                "Unsafe introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnityObject,
                new[] { "Example.Type" },
                "Unity object introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeSerializable,
                new[] { "Example.Type" },
                "Serializable introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeModuleInitializer,
                new[] { "Example.Type" },
                "Module initializer introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnsupported,
                new[] { "Example.Type" },
                "Unsupported introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeConstValueUnverifiable,
                new[] { "Example.Other.Limit", "Example.Type" },
                "Const value cannot be verified: Example.Other.Limit referenced by Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeConstChanged,
                new[] { "Example.Other.Limit", "Example.Type" },
                "Changed const requires a compile: Example.Other.Limit referenced by Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeDelegate,
                new[] { "Example.Handler" },
                "Delegate introduced type requires a compile: Example.Handler");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNested,
                new[] { "Example.Outer/Inner" },
                "Nested type requires a compile: Example.Outer/Inner");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNestedDeclaration,
                new[] { "Example.Outer", "Inner" },
                "Nested declaration inside an introduced type requires a compile: Example.Outer/Inner");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                new[] { "Example.Type" },
                "Changed introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable,
                new[] { "an artifact could not be read." },
                "Introduced types require a compile: an artifact could not be read.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable,
                new string[0],
                "Introduced types require a compile: the target assembly or its references could not be read.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch,
                new string[0],
                "Introduced types require a compile: the target assembly identity does not match the request.");
            yield return Case(
                HotReloadWorkerReasonCode.EditorIsolatedAddedMethodCaller,
                new string[0],
                "Calls an added method whose shim failed to compile; the caller was left unpatched. "
                + "Fix the compile error in the added method (see the Failed row in this response) and reload again, or run 'uloop compile'.");
        }

        private static TestCaseData Case(HotReloadWorkerReasonCode code, string[] args, string expected)
        {
            return new TestCaseData(code.ToString(), args, expected).SetName("Render_" + code);
        }
    }
}
