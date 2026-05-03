namespace io.github.hatayama.UnityCliLoop
{
    public static class PlayerConstants
    {
        public const int TargetFrameRate = 60;
        public const float FixedDeltaTime = 1f / TargetFrameRate;

        public const float MoveSpeed = 5f;
        public const float SprintMultiplier = 1.5f;
        public const float JumpForce = 7f;
        public const float Gravity = -20f;
        public const float MouseSensitivity = 0.3f;
        public const float MinPitch = -89f;
        public const float MaxPitch = 89f;
        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.3f;
        public const float GroundedDownForce = -2f;
    }
}
