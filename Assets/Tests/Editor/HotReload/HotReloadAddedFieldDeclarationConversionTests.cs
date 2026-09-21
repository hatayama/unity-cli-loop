using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for how the worker's added-field rows cross into the Editor.
    /// </summary>
    public class HotReloadAddedFieldDeclarationConversionTests
    {
        /// <summary>
        /// What: a serialized added field of a nested type is named the way C# source spells it,
        /// with neither the worker's '/' nor reflection's '+', and a row without the attribute is
        /// left out.
        /// </summary>
        [Test]
        public void ListSerializedFieldDisplayNames_NestedDeclaringType_UsesSourceSpelling()
        {
            TransformWorkerAddedFieldDeclarationDto[] rows =
            {
                new TransformWorkerAddedFieldDeclarationDto
                {
                    fieldKey = "Game.Outer/Inner::_target",
                    declaringTypeMetadataName = "Game.Outer/Inner",
                    fieldName = "_target",
                    hasSerializationAttribute = true
                },
                new TransformWorkerAddedFieldDeclarationDto
                {
                    fieldKey = "Game.Outer/Inner::_plain",
                    declaringTypeMetadataName = "Game.Outer/Inner",
                    fieldName = "_plain",
                    hasSerializationAttribute = false
                }
            };

            Assert.That(
                HotReloadAddedFieldDeclarationConversion.ListSerializedFieldDisplayNames(rows),
                Is.EqualTo(new[] { "Game.Outer.Inner._target" }));
        }
    }
}
