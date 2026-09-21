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
        /// What: a serialized added field of a nested type keeps the worker's metadata name for
        /// telling fields apart, is shown the way C# source spells it, and a row without the
        /// attribute is left out.
        /// </summary>
        [Test]
        public void ListSerializedFields_NestedDeclaringType_KeepsTheMetadataNameAndShowsTheSourceSpelling()
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

            HotReloadSerializedAddedField[] fields =
                HotReloadAddedFieldDeclarationConversion.ListSerializedFields(rows);

            Assert.That(fields.Length, Is.EqualTo(1));
            Assert.That(fields[0].DeclaringTypeName.Value, Is.EqualTo("Game.Outer/Inner"));
            Assert.That(fields[0].ToDisplayName(), Is.EqualTo("Game.Outer.Inner._target"));
        }
    }
}
