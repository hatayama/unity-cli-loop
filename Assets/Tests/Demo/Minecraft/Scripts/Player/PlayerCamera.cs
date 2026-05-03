using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop
{
    public class MinecraftPlayerCamera : MonoBehaviour
    {
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private Transform playerBody;

        private float pitch;

        private void Awake()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnEnable()
        {
            Debug.Assert(lookAction != null, "lookAction must be assigned in Inspector");
            Debug.Assert(playerBody != null, "playerBody must be assigned in Inspector");
            lookAction.action.Enable();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
        }

        private void Update()
        {
            Vector2 lookDelta = lookAction.action.ReadValue<Vector2>();

            float yawDelta = lookDelta.x * PlayerConstants.MouseSensitivity;
            float pitchDelta = lookDelta.y * PlayerConstants.MouseSensitivity;

            playerBody.Rotate(Vector3.up, yawDelta);

            pitch -= pitchDelta;
            pitch = Mathf.Clamp(pitch, PlayerConstants.MinPitch, PlayerConstants.MaxPitch);
            transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
