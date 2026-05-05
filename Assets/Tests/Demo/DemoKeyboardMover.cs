#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop
{
    public class DemoKeyboardMover : MonoBehaviour
    {
        private const float MoveSpeed = 3f;
        private const float SprintMultiplier = 2f;
        private const float GroundY = 0.5f;
        private const float JumpY = 1.5f;

        private Renderer _renderer = null!;
        private Vector3 _initialPosition;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            Debug.Assert(_renderer != null, "DemoKeyboardMover requires a Renderer");
            _initialPosition = transform.position;
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Vector3 direction = ReadDirection(keyboard);
            float speed = ReadSpeed(keyboard);
            transform.position += direction * speed * Time.deltaTime;
            ApplyVerticalInput(keyboard);
            ApplyColor(direction);
        }

        public void ResetPosition()
        {
            transform.position = _initialPosition;
            ApplyColor(Vector3.zero);
        }

        private static Vector3 ReadDirection(Keyboard keyboard)
        {
            Vector3 direction = Vector3.zero;

            if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
            {
                direction.z += 1f;
            }

            if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
            {
                direction.z -= 1f;
            }

            if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
            {
                direction.x -= 1f;
            }

            if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
            {
                direction.x += 1f;
            }

            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            return direction;
        }

        private static float ReadSpeed(Keyboard keyboard)
        {
            if (keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed)
            {
                return MoveSpeed * SprintMultiplier;
            }

            return MoveSpeed;
        }

        private void ApplyVerticalInput(Keyboard keyboard)
        {
            Vector3 position = transform.position;
            position.y = keyboard[Key.Space].isPressed || keyboard[Key.Enter].isPressed ? JumpY : GroundY;
            transform.position = position;
        }

        private void ApplyColor(Vector3 direction)
        {
            _renderer.material.color = direction == Vector3.zero ? Color.white : Color.cyan;
        }
    }
}
#endif
