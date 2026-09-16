// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity, Newtonsoft and Roslyn references.
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identifies a reason the transform worker reports to the Editor. The wire carries the
    /// member name, so the declaration order here is free to change but a rename is a wire
    /// change. The English sentence for each code lives only in HotReloadWorkerReasonText.
    /// Naming: &lt;family&gt;&lt;original constant name&gt;, where the family is one of MethodTransform,
    /// AddedMethod, AddedField, AddedProperty, Event, UnsupportedMember, Accessor (a fragment
    /// that only appears as the detail of another reason), IntroducedType, or Editor (a reason
    /// the Editor itself produces).
    /// </summary>
    internal enum HotReloadWorkerReasonCode
    {
        IntroducedTypeSymbolUnresolved,
        IntroducedTypeGeneric,
        IntroducedTypePartial,
        IntroducedTypeRecord,
        IntroducedTypeNonPublic,
        IntroducedTypeRefLike,
        IntroducedTypeUnsafe,
        IntroducedTypeUnityObject,
        IntroducedTypeSerializable,
        IntroducedTypeModuleInitializer,
        IntroducedTypeUnsupported,
        IntroducedTypeConstValueUnverifiable,
        IntroducedTypeConstChanged,
        IntroducedTypeDelegate,
        IntroducedTypeNested,
        IntroducedTypeNestedDeclaration,
        IntroducedTypeChanged,
        IntroducedTypeArtifactUnusable,
        IntroducedTypeInputsUnreadable,
        IntroducedTypeIdentityMismatch,
        EditorIsolatedAddedMethodCaller
    }
}
