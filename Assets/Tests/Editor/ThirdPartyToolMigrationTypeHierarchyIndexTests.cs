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

        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassInOtherSourceDoesNotDeclareMember_ReturnsTrue()
        {
            // Verifies an unqualified call reaches the declaring class through an intermediate class in another source.
            string mid = "public class Mid : Runner { }";
            string derived = DerivedCallerSource.Replace(": Runner", ": Mid");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, mid, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsBaseMemberReachable_WhenIntermediateClassDoesNotDeclareMember_ReturnsTrue()
        {
            // Verifies a base call reaches the declaring class through an intermediate class that does not hide the name.
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

        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassDeclaresSameName_ReturnsFalse()
        {
            // Verifies a same-named method on an intermediate class stops the walk before the declaring class.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresOverload_ReturnsFalse()
        {
            // Verifies an overload declared by the calling class keeps the call from being attributed to the base class.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassHasLocalFunctionWithName_ReturnsFalse()
        {
            // Verifies a local function with the member name keeps the call from being attributed to the base class.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassHasDelegateFieldWithName_ReturnsFalse()
        {
            // Verifies a delegate field with the member name hides the inherited method.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassOnlyInvokesName_ReturnsTrue()
        {
            // Verifies calls in expression positions are not mistaken for declarations of the member name.
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

        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberHasNoAccessModifier_ReturnsFalse()
        {
            // Verifies a member without an access modifier is private and cannot be reached from a derived class.
            string runner = RunnerSource.Replace("protected void Run", "void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberIsPrivate_ReturnsFalse()
        {
            // Verifies a private member cannot be reached from a derived class.
            string runner = RunnerSource.Replace("protected void Run", "private void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenDeclaringMemberIsInternal_ReturnsTrue()
        {
            // Verifies an internal member is reachable from a derived class.
            string runner = RunnerSource.Replace("protected void Run", "internal void Run");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseClassIsOutsideIndex_ReturnsFalse()
        {
            // Verifies a base class that no indexed source declares ends the walk without a match.
            string derived = DerivedCallerSource.Replace(": Runner", ": MonoBehaviour");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseNameIsAmbiguous_ReturnsFalse()
        {
            // Verifies a base name that two imported namespaces both declare is not resolved to either of them.
            string runnerA = "namespace NsA {" + RunnerSource + "}";
            string runnerB = "namespace NsB {" + RunnerSource + "}";
            string derived = "using NsA;\nusing NsB;\n" + DerivedCallerSource;

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runnerA, runnerB, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "NsA.Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenClassIsDeclaredTwiceWithoutPartial_ReturnsFalse()
        {
            // Verifies two non-partial declarations of the same qualified name make that class ambiguous.
            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, RunnerSource, DerivedCallerSource });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenPartialPartsSplitBaseAndMembers_ReturnsTrue()
        {
            // Verifies partial parts are merged so the base list of one part applies to calls in the other.
            string derivedBase = "public partial class Derived : Runner { }";
            string derivedMembers = DerivedCallerSource.Replace("public class Derived : Runner", "public partial class Derived");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, derivedBase, derivedMembers });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenPartialPartsListDifferentFirstBaseEntries_ReturnsTrue()
        {
            // Verifies an interface listed first in one partial part does not hide the base class listed in another.
            string foo = "public interface IFoo { }";
            string derivedInterfaceOnly = "public partial class Derived : IFoo { }";
            string derivedMembers = DerivedCallerSource.Replace(
                "public class Derived : Runner",
                "public partial class Derived : Runner, IFoo");

            ThirdPartyToolMigrationTypeHierarchyIndex index = ThirdPartyToolMigrationTypeHierarchyIndex.Build(
                new[] { RunnerSource, foo, derivedInterfaceOnly, derivedMembers });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseChainHasCycle_ReturnsFalse()
        {
            // Verifies a cyclic base chain stops the walk instead of looping.
            string first = "public class First : Second { public void Call() { Run(1, PlayerLoopTiming.Update); } }";
            string second = "public class Second : First { }";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { RunnerSource, first, second });

            Assert.That(index.IsInheritedMemberReachable("First", "Run", "Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsGeneric_ReturnsTrue()
        {
            // Verifies a constructed generic base name resolves to the generic class declaration.
            string runner = RunnerSource.Replace("public class Runner", "public class Runner<T>");
            string derived = DerivedCallerSource.Replace(": Runner", ": Runner<int>");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsGlobalQualified_ReturnsTrue()
        {
            // Verifies a global-qualified base name resolves to the declaring class.
            string runner = "namespace Vendor {" + RunnerSource + "}";
            string derived = DerivedCallerSource.Replace(": Runner", ": global::Vendor.Runner");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Vendor.Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsRelativeToEnclosingNamespace_ReturnsTrue()
        {
            // Verifies a base name written relative to an enclosing namespace resolves to the declaring class.
            string runner = "namespace Vendor.Tools {" + RunnerSource + "}";
            string derived = "namespace Vendor.Game {" + DerivedCallerSource.Replace(": Runner", ": Tools.Runner") + "}";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Vendor.Game.Derived", "Run", "Vendor.Tools.Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseIsImportedByUsing_ReturnsTrue()
        {
            // Verifies a base name imported by a using directive resolves to the declaring class.
            string runner = "namespace Vendor.Tools {" + RunnerSource + "}";
            string derived = "using Vendor.Tools;\nnamespace Game {" + DerivedCallerSource + "}";

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Game.Derived", "Run", "Vendor.Tools.Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenBaseUsesAliasDirective_ReturnsFalse()
        {
            // Verifies a base name written through a using alias is not resolved.
            string runner = "namespace Vendor {" + RunnerSource + "}";
            string derived = "using R = Vendor.Runner;\n" + DerivedCallerSource.Replace(": Runner", ": R");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Vendor.Runner"), Is.False);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenCallIsInNestedClassOfDerived_ReturnsFalse()
        {
            // Verifies a nested class does not inherit the members of the class that encloses it.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingTypeIsRecordClass_ReturnsTrue()
        {
            // Verifies a record class is indexed with its base list like a class.
            string runner = RunnerSource.Replace("public class Runner", "public record Runner");
            string derived = DerivedCallerSource.Replace("public class Derived", "public record class Derived");

            ThirdPartyToolMigrationTypeHierarchyIndex index =
                ThirdPartyToolMigrationTypeHierarchyIndex.Build(new[] { runner, derived });

            Assert.That(index.IsInheritedMemberReachable("Derived", "Run", "Runner"), Is.True);
        }

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresTupleReturningOverload_ReturnsFalse()
        {
            // Verifies a method returning a tuple type is recognized as a declaration of the member name.
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

        [Test]
        public void IsInheritedMemberReachable_WhenIntermediateClassDeclaresTupleReturningOverload_ReturnsFalse()
        {
            // Verifies a tuple-returning method on an intermediate class stops the walk before the declaring class.
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

        [Test]
        public void IsInheritedMemberReachable_WhenContainingClassDeclaresNameInMultiDeclaratorField_ReturnsFalse()
        {
            // Verifies the second declarator of a field declaration is recognized as a declaration of the member name.
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

        [Test]
        public void IsInheritedMemberReachable_WhenCallFollowsCloseParenthesis_ReturnsTrue()
        {
            // Verifies calls after an if condition or a cast are not mistaken for declarations of the member name.
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
    }
}
