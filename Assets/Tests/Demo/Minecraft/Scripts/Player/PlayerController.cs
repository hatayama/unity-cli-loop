using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MinecraftPlayerController : MonoBehaviour
    {
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference sprintAction;

        private CharacterController characterController;
        private float verticalVelocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            Debug.Assert(characterController != null, "CharacterController component required");
        }

        private void Start()
        {
            UnityEngine.Application.targetFrameRate = PlayerConstants.TargetFrameRate;
        }

        private void OnEnable()
        {
            Debug.Assert(moveAction != null, "moveAction must be assigned in Inspector");
            Debug.Assert(jumpAction != null, "jumpAction must be assigned in Inspector");
            Debug.Assert(sprintAction != null, "sprintAction must be assigned in Inspector");

            moveAction.action.Enable();
            jumpAction.action.Enable();
            sprintAction.action.Enable();
        }

        private void OnDisable()
        {
            moveAction.action.Disable();
            jumpAction.action.Disable();
            sprintAction.action.Disable();
        }

        private void Update()
        {
            Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
            bool isSprinting = sprintAction.action.IsPressed();

            float speed = isSprinting
                ? PlayerConstants.MoveSpeed * PlayerConstants.SprintMultiplier
                : PlayerConstants.MoveSpeed;

            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            moveDirection *= speed;

            if (characterController.isGrounded)
            {
                // CharacterController.isGrounded returns false without slight downward movement in previous frame
                verticalVelocity = PlayerConstants.GroundedDownForce;

                if (jumpAction.action.WasPressedThisFrame())
                {
                    verticalVelocity = PlayerConstants.JumpForce;
                }
            }
            else
            {
                verticalVelocity += PlayerConstants.Gravity * PlayerConstants.FixedDeltaTime;
            }

            moveDirection.y = verticalVelocity;
            characterController.Move(moveDirection * PlayerConstants.FixedDeltaTime);
        }
    }
}
