namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the added-field slot of one host the way a patched method would, so a wired value
    /// whose host is back at its place is restored without waiting for game code to read it. Kept
    /// behind an interface so the refresh that drives it is testable without an installed domain.
    /// </summary>
    internal interface IHotReloadWiredValueSlotReader
    {
        /// <summary>
        /// True when the slot of <paramref name="storeFieldKey"/> on <paramref name="host"/> holds
        /// a value the field can use after the read. False when the field is not an instance field
        /// an active reload added under exactly that key, or no value came back.
        /// </summary>
        bool TryRead(object host, string storeFieldKey);
    }
}
