using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadMethodMatcher"/> resolution and overload selection.
    /// </summary>
    public class HotReloadMethodMatcherTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCoreFixture";

        /// <summary>
        /// What: a known instance method resolves to the live MethodBase with matching MetadataToken identity.
        /// </summary>
        [Test]
        public void Resolve_KnownInstanceMethod_ReturnsLiveMethodBase()
        {
            HotReloadMethodMatchResult result = HotReloadMethodMatcher.Resolve(
                TestAssemblyName,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.Int32", "System.Int32" });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method, Is.Not.Null);
            Assert.That(result.Method.Name, Is.EqualTo(nameof(HotReloadCoreFixture.Add)));
            Assert.That(result.Method.GetParameters().Length, Is.EqualTo(2));

            MethodInfo expected = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.Add),
                new[] { typeof(int), typeof(int) });
            Assert.That(result.Method, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: overload selection uses parameter type full names, picking the three-int Add.
        /// </summary>
        [Test]
        public void Resolve_Overload_SelectsMatchingParameterTypes()
        {
            HotReloadMethodMatchResult result = HotReloadMethodMatcher.Resolve(
                TestAssemblyName,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.Int32", "System.Int32", "System.Int32" });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method.GetParameters().Length, Is.EqualTo(3));

            MethodInfo expected = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.Add),
                new[] { typeof(int), typeof(int), typeof(int) });
            Assert.That(result.Method, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: a parameter-type mismatch yields MethodNotFound rather than a wrong overload.
        /// </summary>
        [Test]
        public void Resolve_ParameterTypeMismatch_ReturnsMethodNotFound()
        {
            HotReloadMethodMatchResult result = HotReloadMethodMatcher.Resolve(
                TestAssemblyName,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.String", "System.Int32" });

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.MethodNotFound));
        }

        /// <summary>
        /// What: a static method with no parameters resolves correctly.
        /// </summary>
        [Test]
        public void Resolve_StaticMethod_ReturnsLiveMethodBase()
        {
            HotReloadMethodMatchResult result = HotReloadMethodMatcher.Resolve(
                TestAssemblyName,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.StaticPing),
                new string[0]);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method.IsStatic, Is.True);
            Assert.That(result.Method, Is.EqualTo(typeof(HotReloadCoreFixture).GetMethod(nameof(HotReloadCoreFixture.StaticPing))));
        }

        /// <summary>
        /// What: a mismatched Mvid against a loaded assembly fails with StaleAssembly without
        /// resolving a method from a stale token.
        /// </summary>
        [Test]
        public void ResolveLoadedMethod_MvidMismatch_ReturnsStaleAssembly()
        {
            MethodInfo knownMethod = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.StaticPing));
            int metadataToken = knownMethod.MetadataToken;

            HotReloadMethodMatchResult result = HotReloadMethodMatcher.ResolveLoadedMethod(
                TestAssemblyName,
                "00000000-0000-0000-0000-000000000000",
                metadataToken);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.StaleAssembly));
            Assert.That(result.Method, Is.Null);
        }
    }
}
