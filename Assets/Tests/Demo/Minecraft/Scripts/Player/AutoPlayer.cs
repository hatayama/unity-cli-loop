using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    // simulate-keyboard cannot reproduce mouse-driven actions (look, click),
    // so this script uses random automation to exercise them during PlayMode
    [DisallowMultipleComponent]
    public class AutoPlayer : MonoBehaviour
    {
        private Transform playerBody;
        private Transform cameraTransform;
        private float pitch;

        private float lookTimer;
        private float targetYaw;
        private float targetPitch;
        private float lookLerpSpeed;

        private BlockInteraction blockInteraction;
        private World world;
        private float actionTimer;

        private HotbarUI hotbarUI;
        private float slotTimer;

        private void Start()
        {
            MinecraftPlayerCamera playerCamera = GetComponentInChildren<MinecraftPlayerCamera>();
            Debug.Assert(playerCamera != null, "MinecraftPlayerCamera not found in children");

            cameraTransform = playerCamera.transform;
            playerBody = transform;

            blockInteraction = GetComponent<BlockInteraction>();
            world = Object.FindFirstObjectByType<World>();
            hotbarUI = Object.FindFirstObjectByType<HotbarUI>();

            targetYaw = playerBody.eulerAngles.y;
            targetPitch = cameraTransform.localEulerAngles.x;
            if (targetPitch > 180f)
            {
                targetPitch -= 360f;
            }
            pitch = targetPitch;

            ResetLookTimer();
            ResetActionTimer();
            ResetSlotTimer();
        }

        private void Update()
        {
            UpdateLook();
            UpdateBlockAction();
            UpdateSlotChange();
        }

        private void UpdateLook()
        {
            lookTimer -= Time.deltaTime;
            if (lookTimer <= 0f)
            {
                targetYaw += Random.Range(-90f, 90f);
                targetPitch = Random.Range(PlayerConstants.MinPitch * 0.5f, PlayerConstants.MaxPitch * 0.3f);
                lookLerpSpeed = Random.Range(1f, 3f);
                ResetLookTimer();
            }

            float currentYaw = playerBody.eulerAngles.y;
            float newYaw = Mathf.LerpAngle(currentYaw, targetYaw, lookLerpSpeed * Time.deltaTime);
            playerBody.rotation = Quaternion.Euler(0f, newYaw, 0f);

            pitch = Mathf.Lerp(pitch, targetPitch, lookLerpSpeed * Time.deltaTime);
            pitch = Mathf.Clamp(pitch, PlayerConstants.MinPitch, PlayerConstants.MaxPitch);
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void UpdateBlockAction()
        {
            if (blockInteraction == null)
            {
                return;
            }

            actionTimer -= Time.deltaTime;
            if (actionTimer > 0f)
            {
                return;
            }

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, BlockConstants.InteractionRange))
            {
                ResetActionTimer();
                return;
            }

            if (world == null)
            {
                ResetActionTimer();
                return;
            }

            float roll = Random.value;
            if (roll < 0.4f)
            {
                Vector3Int blockPos = BlockHitHelper.GetHitBlockPosition(hit);
                world.SetBlock(blockPos.x, blockPos.y, blockPos.z, BlockType.Air);
            }
            else if (roll < 0.7f && hotbarUI != null)
            {
                Vector3Int placePos = BlockHitHelper.GetAdjacentBlockPosition(hit);
                world.SetBlock(placePos.x, placePos.y, placePos.z, hotbarUI.SelectedBlockType);
            }

            ResetActionTimer();
        }

        private void UpdateSlotChange()
        {
            if (hotbarUI == null)
            {
                return;
            }

            slotTimer -= Time.deltaTime;
            if (slotTimer > 0f)
            {
                return;
            }

            int direction = Random.value > 0.5f ? 1 : -1;
            hotbarUI.ScrollSlot(direction);
            ResetSlotTimer();
        }

        private void ResetLookTimer()
        {
            lookTimer = Random.Range(1.5f, 4f);
        }

        private void ResetActionTimer()
        {
            actionTimer = Random.Range(2f, 5f);
        }

        private void ResetSlotTimer()
        {
            slotTimer = Random.Range(4f, 8f);
        }
    }
}
