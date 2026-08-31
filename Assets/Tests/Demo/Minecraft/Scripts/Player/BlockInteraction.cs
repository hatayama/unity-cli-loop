using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class BlockInteraction : MonoBehaviour
    {
        [SerializeField] private InputActionReference attackAction;
        [SerializeField] private InputActionReference placeAction;
        [SerializeField] private InputActionReference scrollSlotAction;
        [SerializeField] private InputActionReference nextSlotAction;
        [SerializeField] private InputActionReference prevSlotAction;
        [SerializeField] private World world;
        [SerializeField] private HotbarUI hotbarUI;
        [SerializeField] private Camera playerCamera;

        private void OnEnable()
        {
            Debug.Assert(attackAction != null, "attackAction must be assigned in Inspector");
            Debug.Assert(placeAction != null, "placeAction must be assigned in Inspector");
            Debug.Assert(scrollSlotAction != null, "scrollSlotAction must be assigned in Inspector");
            Debug.Assert(nextSlotAction != null, "nextSlotAction must be assigned in Inspector");
            Debug.Assert(prevSlotAction != null, "prevSlotAction must be assigned in Inspector");
            Debug.Assert(world != null, "world must be assigned in Inspector");
            Debug.Assert(playerCamera != null, "playerCamera must be assigned in Inspector");

            attackAction.action.Enable();
            placeAction.action.Enable();
            scrollSlotAction.action.Enable();
            nextSlotAction.action.Enable();
            prevSlotAction.action.Enable();
        }

        private void OnDisable()
        {
            attackAction.action.Disable();
            placeAction.action.Disable();
            scrollSlotAction.action.Disable();
            nextSlotAction.action.Disable();
            prevSlotAction.action.Disable();
        }

        private void Update()
        {
            HandleSlotChange();

            if (attackAction.action.WasPressedThisFrame())
            {
                DestroyBlock();
            }

            if (placeAction.action.WasPressedThisFrame())
            {
                PlaceBlock();
            }
        }

        private void HandleSlotChange()
        {
            if (hotbarUI == null)
            {
                return;
            }

            Vector2 scrollValue = scrollSlotAction.action.ReadValue<Vector2>();
            if (scrollValue.y > 0f)
            {
                hotbarUI.ScrollSlot(-1);
            }
            else if (scrollValue.y < 0f)
            {
                hotbarUI.ScrollSlot(1);
            }

            if (nextSlotAction.action.WasPressedThisFrame())
            {
                hotbarUI.ScrollSlot(1);
            }

            if (prevSlotAction.action.WasPressedThisFrame())
            {
                hotbarUI.ScrollSlot(-1);
            }
        }

        private void DestroyBlock()
        {
            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            Vector3Int blockPos = BlockHitHelper.GetHitBlockPosition(hit);
            world.SetBlock(blockPos.x, blockPos.y, blockPos.z, BlockType.Air);
        }

        private void PlaceBlock()
        {
            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            if (hotbarUI == null)
            {
                return;
            }

            Vector3Int placePos = BlockHitHelper.GetAdjacentBlockPosition(hit);

            // Guard against placing a block inside the player
            Vector3 playerPos = transform.position;
            int playerBlockX = Mathf.FloorToInt(playerPos.x);
            int playerBlockZ = Mathf.FloorToInt(playerPos.z);
            int playerBlockYFeet = Mathf.FloorToInt(playerPos.y);
            int playerBlockYHead = Mathf.FloorToInt(playerPos.y + PlayerConstants.PlayerHeight);

            if (placePos.x == playerBlockX && placePos.z == playerBlockZ
                && placePos.y >= playerBlockYFeet && placePos.y <= playerBlockYHead)
            {
                return;
            }

            BlockType selectedType = hotbarUI.SelectedBlockType;
            world.SetBlock(placePos.x, placePos.y, placePos.z, selectedType);
        }

        private bool TryRaycast(out RaycastHit hit)
        {
            // Pull origin behind the camera so downward rays can hit blocks at the player's feet
            Vector3 origin = playerCamera.transform.position - playerCamera.transform.forward * BlockConstants.RaycastOriginPullback;
            Ray ray = new(origin, playerCamera.transform.forward);
            return Physics.Raycast(ray, out hit, BlockConstants.InteractionRange);
        }
    }
}
