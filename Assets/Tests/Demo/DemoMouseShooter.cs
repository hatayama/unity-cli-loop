#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DemoMouseShooter : MonoBehaviour
    {
        [SerializeField] private float bulletSpeed = 20f;
        [SerializeField] private float bulletLifetime = 3f;
        [SerializeField] private float bulletSpawnOffset = 0.3f;
        [SerializeField] private float jumpPower = 5f;

        private DemoWeaponSelector? _weaponSelector;

        private void Awake()
        {
            _weaponSelector = GetComponent<DemoWeaponSelector>();
        }

        private void Update()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                FireBullet();
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                Jump();
            }
        }

        private void FireBullet()
        {
            Vector3 fireDirection = transform.forward;

            Vector3 spawnPosition = transform.position + fireDirection * bulletSpawnOffset + Vector3.up;
            GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bullet.name = "Bullet";
            bullet.transform.position = spawnPosition;
            bullet.transform.localScale = Vector3.one * 0.3f;

            Color bulletColor = _weaponSelector != null ? _weaponSelector.SelectedColor : Color.yellow;
            Renderer renderer = bullet.GetComponent<Renderer>();
            renderer.material.color = bulletColor;

            Rigidbody rb = bullet.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.AddForce(fireDirection * bulletSpeed, ForceMode.VelocityChange);

            Destroy(bullet, bulletLifetime);
        }

        private static readonly int JumpTrigger = Animator.StringToHash("Jump");

        private void Jump()
        {
            Rigidbody? rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                return;
            }

            rb.AddForce(Vector3.up * jumpPower, ForceMode.VelocityChange);

            Animator? animator = GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(JumpTrigger);
            }
        }
    }
}
#endif
