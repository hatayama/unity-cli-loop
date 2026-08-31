namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Represents one axis-aligned outline segment in top-left Game View input coordinates.
    /// </summary>
    internal readonly struct RaycastOutlineSegment
    {
        public readonly float StartX;
        public readonly float StartY;
        public readonly float EndX;
        public readonly float EndY;

        public RaycastOutlineSegment(float startX, float startY, float endX, float endY)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
        }
    }
}
