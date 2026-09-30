using System;
using System.Collections.Generic;
using System.Reflection;

using Mono.Cecil;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the method label every hot reload row and ledger shares: the label
    /// built from a resolved method must equal the label built from the same method's metadata
    /// spelling, which is what worker rows and call-site hits carry.
    /// </summary>
    public class HotReloadMethodLabelParityTests
    {
        private const BindingFlags FixtureMethodFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private AssemblyDefinition _fixtureAssembly;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _fixtureAssembly = AssemblyDefinition.ReadAssembly(
                typeof(HotReloadLabelShapeHost).Assembly.Location,
                new ReaderParameters { InMemory = true });
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fixtureAssembly?.Dispose();
            _fixtureAssembly = null;
        }

        private static IEnumerable<TestCaseData> ParameterShapeMethods()
        {
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeInt));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeNested));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeList));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeDictionaryOfLists));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeGrid));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeCube));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeGridOfVectors));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeVector));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeJagged));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeVectorOfLists));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeListOfGrids));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeRefList));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeOutGrid));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeNullable));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeTuple));
            yield return Shape(
                typeof(HotReloadLabelShapeHost),
                nameof(HotReloadLabelShapeHost.TakeNestedOfConstructedOuter));
            yield return Shape(
                typeof(HotReloadLabelShapeHost),
                nameof(HotReloadLabelShapeHost.TakeNestedGenericOfConstructedOuter));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeListOfNested));
            yield return Shape(typeof(HotReloadLabelShapeHost), nameof(HotReloadLabelShapeHost.TakeGeneric));
            yield return Shape(
                typeof(HotReloadLabelShapeGenericHost<>),
                nameof(HotReloadLabelShapeGenericHost<int>.TakeTypeParameterList));
            yield return Shape(
                typeof(HotReloadLabelShapeGenericHost<>),
                nameof(HotReloadLabelShapeGenericHost<int>.TakeSelf));
            yield return Shape(
                typeof(HotReloadLabelShapeGenericHost<>),
                nameof(HotReloadLabelShapeGenericHost<int>.TakeOwnInner));
            yield return Shape(
                typeof(HotReloadLabelShapeGenericHost<>),
                nameof(HotReloadLabelShapeGenericHost<int>.TakeOwnInnerOf));
        }

        /// <summary>
        /// Verifies that for each parameter shape a worker row or a call-site hit can carry, the
        /// label built from the resolved method reads the same as the label built from Cecil's
        /// spelling of that method, so a label made in either world matches the other.
        /// </summary>
        [TestCaseSource(nameof(ParameterShapeMethods))]
        public void FormatMethodLabel_ParameterShape_MatchesTheLabelBuiltFromTheMetadataSpelling(
            Type hostType,
            string methodName)
        {
            MethodInfo method = hostType.GetMethod(methodName, FixtureMethodFlags);
            Assert.That(method, Is.Not.Null, "Precondition: the fixture method must exist.");
            string fromMetadata = FormatLabelFromMetadataSpelling(method);

            string fromMethod = HotReloadMethodKeys.FormatMethodLabel(method);

            Assert.That(fromMethod, Is.EqualTo(fromMetadata));
        }

        private static TestCaseData Shape(Type hostType, string methodName)
        {
            return new TestCaseData(hostType, methodName);
        }

        private string FormatLabelFromMetadataSpelling(MethodInfo method)
        {
            MethodDefinition definition = (MethodDefinition)_fixtureAssembly.MainModule.LookupToken(method.MetadataToken);
            string[] parameterTypeFullNames = new string[definition.Parameters.Count];
            for (int index = 0; index < definition.Parameters.Count; index++)
            {
                parameterTypeFullNames[index] = definition.Parameters[index].ParameterType.FullName;
            }

            return HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(definition.DeclaringType.FullName),
                definition.Name,
                parameterTypeFullNames,
                definition.GenericParameters.Count);
        }
    }

    public sealed class HotReloadLabelShapeHost
    {
        public sealed class Inner
        {
        }

        public void TakeInt(int value)
        {
        }

        public void TakeNested(Inner value)
        {
        }

        public void TakeList(List<int> values)
        {
        }

        public void TakeDictionaryOfLists(Dictionary<string, List<int>> values)
        {
        }

        public void TakeGrid(int[,] values)
        {
        }

        public void TakeCube(int[,,] values)
        {
        }

        public void TakeGridOfVectors(int[,][] values)
        {
        }

        public void TakeVector(int[] values)
        {
        }

        public void TakeJagged(int[][] values)
        {
        }

        public void TakeVectorOfLists(List<int>[] values)
        {
        }

        public void TakeListOfGrids(List<int[,]> values)
        {
        }

        public void TakeRefList(ref List<int> values)
        {
        }

        public void TakeOutGrid(out int[,] values)
        {
            values = null;
        }

        public void TakeNullable(int? value)
        {
        }

        public void TakeTuple((int, string) value)
        {
        }

        public void TakeNestedOfConstructedOuter(HotReloadLabelShapeGenericHost<int>.Inner value)
        {
        }

        public void TakeNestedGenericOfConstructedOuter(HotReloadLabelShapeGenericHost<int>.InnerOf<string> value)
        {
        }

        public void TakeListOfNested(List<Inner> values)
        {
        }

        public void TakeGeneric<TItem>(TItem value, List<TItem> values)
        {
        }
    }

    public sealed class HotReloadLabelShapeGenericHost<T>
    {
        public sealed class Inner
        {
        }

        public sealed class InnerOf<TInner>
        {
        }

        public void TakeTypeParameterList(List<T> values)
        {
        }

        public void TakeSelf(HotReloadLabelShapeGenericHost<T> other)
        {
        }

        public void TakeOwnInner(Inner inner)
        {
        }

        public void TakeOwnInnerOf(InnerOf<string> inner)
        {
        }
    }
}
