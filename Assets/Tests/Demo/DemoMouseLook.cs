#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    // Orbits the Cinemachine camera around the character without rotating the character.
    // Mouse delta.x rotates the camera's FollowOffset around Y axis,
    // mouse delta.y tilts the camera pitch via the Composer's TrackedObjectOffset.
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DemoMouseLook : MonoBehaviour
    {
        [SerializeField] private float horizontalSensitivity = 0.4f;
        [SerializeField] private float verticalSensitivity = 0.05f;
        [SerializeField] private float minPitch = -2f;
        [SerializeField] private float maxPitch = 3f;

        private CinemachineTransposer? _transposer;
        private CinemachineComposer? _composer;
        private float _yaw;
        private float _pitch;
        private float _originalDistance;
        private float _originalY;
        private float _originalTrackedY;
        private CursorLockMode _previousCursorLockState;
        private bool _previousCursorVisible;

        private void Awake()
        {
            CinemachineVirtualCamera? vcam = FindObjectOfType<CinemachineVirtualCamera>();
            if (vcam == null)
            {
                return;
            }

            _transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
            _composer = vcam.GetCinemachineComponent<CinemachineComposer>();

            if (_transposer != null)
            {
                Vector3 offset = _transposer.m_FollowOffset;
                _originalDistance = new Vector2(offset.x, offset.z).magnitude;
                _originalY = offset.y;
                _yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }

            if (_composer != null)
            {
                _originalTrackedY = _composer.m_TrackedObjectOffset.y;
                _pitch = 0f;
            }
        }

        private void OnEnable()
        {
            _previousCursorLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = _previousCursorLockState;
            Cursor.visible = _previousCursorVisible;
        }

        private void Update()
        {
            if (_transposer == null)
            {
                return;
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (delta == Vector2.zero)
            {
                return;
            }

            // Horizontal: orbit camera around character
            _yaw += delta.x * horizontalSensitivity;

            float yawRad = _yaw * Mathf.Deg2Rad;
            _transposer.m_FollowOffset = new Vector3(
                Mathf.Sin(yawRad) * _originalDistance,
                _originalY,
                Mathf.Cos(yawRad) * _originalDistance
            );

            // Vertical: shift look target up/down
            if (_composer != null)
            {
                _pitch -= delta.y * verticalSensitivity;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

                Vector3 trackedOffset = _composer.m_TrackedObjectOffset;
                trackedOffset.y = _originalTrackedY + _pitch;
                _composer.m_TrackedObjectOffset = trackedOffset;
            }
        }
    }
}
#endif
