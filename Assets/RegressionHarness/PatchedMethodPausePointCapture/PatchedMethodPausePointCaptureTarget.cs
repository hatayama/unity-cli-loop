using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    /// <summary>
    /// PlayMode target that mirrors the tester ApplyMove shape: grounded/jump
    /// branches, several locals, comments before the step line, then Move.
    /// The driver pauses on body.Move so a spurious earlier line-108 sequence
    /// point in the hot-reload shim PDB can be distinguished from a real capture.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PatchedMethodPausePointCaptureTarget : MonoBehaviour
    {
        public const float MoveSpeed = 6f;
        public const float JumpSpeed = 6.5f;
        public const float Gravity = -80f;
        public const float TerminalVelocity = -20f;

        private CharacterController body;
        private bool grounded = true;
        private bool jumpPressed;
        private float verticalVelocity = -1f;

        public float VerticalVelocity
        {
            get
            {
                return verticalVelocity;
            }
        }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (body == null)
            {
                return;
            }

            ApplyLook();
            ApplyMove();
        }

        private void ApplyLook()
        {
            // Extra method so the file is not a single-method type, matching the
            // tester file's FindMethodEntryForLine range.
        }

        private void ApplyMove()
        {
            Vector2 input = ReadMoveInput();
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * MoveSpeed;

            if (grounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1f;
            }

            if (grounded && jumpPressed)
            {
                verticalVelocity = JumpSpeed;
            }

            // Comment block before step, matching the tester source layout.
            // A hot-reload shim PDB can emit a second sequence point for the Move
            // line that actually sits at the step assignment.
            float step = Mathf.Min(Time.deltaTime, 1f / 30f);
            verticalVelocity = Mathf.Max(verticalVelocity + Gravity * 1.000f * step, TerminalVelocity);
            Vector3 motion = horizontal + Vector3.up * verticalVelocity;
            body.Move(motion * step);
        }

        private static Vector2 ReadMoveInput()
        {
            return Vector2.zero;
        }
    }
}
