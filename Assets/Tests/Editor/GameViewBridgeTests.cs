using System.Reflection;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies PlayModeView RenderTexture field resolution used by GameViewBridge.
    /// </summary>
    public class GameViewBridgeTests
    {
        private class FakePlayModeView
        {
#pragma warning disable CS0414 // Field is assigned for reflection-based tests only.
            private object m_TargetTexture = "texture";
#pragma warning restore CS0414
        }

        private class FakeSimulatorWindow : FakePlayModeView
        {
        }

        [Test]
        public void ResolveTargetTextureField_WhenCalledOnDeclaringType_FindsPrivateBaseField()
        {
            // Verifies GetField on the PlayModeView declaring type finds m_TargetTexture.
            FieldInfo field = GameViewBridge.ResolveTargetTextureField(typeof(FakePlayModeView));

            Assert.That(field, Is.Not.Null);
            Assert.That(field.Name, Is.EqualTo("m_TargetTexture"));
            Assert.That(field.DeclaringType, Is.EqualTo(typeof(FakePlayModeView)));
        }

        [Test]
        public void ResolveTargetTextureField_WhenDerivedTypeGetFieldMissesPrivateBaseField_DeclaringTypeStillResolves()
        {
            // Verifies the reflection pitfall: derived-type GetField cannot see private base fields.
            FieldInfo fromDerived = typeof(FakeSimulatorWindow).GetField(
                "m_TargetTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo fromDeclaring = GameViewBridge.ResolveTargetTextureField(typeof(FakePlayModeView));

            Assert.That(fromDerived, Is.Null);
            Assert.That(fromDeclaring, Is.Not.Null);

            FakeSimulatorWindow instance = new();
            object value = fromDeclaring.GetValue(instance);
            Assert.That(value, Is.EqualTo("texture"));
        }

        [Test]
        public void GetRenderTexture_WhenMembersResolved_DoesNotThrow()
        {
            // Verifies PlayModeView type/method/field resolution completes without throwing.
            Assert.DoesNotThrow(() => GameViewBridge.GetRenderTexture());
        }
    }
}
