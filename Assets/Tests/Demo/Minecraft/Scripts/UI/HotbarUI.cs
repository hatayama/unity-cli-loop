using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public class HotbarUI : MonoBehaviour
    {
        [SerializeField] private HotbarSlotUI[] slots;
        [SerializeField] private BlockRegistry blockRegistry;

        private int selectedIndex;

        private static readonly BlockType[] SlotBlockTypes = new BlockType[]
        {
            BlockType.Grass,
            BlockType.Dirt,
            BlockType.Stone,
            BlockType.Wood,
            BlockType.Sand,
            BlockType.Brick,
            BlockType.Water
        };

        public BlockType SelectedBlockType => SlotBlockTypes[selectedIndex];

        private void Start()
        {
            Debug.Assert(slots != null && slots.Length > 0, "slots must be assigned in Inspector");
            Debug.Assert(blockRegistry != null, "blockRegistry must be assigned in Inspector");

            // World.Start may not have run yet, so initialize explicitly
            blockRegistry.Initialize();
            InitializeSlots();
            SelectSlot(0);
        }

        public void SelectSlot(int index)
        {
            Debug.Assert(index >= 0 && index < slots.Length, $"Slot index {index} is out of range");

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].SetSelected(i == index);
            }
            selectedIndex = index;
        }

        public void ScrollSlot(int direction)
        {
            int newIndex = (selectedIndex + direction + slots.Length) % slots.Length;
            SelectSlot(newIndex);
        }

        private void InitializeSlots()
        {
            for (int i = 0; i < slots.Length && i < SlotBlockTypes.Length; i++)
            {
                BlockDefinition definition = blockRegistry.GetDefinition(SlotBlockTypes[i]);
                slots[i].SetBlockInfo(definition.DisplayName, GetBlockColor(SlotBlockTypes[i]));
            }
        }

        private Color GetBlockColor(BlockType type)
        {
            switch (type)
            {
                case BlockType.Grass: return new Color(0.36f, 0.67f, 0.24f);
                case BlockType.Dirt: return new Color(0.55f, 0.36f, 0.20f);
                case BlockType.Stone: return new Color(0.50f, 0.50f, 0.50f);
                case BlockType.Wood: return new Color(0.55f, 0.38f, 0.18f);
                case BlockType.Sand: return new Color(0.86f, 0.80f, 0.55f);
                case BlockType.Brick: return new Color(0.65f, 0.30f, 0.20f);
                case BlockType.Water: return new Color(0.20f, 0.40f, 0.80f);
                default: return Color.white;
            }
        }
    }
}
