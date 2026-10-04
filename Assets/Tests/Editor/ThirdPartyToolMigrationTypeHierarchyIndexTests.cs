using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the type hierarchy index decides whether an inherited member call reaches
    /// the class that declares a removed timing signature.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTypeHierarchyIndexTests
    {
        private const string RunnerSource = @"
public class Runner
{
    protected void Run(int value, PlayerLoopTiming timing)
    {
    }
}
";

        private const string DerivedCallerSource = @"
public class Derived : Runner
{
    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);
    }
}
";

        /// <summary>
        /// Verifies an unqualified call reaches the declaring class through an intermediate class in another source.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassInOtherSourceDoesNotDeclareMember_ReturnsTrue()
        {
            string mid = "public class Mid : Runner { }";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a base call reaches the declaring class through an intermediate class that does not hide the name.
        /// </summary>
        [Test]
        public void IsBaseMemberReachable_WhenIntermediateClassDoesNotDeclareMember_ReturnsTrue()
        {
            string mid = "public class Mid : Runner { }";
            string derived = @"
public class Derived : Mid
{
    protected void Run(int value)
    {
        base.Run(value, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsBaseMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a same-named method on an intermediate class stops the walk before the declaring class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassDeclaresSameName_ReturnsFalse()
        {
            string mid = @"
public class Mid : Runner
{
    protected void Run(int value)
    {
    }
}
";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies an overload declared by the calling class keeps the call from being attributed to the base class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresOverload_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    protected void Run(int value)
    {
    }

    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a local function with the member name keeps the call from being attributed to the base class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassHasLocalFunctionWithName_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);

        void Run(int value, PlayerLoopTiming timing)
        {
        }
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a delegate field with the member name hides the inherited method.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassHasDelegateFieldWithName_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    protected Action<int, PlayerLoopTiming> Run;

    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies calls in expression positions are not mistaken for declarations of the member name.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassOnlyInvokesName_ReturnsTrue()
        {
            string derived = @"
public class Derived : Runner
{
    public async Task<int> CallAsync(bool ready)
    {
        int assigned = Run(1, PlayerLoopTiming.Update);
        if (Run(2, PlayerLoopTiming.Update))
        {
        }

        await Run(3, PlayerLoopTiming.Update);
        await Task.WhenAll(Run(4, PlayerLoopTiming.Update), Run(5, PlayerLoopTiming.Update));
        int chosen = ready ? Run(6, PlayerLoopTiming.Update) : 0;
        return Run(7, PlayerLoopTiming.Update);
    }

    public int Compute() => Run(8, PlayerLoopTiming.Update);
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a member without an access modifier is private and cannot be reached from a derived class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberHasNoAccessModifier_ReturnsFalse()
        {
            string runner = RunnerSource.Replace("protected void Run", "void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a private member cannot be reached from a derived class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberIsPrivate_ReturnsFalse()
        {
            string runner = RunnerSource.Replace("protected void Run", "private void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies an internal member is reachable from a derived class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberIsInternal_ReturnsTrue()
        {
            string runner = RunnerSource.Replace("protected void Run", "internal void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a base class that no indexed source declares ends the walk without a match.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseClassIsOutsideIndex_ReturnsFalse()
        {
            string derived = DerivedCallerSource.Replace(": Runner", ": MonoBehaviour");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a base name that two imported namespaces both declare is not resolved to either of them.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseNameIsAmbiguous_ReturnsFalse()
        {
            string runnerA = "namespace NsA {" + RunnerSource + "}";
            string runnerB = "namespace NsB {" + RunnerSource + "}";
            string derived = "using NsA;\nusing NsB;\n" + DerivedCallerSource;

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runnerA, runnerB, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "NsA.Runner"), Is.False);
        }

        /// <summary>
        /// Verifies two non-partial declarations of the same qualified name make that class ambiguous.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenClassIsDeclaredTwiceWithoutPartial_ReturnsFalse()
        {
            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, RunnerSource, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies partial parts are merged so the base list of one part applies to calls in the other.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenPartialPartsSplitBaseAndMembers_ReturnsTrue()
        {
            string derivedBase = "public partial class Derived : Runner { }";
            string derivedMembers = DerivedCallerSource.Replace("public class Derived : Runner", "public partial class Derived");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derivedBase, derivedMembers });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies an interface listed first in one partial part does not hide the base class listed in another.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenPartialPartsListDifferentFirstBaseEntries_ReturnsTrue()
        {
            string foo = "public interface IFoo { }";
            string derivedInterfaceOnly = "public partial class Derived : IFoo { }";
            string derivedMembers = DerivedCallerSource.Replace(
                "public class Derived : Runner",
                "public partial class Derived : Runner, IFoo");

            ThirdPartyToolMigrationTypeHierarchyIndex index = ThirdPartyToolMigrationTypeHierarchyIndex.Build(
                new[] { RunnerSource, foo, derivedInterfaceOnly, derivedMembers });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a cyclic base chain stops the walk instead of looping.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseChainHasCycle_ReturnsFalse()
        {
            string first = "public class First : Second { public void Call() { Run(1, PlayerLoopTiming.Update); } }";
            string second = "public class Second : First { }";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, first, second });

            Assert.That(index.IsInheritedMemberReachable("First", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a constructed generic base name resolves to the generic class declaration.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsGeneric_ReturnsTrue()
        {
            string runner = RunnerSource.Replace("public class Runner", "public class Runner<T>");
            string derived = DerivedCallerSource.Replace(": Runner", ": Runner<int>");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a global-qualified base name resolves to the declaring class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsGlobalQualified_ReturnsTrue()
        {
            string runner = "namespace Vendor {" + RunnerSource + "}";
            string derived = DerivedCallerSource.Replace(": Runner", ": global::Vendor.Runner");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Vendor.Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a base name written relative to an enclosing namespace resolves to the declaring class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsRelativeToEnclosingNamespace_ReturnsTrue()
        {
            string runner = "namespace Vendor.Tools {" + RunnerSource + "}";
            string derived = "namespace Vendor.Game {" + DerivedCallerSource.Replace(": Runner", ": Tools.Runner") + "}";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Vendor.Game.Derived", "Run", "Vendor.Tools.Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a base name imported by a using directive resolves to the declaring class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsImportedByUsing_ReturnsTrue()
        {
            string runner = "namespace Vendor.Tools {" + RunnerSource + "}";
            string derived = "using Vendor.Tools;\nnamespace Game {" + DerivedCallerSource + "}";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Game.Derived", "Run", "Vendor.Tools.Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a base name written through a using alias is not resolved.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenBaseUsesAliasDirective_ReturnsFalse()
        {
            string runner = "namespace Vendor {" + RunnerSource + "}";
            string derived = "using R = Vendor.Runner;\n" + DerivedCallerSource.Replace(": Runner", ": R");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Vendor.Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a nested class does not inherit the members of the class that encloses it.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenCallIsInNestedClassOfDerived_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    private sealed class Inner
    {
        public void Call()
        {
            Run(1, PlayerLoopTiming.Update);
        }
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived.Inner", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a record class is indexed with its base list like a class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingTypeIsRecordClass_ReturnsTrue()
        {
            string runner = RunnerSource.Replace("public class Runner", "public record Runner");
            string derived = DerivedCallerSource.Replace("public class Derived", "public record class Derived");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a method returning a tuple type is recognized as a declaration of the member name.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresTupleReturningOverload_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    protected (bool, string) Run(int value)
    {
        return (true, string.Empty);
    }

    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a tuple-returning method on an intermediate class stops the walk before the declaring class.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassDeclaresTupleReturningOverload_ReturnsFalse()
        {
            string mid = @"
public class Mid : Runner
{
    protected (bool, string) Run(int value)
    {
        return (true, string.Empty);
    }
}
";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a method whose return type ends in an array or nullable suffix is recognized as a declaration of the member name.
        /// </summary>
        [TestCase("int[]")]
        [TestCase("int?")]
        [TestCase("(int, int)?")]
        public void IsInheritedMemberReachable_WhenIntermediateClassDeclaresNameAfterArrayOrNullableType_ReturnsFalse(
            string returnTypeName)
        {
            string mid = @"
public class Mid : Runner
{
    protected " + returnTypeName + @" Run(int value)
    {
        return default;
    }
}
";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies the second declarator of a field declaration is recognized as a declaration of the member name.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresNameInMultiDeclaratorField_ReturnsFalse()
        {
            string derived = @"
public class Derived : Runner
{
    private Action<int, PlayerLoopTiming> first, Run;

    public void Call()
    {
        Run(1, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies calls after an if condition or a cast are not mistaken for declarations of the member name.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenCallFollowsCloseParenthesis_ReturnsTrue()
        {
            string derived = @"
public class Derived : Runner
{
    public void Call(bool ready)
    {
        if (ready) Run(1, PlayerLoopTiming.Update);
        object boxed = (object)Run(2, PlayerLoopTiming.Update);
    }
}
";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a member whose return type ends in an array rank specifier keeps its access modifier, so derived
        /// classes reach it unqualified and through base.
        /// </summary>
        [TestCase("int[]")]
        [TestCase("Task<string[]>")]
        [TestCase("(int, int)[]")]
        [TestCase("int[,]")]
        [TestCase("int[][]")]
        [TestCase("int?[]")]
        public void IsInheritedMemberReachable_WhenDeclaringMemberReturnsArray_ReturnsTrue(string returnTypeName)
        {
            string runner = RunnerSource.Replace("protected void Run", "protected " + returnTypeName + " Run");
            string mid = "public class Mid : Runner { }";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
            Assert.That(index.IsBaseMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies the modifier scan stops at an attribute's closing bracket rather than skipping it as a rank
        /// specifier, so the modifiers after the attribute decide the access.
        /// </summary>
        [TestCase("[Obsolete] protected int[] Run", true)]
        [TestCase("[Obsolete] int[] Run", false)]
        public void IsInheritedMemberReachable_WhenArrayReturningMemberHasAttribute_ReadsModifiersAfterAttribute(
            string declarationStart,
            bool expected)
        {
            string runner = RunnerSource.Replace("protected void Run", declarationStart);

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a lambda parameter named like the member hides it, so the class is left unchanged. Names are
        /// collected per class, so the other unqualified calls of the name in that class are left unchanged too.
        /// </summary>
        [TestCase("Use(Run => Run(1, PlayerLoopTiming.Update));")]
        [TestCase("handler = Run => Run(1, PlayerLoopTiming.Update);")]
        [TestCase("Use((Run, x) => Run(x, PlayerLoopTiming.Update));")]
        public void IsInheritedMemberReachable_WhenContainingClassHasLambdaParameterWithName_ReturnsFalse(
            string statement)
        {
            string derived = DerivedCallerSource.Replace(
                "Run(1, PlayerLoopTiming.Update);",
                statement + "\n        Run(2, PlayerLoopTiming.Update);");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies the name passed as an ordinary argument is a use, not a lambda parameter declaration.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenNameIsPassedAsArgument_ReturnsTrue()
        {
            string derived = DerivedCallerSource.Replace(
                "Run(1, PlayerLoopTiming.Update);",
                "Foo(Run, x);\n        Run(1, PlayerLoopTiming.Update);");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies an expression-bodied explicit interface property named like the member is not a declaration this
        /// class can call unqualified, so the inherited member stays reachable.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassHasExpressionBodiedExplicitInterfaceProperty_ReturnsTrue()
        {
            string derived = DerivedCallerSource
                .Replace(": Runner", ": Runner, IFoo")
                .Replace("public void Call()", "int IFoo.Run => 0;\n\n    public void Call()");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(derived, Does.Contain("int IFoo.Run => 0;"));
            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        /// <summary>
        /// Verifies a local introduced by a var deconstruction, a property pattern, or a positional pattern hides the
        /// member, so the class is left unchanged.
        /// </summary>
        [TestCase("var (Run, count) = pair;")]
        [TestCase("var (count, Run) = pair;")]
        [TestCase("foreach (var (Run, n) in pairs) { }")]
        [TestCase("if (pair is var (Run, n)) { }")]
        [TestCase("if (_handler is { } Run) { Run(1, PlayerLoopTiming.Update); }")]
        [TestCase("switch (_handler) { case { } Run: break; }")]
        [TestCase("if (_handler is Holder(1) Run) { }")]
        [TestCase("if (_handler is Holder { } Run) { }")]
        [TestCase("if (pair is (1, 2) Run) { }")]
        [TestCase("int result = _handler switch { { } Run => 1, _ => 0 };")]
        public void IsInheritedMemberReachable_WhenContainingClassHasDesignationWithName_ReturnsFalse(string statement)
        {
            string derived = DerivedCallerSource.Replace(
                "Run(1, PlayerLoopTiming.Update);",
                statement + "\n        Run(2, PlayerLoopTiming.Update);");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        /// <summary>
        /// Verifies a call that starts a statement after a block is a use, not a pattern designation.
        /// </summary>
        [Test]
        public void IsInheritedMemberReachable_WhenCallFollowsBlock_ReturnsTrue()
        {
            string derived = DerivedCallerSource.Replace(
                "Run(1, PlayerLoopTiming.Update);",
                "if (true) { } Run(1, PlayerLoopTiming.Update);");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }
    }
}
