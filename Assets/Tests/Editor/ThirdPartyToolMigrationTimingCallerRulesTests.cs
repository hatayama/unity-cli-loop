using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration rewrites callers of methods whose PlayerLoopTiming parameter was removed.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingCallerRulesTests
    {
        /// <summary>
        /// Verifies a method group reference is left untouched while a real call is rewritten.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenMethodIsReferencedAsMethodGroup_RewritesOnlyCall()
        {
            string source =
                "class Runner\n{\n    void Call()\n    {\n        Action<int, PlayerLoopTiming> action = Run;\n" +
                "        Run(1, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature("Runner") },
                    Array.Empty<string>(),
                    ThirdPartyToolMigrationTypeHierarchyIndex.Empty);

            Assert.That(
                content,
                Is.EqualTo(
                    "class Runner\n{\n    void Call()\n    {\n        Action<int, PlayerLoopTiming> action = Run;\n" +
                    "        Run(1);\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a call whose argument list never closes is left untouched.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenArgumentListIsUnterminated_KeepsSource()
        {
            string source = "class Runner\n{\n    void Call() { Run(1, PlayerLoopTiming.Update";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature("Runner") },
                    Array.Empty<string>(),
                    ThirdPartyToolMigrationTypeHierarchyIndex.Empty);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a call nested in the arguments of another call to the same method is rewritten as well as the outer call.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenCallIsNestedInSameMethodCall_RewritesBothCalls()
        {
            string source =
                "class Runner\n{\n    void Call()\n    {\n" +
                "        Run(Run(1, PlayerLoopTiming.Update), PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature("Runner") },
                    Array.Empty<string>(),
                    ThirdPartyToolMigrationTypeHierarchyIndex.Empty);

            Assert.That(
                content,
                Is.EqualTo("class Runner\n{\n    void Call()\n    {\n        Run(Run(1));\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(2));
        }

        // The Runner declaration is written in its migrated form: the invocation pattern also matches declarations,
        // and in the real flow the declaration is migrated before its callers are revisited.
        private const string MigratedRunnerSource =
            "public class Runner\n{\n    protected void Run(int value)\n    {\n    }\n}\n";

        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenDerivedCallsInheritedMethod_RemovesTimingFromEveryForm()
        {
            // Verifies unqualified, this. and base. calls of an inherited method in a derived class all lose the timing argument.
            string source = MigratedRunnerSource +
                "public class Derived : Runner\n{\n    public void Call()\n    {\n" +
                "        Run(1, PlayerLoopTiming.Update);\n" +
                "        this.Run(2, PlayerLoopTiming.Update);\n" +
                "        base.Run(3, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) = RemoveCallerArgumentsWithIndex(
                source,
                new[] { CreateValueAndTimingSignature("Runner") });

            Assert.That(
                content,
                Is.EqualTo(
                    MigratedRunnerSource +
                    "public class Derived : Runner\n{\n    public void Call()\n    {\n" +
                    "        Run(1);\n" +
                    "        this.Run(2);\n" +
                    "        base.Run(3);\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(3));
        }

        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenDerivedDeclaresOverload_RewritesOnlyBaseCall()
        {
            // Verifies an overload in the derived class keeps unqualified and this. calls while base. still reaches the base method.
            string derivedHeader =
                "public class Derived : Runner\n{\n    protected void Run(int value)\n    {\n    }\n\n" +
                "    public void Call()\n    {\n" +
                "        Run(1, PlayerLoopTiming.Update);\n" +
                "        this.Run(2, PlayerLoopTiming.Update);\n";
            string source = MigratedRunnerSource + derivedHeader +
                "        base.Run(3, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) = RemoveCallerArgumentsWithIndex(
                source,
                new[] { CreateValueAndTimingSignature("Runner") });

            Assert.That(
                content,
                Is.EqualTo(MigratedRunnerSource + derivedHeader + "        base.Run(3);\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenDerivedCallsMethodOnExpressionReceiver_KeepsCall()
        {
            // Verifies a call on a returned object is not treated as a call of the inherited method.
            string source = MigratedRunnerSource +
                "public class Other\n{\n    public void Run(int value, PlayerLoopTiming timing)\n    {\n    }\n}\n" +
                "public class Derived : Runner\n{\n    private Other GetOther()\n    {\n        return new Other();\n    }\n\n" +
                "    public void Call()\n    {\n        GetOther().Run(1, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) = RemoveCallerArgumentsWithIndex(
                source,
                new[] { CreateValueAndTimingSignature("Runner") });

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenDeclaringTypeCallsMethodOnExpressionReceiver_KeepsCall()
        {
            // Verifies a call on a returned object inside the declaring type is not treated as a call of the declaring type's method.
            string source =
                "public class Other\n{\n    public void Run(int value, PlayerLoopTiming timing)\n    {\n    }\n}\n" +
                "public class Runner\n{\n    protected void Run(int value)\n    {\n    }\n\n" +
                "    private Other GetOther()\n    {\n        return new Other();\n    }\n\n" +
                "    public void Call()\n    {\n        GetOther().Run(1, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature("Runner") },
                    Array.Empty<string>(),
                    ThirdPartyToolMigrationTypeHierarchyIndex.Empty);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenDerivedConstructsDeclaringType_KeepsConstructorCall()
        {
            // Verifies a constructor call in a derived class is not treated as an inherited member call.
            // Create returns object: a Runner return type would declare the name in Derived and keep the call
            // unchanged even without the constructor check.
            string source =
                "public class Runner\n{\n    public Runner()\n    {\n    }\n}\n" +
                "public class Derived : Runner\n{\n    public object Create()\n    {\n" +
                "        return new Runner(PlayerLoopTiming.Update);\n    }\n}\n";
            RemovedLegacyPlayerLoopTimingSignature constructorSignature = new(
                "Runner",
                "Runner",
                new[] { new LegacyPlayerLoopTimingParameterDeclaration(0, "PlayerLoopTiming", "timing", false) },
                new[] { new RemovedLegacyPlayerLoopTimingParameter(0, "timing") });

            (string content, int replacementCount) = RemoveCallerArgumentsWithIndex(
                source,
                new[] { constructorSignature });

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenSignaturesShareMethodName_RewritesOnlyInheritedOne(
            bool otherSignatureFirst)
        {
            // Verifies only the signature declared up the derived class's chain rewrites the call, in either signature order.
            string otherSource = "public class Other\n{\n    public void Run(int value)\n    {\n    }\n}\n";
            string source = MigratedRunnerSource + otherSource +
                "public class Derived : Runner\n{\n    public void Call()\n    {\n" +
                "        Run(1, PlayerLoopTiming.Update);\n    }\n}\n";
            RemovedLegacyPlayerLoopTimingSignature runnerSignature = CreateValueAndTimingSignature("Runner");
            RemovedLegacyPlayerLoopTimingSignature otherSignature = CreateValueAndTimingSignature("Other");
            RemovedLegacyPlayerLoopTimingSignature[] signatures = otherSignatureFirst
                ? new[] { otherSignature, runnerSignature }
                : new[] { runnerSignature, otherSignature };

            (string content, int replacementCount) = RemoveCallerArgumentsWithIndex(source, signatures);

            Assert.That(
                content,
                Is.EqualTo(
                    MigratedRunnerSource + otherSource +
                    "public class Derived : Runner\n{\n    public void Call()\n    {\n" +
                    "        Run(1);\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        private static (string Content, int ReplacementCount) RemoveCallerArgumentsWithIndex(
            string source,
            RemovedLegacyPlayerLoopTimingSignature[] signatures)
        {
            return ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                source,
                signatures,
                Array.Empty<string>(),
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { source }));
        }

        private static RemovedLegacyPlayerLoopTimingSignature CreateValueAndTimingSignature(string declaringTypeName)
        {
            return new RemovedLegacyPlayerLoopTimingSignature(
                "Run",
                declaringTypeName,
                new[]
                {
                    new LegacyPlayerLoopTimingParameterDeclaration(0, "int", "value", false),
                    new LegacyPlayerLoopTimingParameterDeclaration(1, "PlayerLoopTiming", "timing", false)
                },
                new[] { new RemovedLegacyPlayerLoopTimingParameter(1, "timing") });
        }
    }
}
