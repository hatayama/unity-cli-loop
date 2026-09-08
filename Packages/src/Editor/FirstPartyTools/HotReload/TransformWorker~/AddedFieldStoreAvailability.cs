/// <summary>
/// Why a value can or cannot live in the added-field store. Fields and auto-properties share the
/// store, so both classify against this and each phrase the outcome in their own wording.
/// </summary>
internal enum AddedFieldStoreAvailability
{
    Available,
    StructHost,
    ValueTypeUnresolved,
    ValueTypeNotExternallyVisible,
    InitializerNotEmittable
}
